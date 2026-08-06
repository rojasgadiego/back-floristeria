using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Mermas.Dtos;

namespace Colibri.Api.Features.Mermas;

public class MermasService : IMermasService
{
    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<MermasService> _log;

    public MermasService(
        ColibriDbContext db, IUsuarioActual usuarioActual, ILogger<MermasService> log)
    {
        _db = db;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    public async Task<ResultadoPagina<MermaDto>> ListarAsync(
        MermaFiltro filtro, CancellationToken ct = default)
    {
        var consulta = Proyectar();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(m =>
                m.Producto.ToLower().Contains(q) ||
                m.Motivo.ToLower().Contains(q) ||
                (m.LoteCodigo != null && m.LoteCodigo.ToLower().Contains(q)));
        }

        if (filtro.ProductoId.HasValue)
            consulta = consulta.Where(m => m.ProductoId == filtro.ProductoId.Value);

        if (filtro.LoteId.HasValue)
            consulta = consulta.Where(m => m.LoteId == filtro.LoteId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Destino))
            consulta = consulta.Where(m => m.Destino == ADestino(filtro.Destino).ToString());

        if (!string.IsNullOrWhiteSpace(filtro.Motivo))
        {
            var motivo = filtro.Motivo.Trim().ToLower();
            consulta = consulta.Where(m => m.Motivo.ToLower() == motivo);
        }

        if (filtro.Revertida.HasValue)
            consulta = consulta.Where(m => m.Revertida == filtro.Revertida.Value);

        if (filtro.Desde.HasValue)
        {
            var desde = new DateTimeOffset(
                filtro.Desde.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(m => m.Fecha >= desde);
        }

        if (filtro.Hasta.HasValue)
        {
            var hasta = new DateTimeOffset(
                filtro.Hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(m => m.Fecha < hasta);
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<MermaDto>.Crear(items, total, filtro);
    }

    public async Task<MermaDto> ObtenerAsync(int id, CancellationToken ct = default)
        => await Proyectar().FirstOrDefaultAsync(m => m.Id == id, ct)
           ?? throw new NoEncontradoException("La merma");

    /* ==================================================================
       REGISTRO
       ================================================================== */

    /// <summary>
    /// Registra una salida de inventario y decide qué pasa con lo que salió.
    ///
    /// No toda salida es pérdida: un pedido que el cliente pagó y no usó
    /// vuelve al stock, y una caja fallada devuelta al proveedor se abona.
    /// El costo se congela acá, y el que importa —costo_perdido— lo calcula
    /// la base descontando lo recuperado.
    /// </summary>
    public async Task<MermaDto> RegistrarAsync(
        RegistrarMermaRequest peticion, CancellationToken ct = default)
    {
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == peticion.ProductoId, ct)
            ?? throw new NoEncontradoException("El producto");

        var destino = ADestino(peticion.Destino);
        var recuperada = destino == DestinoMerma.reingreso ? peticion.CantidadRecuperada : 0;

        if (destino == DestinoMerma.reingreso && recuperada <= 0)
            throw new ExcepcionNegocio(
                "Con destino reingreso hay que indicar cuántas unidades vuelven al inventario.");

        if (recuperada > peticion.Cantidad)
            throw new ExcepcionNegocio(
                $"No pueden volver {recuperada} unidades si solo salieron {peticion.Cantidad}.");

        CalidadReingreso? calidad = recuperada > 0
            ? ACalidad(peticion.Calidad ?? throw new ExcepcionNegocio(
                "Indica en qué estado vuelve la flor: de eso depende su precio y su balde."))
            : null;

        if (peticion.CostoRecuperado.HasValue && recuperada == 0)
            throw new ExcepcionNegocio(
                "El costo de reingreso solo aplica si algo vuelve al inventario.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var merma = producto.Tipo == TipoProducto.armado
            ? await MermarArmadoAsync(producto, peticion, destino, recuperada, calidad, ct)
            : producto.ControlaLotes
                ? await MermarLoteAsync(producto, peticion, destino, recuperada, calidad, ct)
                : await MermarSimpleAsync(producto, peticion, destino, recuperada, ct);

        await tx.CommitAsync(ct);

        _log.LogWarning(
            "Merma de {Cantidad} × {Producto} ({Destino}). Recuperadas {Recuperadas}. " +
            "Motivo: {Motivo}. Registró {Autor}",
            merma.Cantidad, producto.Nombre, destino, recuperada,
            merma.Motivo, _usuarioActual.Email);

        return await ObtenerAsync(merma.Id, ct);
    }

    /// <summary>
    /// Da de baja el lote completo con lo que le quede. Es el destino de los
    /// rezagados: el resto que no se alcanzó a liquidar y ya no sirve.
    /// </summary>
    public async Task<MermaDto> DescartarLoteAsync(
        int loteId, DescartarLoteRequest peticion, CancellationToken ct = default)
    {
        var lote = await _db.Lotes
            .Include(l => l.Producto)
            .FirstOrDefaultAsync(l => l.Id == loteId, ct)
            ?? throw new NoEncontradoException("El lote");

        if (lote.Estado == EstadoLote.descartado)
            throw new ExcepcionNegocio($"El lote {lote.Codigo} ya fue descartado.");

        if (lote.VarasDisponibles == 0)
            throw new ExcepcionNegocio(
                $"El lote {lote.Codigo} ya se consumió completo: no queda nada que descartar.");

        var cantidad = lote.VarasDisponibles;
        var costoUnitario = (int)Math.Round(lote.CostoPorVara);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // Las dos columnas se escriben en el mismo UPDATE. Si se hiciera en
        // dos pasos, el CHECK lotes_estado_coherente rechazaría el estado
        // intermedio: 'activo' con cero varas.
        lote.VarasDisponibles = 0;
        lote.Estado = EstadoLote.descartado;

        var merma = new Merma
        {
            ProductoId = lote.ProductoId,
            LoteId = lote.Id,
            Cantidad = cantidad,
            Destino = peticion.EsDevolucionProveedor
                ? DestinoMerma.devolucion_proveedor
                : DestinoMerma.perdida,
            Motivo = peticion.Motivo.Trim(),
            Detalle = Limpiar(peticion.Detalle),
            CostoUnitario = costoUnitario,
            CostoTotal = costoUnitario * cantidad,
            UsuarioId = _usuarioActual.Id
        };

        _db.Mermas.Add(merma);
        await _db.SaveChangesAsync(ct);

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = lote.ProductoId,
            LoteId = lote.Id,
            Tipo = TipoMovimiento.merma,
            Cantidad = -cantidad,
            StockResultante = await StockActualAsync(lote.ProductoId, ct),
            Motivo = $"Descarte del lote {lote.Codigo} · {merma.Motivo}",
            Detalle = merma.Detalle,
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "merma",
            ReferenciaId = merma.Id
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogWarning(
            "Lote {Codigo} descartado: {Cantidad} varas de {Producto}. Destino {Destino}. " +
            "Motivo: {Motivo}. Registró {Autor}",
            lote.Codigo, cantidad, lote.Producto.Nombre, merma.Destino,
            merma.Motivo, _usuarioActual.Email);

        return await ObtenerAsync(merma.Id, ct);
    }

    /// <summary>
    /// Revierte una merma registrada por error.
    ///
    /// Si hubo recuperación, primero se quitan las varas del lote donde
    /// quedaron y después se devuelve el total al de origen. Cuando ambos son
    /// el mismo lote —flor óptima— la resta y la suma se compensan y queda
    /// exactamente lo perdido.
    /// </summary>
    public async Task<MermaDto> RevertirAsync(
        int id, RevertirMermaRequest peticion, CancellationToken ct = default)
    {
        var merma = await _db.Mermas.FirstOrDefaultAsync(m => m.Id == id, ct)
            ?? throw new NoEncontradoException("La merma");

        if (merma.Revertida)
            throw new ExcepcionNegocio("Esta merma ya fue revertida.");

        var producto = await _db.Productos.FirstAsync(p => p.Id == merma.ProductoId, ct);

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // 1. Deshacer el reingreso: las varas recuperadas salen de donde
        //    quedaron. Si ya se vendieron, no hay forma de revertir sin
        //    inventar stock, así que se rechaza con un mensaje claro.
        if (merma.CantidadRecuperada > 0 && merma.LoteRecuperacionId.HasValue)
        {
            var destino = await _db.Lotes.FirstAsync(l => l.Id == merma.LoteRecuperacionId.Value, ct);

            if (destino.VarasDisponibles < merma.CantidadRecuperada)
                throw new ExcepcionNegocio(
                    $"No se puede revertir: de las {merma.CantidadRecuperada} varas que " +
                    $"volvieron al lote {destino.Codigo} quedan {destino.VarasDisponibles}. " +
                    "El resto ya se vendió o se consumió.");

            destino.VarasDisponibles -= merma.CantidadRecuperada;
        }

        // 2. Devolver todo lo que salió a su lote de origen
        if (merma.LoteId.HasValue)
        {
            var origen = await _db.Lotes.FirstAsync(l => l.Id == merma.LoteId.Value, ct);

            if (origen.VarasDisponibles + merma.Cantidad > origen.VarasIniciales)
                throw new ExcepcionNegocio(
                    $"No se pueden devolver {merma.Cantidad} varas al lote {origen.Codigo}: " +
                    $"superaría las {origen.VarasIniciales} que traía.");

            // Estado y cantidad en el mismo UPDATE: 'descartado' con varas o
            // 'activo' con cero no pasan el CHECK de coherencia.
            if (origen.Estado == EstadoLote.descartado)
                origen.Estado = EstadoLote.activo;

            origen.VarasDisponibles += merma.Cantidad;
        }
        else if (producto.Tipo == TipoProducto.armado)
        {
            producto.StockListo = (producto.StockListo ?? 0) + merma.Cantidad;
        }
        else
        {
            producto.Stock = (producto.Stock ?? 0) + merma.Cantidad;
        }

        merma.Revertida = true;
        merma.RevertidaPor = _usuarioActual.Id;
        merma.RevertidaEn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = merma.ProductoId,
            LoteId = merma.LoteId,
            Tipo = TipoMovimiento.entrada,
            Cantidad = merma.Cantidad - merma.CantidadRecuperada,
            StockResultante = await StockActualAsync(merma.ProductoId, ct),
            Motivo = $"Reversa de merma #{merma.Id}",
            Detalle = peticion.Motivo.Trim(),
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "merma",
            ReferenciaId = merma.Id
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogWarning("Merma #{Id} revertida por {Autor}: {Motivo}",
            merma.Id, _usuarioActual.Email, peticion.Motivo);

        return await ObtenerAsync(id, ct);
    }

    /* ==================================================================
       DESARME
       ================================================================== */

    /// <summary>
    /// Plan sugerido para desarmar. Rastrea de qué lote salió cada tallo
    /// mirando los movimientos del armado, para que la persona no tenga que
    /// recordarlo ni adivinarlo.
    /// </summary>
    public async Task<PlanDesarmeDto> PlanDesarmeAsync(
        int productoId, int cantidad, CancellationToken ct = default)
    {
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new NoEncontradoException("El producto");

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo se desarman los productos armados.");

        var unidades = Math.Max(1, cantidad);
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var receta = await _db.Recetas.AsNoTracking()
            .Where(r => r.ProductoId == productoId)
            .Select(r => new
            {
                r.ComponenteId,
                r.Cantidad,
                Nombre = r.Componente.Nombre,
                r.Componente.Emoji,
                r.Componente.Precio,
                r.Componente.ControlaLotes
            })
            .ToListAsync(ct);

        if (receta.Count == 0)
            throw new ExcepcionNegocio("Este producto no tiene receta: no hay qué recuperar.");

        var lineas = new List<LineaPlanDto>();

        foreach (var ingrediente in receta)
        {
            var linea = new LineaPlanDto
            {
                ComponenteId = ingrediente.ComponenteId,
                Componente = ingrediente.Nombre,
                Emoji = ingrediente.Emoji,
                Cantidad = ingrediente.Cantidad * unidades,
                PrecioLista = ingrediente.Precio
            };

            if (ingrediente.ControlaLotes)
            {
                // El último consumo por armado de este componente dice de qué
                // lote salieron los tallos. Es lo más cercano a la verdad que
                // se puede saber sin marcar cada vara.
                var origen = await _db.MovimientosInventario.AsNoTracking()
                    .Where(m => m.ProductoId == ingrediente.ComponenteId
                             && m.Tipo == TipoMovimiento.consumo
                             && m.ReferenciaTipo == "armado"
                             && m.ReferenciaId == productoId
                             && m.LoteId != null)
                    .OrderByDescending(m => m.CreadoEn)
                    .Select(m => new
                    {
                        m.LoteId,
                        Codigo = m.Lote!.Codigo,
                        m.Lote.FechaIngreso,
                        m.Lote.Estado
                    })
                    .FirstOrDefaultAsync(ct);

                if (origen is not null)
                {
                    linea.LoteOrigenId = origen.LoteId;
                    linea.LoteOrigenCodigo = origen.Codigo;
                    linea.LoteIngreso = origen.FechaIngreso;
                    linea.DiasEnCamara = hoy.DayNumber - origen.FechaIngreso.DayNumber;
                }
            }

            lineas.Add(linea);
        }

        return new PlanDesarmeDto
        {
            ProductoId = producto.Id,
            Producto = producto.Nombre,
            Cantidad = unidades,
            StockListo = producto.StockListo ?? 0,
            Lineas = lineas
        };
    }

    /// <summary>
    /// Desarma unidades armadas y clasifica sus varas.
    ///
    /// La clasificación es por vara y no por conjunto: del mismo ramo pueden
    /// salir ocho rosas óptimas que vuelven a su balde y cuatro buenas que
    /// van a uno aparte con precio rebajado. Por eso se reciben varias líneas
    /// por componente.
    /// </summary>
    public async Task<ResultadoDesarmeDto> DesarmarAsync(
        int productoId, DesarmarRequest peticion, CancellationToken ct = default)
    {
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new NoEncontradoException("El producto");

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo se desarman los productos armados.");

        if ((producto.StockListo ?? 0) < peticion.Cantidad)
            throw new ExcepcionNegocio(
                $"Hay {producto.StockListo} unidad(es) armada(s) de {producto.Nombre} " +
                $"y se intentan desarmar {peticion.Cantidad}.");

        var receta = await _db.Recetas.AsNoTracking()
            .Where(r => r.ProductoId == productoId)
            .Select(r => new { r.ComponenteId, r.Cantidad, r.Componente.Nombre })
            .ToListAsync(ct);

        if (receta.Count == 0)
            throw new ExcepcionNegocio("Este producto no tiene receta: no hay qué recuperar.");

        // Las líneas tienen que cuadrar con la receta: si no, quedarían varas
        // sin destino, saliendo del ramo pero sin entrar ni salir del stock.
        foreach (var ingrediente in receta)
        {
            var esperado = ingrediente.Cantidad * peticion.Cantidad;
            var declarado = peticion.Lineas
                .Where(l => l.ComponenteId == ingrediente.ComponenteId)
                .Sum(l => l.Cantidad);

            if (declarado != esperado)
                throw new ExcepcionNegocio(
                    $"De {ingrediente.Nombre} salen {esperado} vara(s) y se declararon " +
                    $"{declarado}. Cada vara tiene que tener un destino.");
        }

        var ajenas = peticion.Lineas
            .Select(l => l.ComponenteId)
            .Except(receta.Select(r => r.ComponenteId))
            .ToList();

        if (ajenas.Count > 0)
            throw new ExcepcionNegocio(
                $"Hay líneas de productos que no están en la receta: {string.Join(", ", ajenas)}.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        producto.StockListo -= peticion.Cantidad;

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.baja,
            Cantidad = -peticion.Cantidad,
            StockResultante = producto.StockListo,
            Motivo = peticion.Motivo.Trim(),
            Detalle = Limpiar(peticion.Detalle),
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "desarme",
            ReferenciaId = producto.Id
        });

        await _db.SaveChangesAsync(ct);

        var resultado = new List<ResultadoLineaDesarmeDto>();
        var recuperadas = 0;
        var perdidas = 0;
        var costoPerdido = 0;

        foreach (var linea in peticion.Lineas)
        {
            var componente = await _db.Productos.FirstAsync(p => p.Id == linea.ComponenteId, ct);
            var destino = ADestino(linea.Destino);
            var nombre = receta.First(r => r.ComponenteId == linea.ComponenteId).Nombre;

            if (destino == DestinoMerma.reingreso)
            {
                var calidad = ACalidad(linea.Calidad ?? throw new ExcepcionNegocio(
                    $"Indica en qué estado vuelven las {linea.Cantidad} vara(s) de {nombre}."));


                var reingreso = await ReingresarAsync(
                    componente, linea.Cantidad, calidad, linea.LoteOrigenId,
                    linea.PrecioUnitario, linea.CostoRecuperado,
                    $"{peticion.Motivo.Trim()} · {producto.Nombre}",
                    "desarme", producto.Id, ct);

                recuperadas += linea.Cantidad;

                // Si vuelven valiendo menos, esa diferencia es pérdida y
                // tiene que quedar registrada: si no, el valor se esfumaría
                // del inventario sin aparecer en ningún resultado.
                if (linea.CostoRecuperado.HasValue)
                {
                    var costoOriginal = componente.ControlaLotes && linea.LoteOrigenId.HasValue
                        ? (int)Math.Round(await _db.Lotes.AsNoTracking()
                            .Where(l => l.Id == linea.LoteOrigenId.Value)
                            .Select(l => l.CostoPorVara).FirstOrDefaultAsync(ct))
                        : componente.Costo ?? 0;

                    if (linea.CostoRecuperado.Value < costoOriginal)
                    {
                        _db.Mermas.Add(new Merma
                        {
                            ProductoId = componente.Id,
                            LoteId = linea.LoteOrigenId,
                            Cantidad = linea.Cantidad,
                            Destino = DestinoMerma.reingreso,
                            CantidadRecuperada = linea.Cantidad,
                            CalidadReingreso = calidad,
                            CostoRecuperadoUnitario = linea.CostoRecuperado.Value,
                            LoteRecuperacionId = reingreso.LoteId > 0 ? reingreso.LoteId : null,
                            Motivo = $"Deterioro por desarme · {producto.Nombre}",
                            Detalle = Limpiar(peticion.Detalle),
                            CostoUnitario = costoOriginal,
                            CostoTotal = costoOriginal * linea.Cantidad,
                            UsuarioId = _usuarioActual.Id
                        });

                        await _db.SaveChangesAsync(ct);
                        costoPerdido += (costoOriginal - linea.CostoRecuperado.Value) * linea.Cantidad;
                    }
                }

                resultado.Add(new ResultadoLineaDesarmeDto
                {
                    ComponenteId = componente.Id,
                    Componente = nombre,
                    Cantidad = linea.Cantidad,
                    Destino = destino.ToString(),
                    Calidad = calidad.ToString(),
                    LoteDestino = reingreso.Codigo,
                    EsLoteNuevo = reingreso.EsNuevo,
                    PrecioUnitario = linea.PrecioUnitario,
                    CostoRecuperado = linea.CostoRecuperado
                });

                continue;
            }

            // Perdidas: no vuelven, pero tampoco salieron del stock —los
            // tallos se descontaron al armar el ramo—. Se registra la merma
            // para que la pérdida quede valorizada y explicada.
            var costo = componente.ControlaLotes && linea.LoteOrigenId.HasValue
                ? (int)Math.Round(await _db.Lotes.AsNoTracking()
                    .Where(l => l.Id == linea.LoteOrigenId.Value)
                    .Select(l => l.CostoPorVara).FirstOrDefaultAsync(ct))
                : componente.Costo ?? 0;

            var merma = new Merma
            {
                ProductoId = componente.Id,
                LoteId = linea.LoteOrigenId,
                Cantidad = linea.Cantidad,
                Destino = DestinoMerma.perdida,
                Motivo = $"{peticion.Motivo.Trim()} · {producto.Nombre}",
                Detalle = Limpiar(peticion.Detalle),
                CostoUnitario = costo,
                CostoTotal = costo * linea.Cantidad,
                UsuarioId = _usuarioActual.Id
            };

            _db.Mermas.Add(merma);
            await _db.SaveChangesAsync(ct);

            perdidas += linea.Cantidad;
            costoPerdido += merma.CostoTotal;

            resultado.Add(new ResultadoLineaDesarmeDto
            {
                ComponenteId = componente.Id,
                Componente = nombre,
                Cantidad = linea.Cantidad,
                Destino = destino.ToString()
            });
        }

        await tx.CommitAsync(ct);

        _log.LogInformation(
            "Desarmadas {Cantidad} unidad(es) de {Producto}: {Recuperadas} varas recuperadas, " +
            "{Perdidas} perdidas. Registró {Autor}",
            peticion.Cantidad, producto.Nombre, recuperadas, perdidas, _usuarioActual.Email);

        return new ResultadoDesarmeDto
        {
            ProductoId = producto.Id,
            Producto = producto.Nombre,
            Desarmadas = peticion.Cantidad,
            StockListo = producto.StockListo ?? 0,
            VarasRecuperadas = recuperadas,
            VarasPerdidas = perdidas,
            CostoPerdido = costoPerdido,
            Lineas = resultado
        };
    }

    /* ==================================================================
       REPORTE
       ================================================================== */

    public async Task<ResumenMermasDto> ResumenAsync(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var inicio = desde ?? hoy.AddDays(-30);
        var fin = hasta ?? hoy;

        var desdeDt = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaDt = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var mermas = await _db.Mermas.AsNoTracking()
            .Where(m => !m.Revertida && m.CreadoEn >= desdeDt && m.CreadoEn < hastaDt)
            .Select(m => new
            {
                m.ProductoId,
                Producto = m.Producto.Nombre,
                m.Producto.Emoji,
                m.Cantidad,
                m.CantidadRecuperada,
                m.CostoTotal,
                m.CostoPerdido,
                m.CostoUnitario,
                m.CostoRecuperadoUnitario,
                m.Motivo,
                m.Destino

            })
            .ToListAsync(ct);

        var vendido = await _db.Ventas.AsNoTracking()
            .Where(v => !v.Anulada && v.CreadoEn >= desdeDt && v.CreadoEn < hastaDt)
            .SumAsync(v => (long?)v.Total, ct) ?? 0;

        // Cuánto se compró de cada producto en el período: sin ese
        // denominador, "se perdieron 40 rosas" no dice si es mucho o poco.
        var comprado = await _db.CompraItems.AsNoTracking()
            .Where(ci => ci.Compra.Estado == EstadoCompra.recibida
                      && ci.Compra.Fecha >= inicio && ci.Compra.Fecha <= fin)
            .GroupBy(ci => ci.ProductoId)
            .Select(g => new { ProductoId = g.Key, Varas = g.Sum(x => x.VarasTotales) })
            .ToDictionaryAsync(x => x.ProductoId, x => x.Varas, ct);

        var porDestino = mermas
            .GroupBy(m => m.Destino)
            .Select(g => new MermaPorDestinoDto
            {
                Destino = g.Key.ToString(),
                Registros = g.Count(),
                Unidades = g.Sum(x => x.Cantidad),
                CostoMovido = g.Sum(x => x.CostoTotal),
                CostoBotado = g.Sum(x => x.CostoUnitario * (x.Cantidad - x.CantidadRecuperada)),
                CostoDesvalorizado = g.Sum(x =>
                    (x.CostoUnitario - (x.CostoRecuperadoUnitario ?? x.CostoUnitario))
                    * x.CantidadRecuperada),
                CostoPerdido = g.Sum(x => x.CostoPerdido)

            })
            .OrderByDescending(d => d.CostoPerdido)
            .ToList();

        var porProducto = mermas
            .GroupBy(m => new { m.ProductoId, m.Producto, m.Emoji })
            .Select(g =>
            {
                var perdidas = g.Sum(x => x.Cantidad - x.CantidadRecuperada);
                comprado.TryGetValue(g.Key.ProductoId, out var compradas);

                return new MermaPorProductoDto
                {
                    ProductoId = g.Key.ProductoId,
                    Producto = g.Key.Producto,
                    Emoji = g.Key.Emoji,
                    UnidadesMovidas = g.Sum(x => x.Cantidad),
                    UnidadesPerdidas = perdidas,
                    CostoPerdido = g.Sum(x => x.CostoPerdido),
                    PorcentajeDeLoComprado = compradas > 0
                        ? Math.Round(100m * perdidas / compradas, 1)
                        : null,


                };
            })
            .OrderByDescending(p => p.CostoPerdido)
            .ToList();

        var porMotivo = mermas
            .GroupBy(m => m.Motivo)
            .Select(g => new MermaPorMotivoDto
            {
                Motivo = g.Key,
                Registros = g.Count(),
                Unidades = g.Sum(x => x.Cantidad),
                CostoPerdido = g.Sum(x => x.CostoPerdido)
            })
            .OrderByDescending(m => m.CostoPerdido)
            .ToList();

        var costoMovido = mermas.Sum(m => m.CostoTotal);
        var costoPerdido = mermas.Sum(m => m.CostoPerdido);

        return new ResumenMermasDto
        {
            Desde = inicio,
            Hasta = fin,
            Registros = mermas.Count,
            UnidadesMovidas = mermas.Sum(m => m.Cantidad),
            UnidadesRecuperadas = mermas.Sum(m => m.CantidadRecuperada),
            UnidadesPerdidas = mermas.Sum(m => m.Cantidad - m.CantidadRecuperada),
            CostoMovido = costoMovido,
            CostoPerdido = costoPerdido,
            CostoRecuperado = costoMovido - costoPerdido,
            CostoBotado = mermas.Sum(m => m.CostoUnitario * (m.Cantidad - m.CantidadRecuperada)),
            CostoDesvalorizado = mermas.Sum(m =>
                (m.CostoUnitario - (m.CostoRecuperadoUnitario ?? m.CostoUnitario))
                * m.CantidadRecuperada),
            Vendido = vendido,
            // Sobre la pérdida real, no sobre lo movido: si no, un pedido
            // devuelto en perfecto estado inflaría el indicador.
            PorcentajeSobreVentas = vendido > 0
                ? Math.Round(100m * costoPerdido / vendido, 2)
                : 0,
            PorDestino = porDestino,
            PorProducto = porProducto,
            PorMotivo = porMotivo,

        };
    }

    /// <summary>
    /// Motivos habituales, para que la interfaz los ofrezca. Escribir el
    /// motivo libre cada vez produce "marchita", "Marchita" y "se marchitó"
    /// como tres categorías distintas, y el reporte deja de servir.
    /// </summary>
    public IReadOnlyList<string> MotivosSugeridos() => new[]
    {
        "Flor marchita",
        "Vencimiento",
        "Daño en manipulación",
        "Rotura",
        "Error de armado",
        "Pedido no retirado",
        "Devolución del cliente",
        "Devolución a proveedor",
        "Desarme de producto sin vender",
        "Ajuste por conteo",
        "Regalo o degustación"
    };

    /* ==================================================================
       INTERNO
       ================================================================== */

    /// <summary>
    /// Devuelve varas al inventario llamando a fn_reingresar_lote.
    ///
    /// La función decide dónde quedan: si la calidad es óptima y hay lote de
    /// origen vuelven ahí; si no, crea un lote de recuperación que hereda la
    /// fecha de ingreso y el vencimiento del origen. La flor no rejuvenece
    /// por volver al balde.
    /// </summary>
    private async Task<ReingresoLote> ReingresarAsync(
        Producto componente, int cantidad, CalidadReingreso calidad,
        int? loteOrigenId, int? precioUnitario, int? costoRecuperado, string motivo,
        string referenciaTipo, int referenciaId, CancellationToken ct)
    {
        if (!componente.ControlaLotes)
        {
            // Sin lotes, el reingreso es una suma directa al stock
            componente.Stock = (componente.Stock ?? 0) + cantidad;

            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = componente.Id,
                Tipo = TipoMovimiento.entrada,
                Cantidad = cantidad,
                StockResultante = componente.Stock,
                Motivo = motivo,
                UsuarioId = _usuarioActual.Id,
                ReferenciaTipo = referenciaTipo,
                ReferenciaId = referenciaId
            });

            await _db.SaveChangesAsync(ct);

            return new ReingresoLote
            {
                LoteId = 0,
                Codigo = "sin lote",
                EsNuevo = false,
                FechaIngreso = DateOnly.FromDateTime(DateTime.Today)
            };
        }

        var usuarioId = _usuarioActual.Id;

        // El costo va como NUMERIC: si es null, la función hereda el del
        // lote de origen. Bajarlo es lo que hace que un ramo armado con flor
        // reutilizada cueste menos que uno con flor nueva.
        decimal? costo = costoRecuperado;

        var filas = await _db.ReingresosLote
            .FromSqlInterpolated($@"
                SELECT * FROM fn_reingresar_lote(
                    {componente.Id}, {cantidad}, {calidad.ToString()}::calidad_reingreso,
                    {motivo}, {usuarioId}, {loteOrigenId}, {precioUnitario},
                    {costo}, NULL, {referenciaTipo}, {referenciaId})")
            .ToListAsync(ct);

        return filas.FirstOrDefault()
            ?? throw new ExcepcionNegocio("No se pudo reingresar la flor al inventario.");
    }

    private async Task<Merma> MermarLoteAsync(
        Producto producto, RegistrarMermaRequest peticion, DestinoMerma destino,
        int recuperada, CalidadReingreso? calidad, CancellationToken ct)
    {
        if (!peticion.LoteId.HasValue)
            throw new ExcepcionNegocio(
                $"{producto.Nombre} se controla por lote: indica de qué lote salió. " +
                "Sin eso, la pérdida no se puede valorizar ni rastrear.");

        var lote = await _db.Lotes.FirstOrDefaultAsync(l => l.Id == peticion.LoteId.Value, ct)
            ?? throw new NoEncontradoException("El lote");

        if (lote.ProductoId != producto.Id)
            throw new ExcepcionNegocio($"El lote {lote.Codigo} no pertenece a {producto.Nombre}.");

        if (lote.Estado == EstadoLote.descartado)
            throw new ExcepcionNegocio($"El lote {lote.Codigo} ya fue descartado.");

        if (lote.VarasDisponibles < peticion.Cantidad)
            throw new ExcepcionNegocio(
                $"El lote {lote.Codigo} solo tiene {lote.VarasDisponibles} vara(s) " +
                $"y se intentan mover {peticion.Cantidad}.");

        lote.VarasDisponibles -= peticion.Cantidad;

        // El costo real del lote, no el de reposición del producto: dos rosas
        // iguales de lotes distintos no costaron lo mismo.
        var costoUnitario = (int)Math.Round(lote.CostoPorVara);

        var merma = new Merma
        {
            ProductoId = producto.Id,
            LoteId = lote.Id,
            Cantidad = peticion.Cantidad,
            Destino = destino,
            CantidadRecuperada = recuperada,
            CalidadReingreso = calidad,
            // La diferencia contra el costo original es pérdida por deterioro:
            // la suma costo_perdido, que es columna generada en la base.
            CostoRecuperadoUnitario = recuperada > 0 ? peticion.CostoRecuperado : null,
            Motivo = peticion.Motivo.Trim(),
            Detalle = Limpiar(peticion.Detalle),
            CostoUnitario = costoUnitario,
            CostoTotal = costoUnitario * peticion.Cantidad,
            UsuarioId = _usuarioActual.Id
        };

        _db.Mermas.Add(merma);
        await _db.SaveChangesAsync(ct);

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = producto.Id,
            LoteId = lote.Id,
            Tipo = TipoMovimiento.merma,
            Cantidad = -peticion.Cantidad,
            StockResultante = await StockActualAsync(producto.Id, ct),
            Motivo = $"{merma.Motivo} · lote {lote.Codigo}",
            Detalle = merma.Detalle,
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "merma",
            ReferenciaId = merma.Id
        });

        await _db.SaveChangesAsync(ct);

        if (recuperada > 0 && calidad.HasValue)
        {
            var reingreso = await ReingresarAsync(
                producto, recuperada, calidad.Value, lote.Id,
                peticion.PrecioRecuperado, peticion.CostoRecuperado,
                merma.Motivo, "merma", merma.Id, ct);

            merma.LoteRecuperacionId = reingreso.LoteId > 0 ? reingreso.LoteId : null;
            await _db.SaveChangesAsync(ct);
        }

        return merma;
    }

    private async Task<Merma> MermarSimpleAsync(
        Producto producto, RegistrarMermaRequest peticion,
        DestinoMerma destino, int recuperada, CancellationToken ct)
    {
        if ((producto.Stock ?? 0) < peticion.Cantidad)
            throw new ExcepcionNegocio(
                $"{producto.Nombre} tiene {producto.Stock} en stock y se intentan " +
                $"mover {peticion.Cantidad}.");

        // Sin lotes, lo que sale y lo que vuelve se compensa en el mismo stock
        producto.Stock -= peticion.Cantidad - recuperada;

        var costoUnitario = producto.Costo ?? 0;

        var merma = new Merma
        {
            ProductoId = producto.Id,
            Cantidad = peticion.Cantidad,
            Destino = destino,
            CantidadRecuperada = recuperada,
            CalidadReingreso = recuperada > 0 ? CalidadReingreso.optima : null,
            Motivo = peticion.Motivo.Trim(),
            Detalle = Limpiar(peticion.Detalle),
            CostoUnitario = costoUnitario,
            CostoTotal = costoUnitario * peticion.Cantidad,
            UsuarioId = _usuarioActual.Id
        };

        _db.Mermas.Add(merma);
        await _db.SaveChangesAsync(ct);

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.merma,
            Cantidad = -(peticion.Cantidad - recuperada),
            StockResultante = producto.Stock,
            Motivo = merma.Motivo,
            Detalle = merma.Detalle,
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "merma",
            ReferenciaId = merma.Id
        });

        await _db.SaveChangesAsync(ct);
        return merma;
    }

    /// <summary>
    /// Merma de un ramo ya armado. Para recuperar sus tallos hay que
    /// desarmarlo: acá la unidad se pierde entera.
    /// </summary>
    private async Task<Merma> MermarArmadoAsync(
        Producto producto, RegistrarMermaRequest peticion, DestinoMerma destino,
        int recuperada, CalidadReingreso? calidad, CancellationToken ct)
    {
        if (peticion.LoteId.HasValue)
            throw new ExcepcionNegocio(
                "Un producto armado no tiene lote propio: lo tienen sus tallos.");

        if (recuperada > 0)
            throw new ExcepcionNegocio(
                "Para recuperar las varas de un producto armado hay que desarmarlo: " +
                "usa el desarme, que permite clasificar cada vara por separado.");

        if ((producto.StockListo ?? 0) < peticion.Cantidad)
            throw new ExcepcionNegocio(
                $"Hay {producto.StockListo} unidad(es) armada(s) de {producto.Nombre} " +
                $"y se intentan mermar {peticion.Cantidad}.");

        producto.StockListo -= peticion.Cantidad;

        var costoUnitario = await _db.ProductoCostos.AsNoTracking()
            .Where(c => c.ProductoId == producto.Id)
            .Select(c => c.CostoUnitario)
            .FirstOrDefaultAsync(ct);

        var merma = new Merma
        {
            ProductoId = producto.Id,
            Cantidad = peticion.Cantidad,
            Destino = destino,
            Motivo = peticion.Motivo.Trim(),
            Detalle = Limpiar(peticion.Detalle),
            CostoUnitario = costoUnitario,
            CostoTotal = costoUnitario * peticion.Cantidad,
            UsuarioId = _usuarioActual.Id
        };

        _db.Mermas.Add(merma);
        await _db.SaveChangesAsync(ct);

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = producto.Id,
            Tipo = TipoMovimiento.merma,
            Cantidad = -peticion.Cantidad,
            StockResultante = producto.StockListo,
            Motivo = merma.Motivo,
            Detalle = merma.Detalle,
            UsuarioId = _usuarioActual.Id,
            ReferenciaTipo = "merma",
            ReferenciaId = merma.Id
        });

        await _db.SaveChangesAsync(ct);
        return merma;
    }

    /// <summary>
    /// Stock después del movimiento. En productos con lote lo mantiene un
    /// trigger sumando los lotes activos, así que se relee en vez de
    /// calcularlo acá.
    /// </summary>
    private async Task<int?> StockActualAsync(int productoId, CancellationToken ct)
        => await _db.Productos.AsNoTracking()
            .Where(p => p.Id == productoId)
            .Select(p => p.Stock)
            .FirstOrDefaultAsync(ct);

    private IQueryable<MermaDto> Proyectar()
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        return from m in _db.Mermas.AsNoTracking()
               join lr in _db.Lotes on m.LoteRecuperacionId equals lr.Id into recup
               from lr in recup.DefaultIfEmpty()
               select new MermaDto
               {
                   Id = m.Id,
                   Fecha = m.CreadoEn,
                   ProductoId = m.ProductoId,
                   Producto = m.Producto.Nombre,
                   Emoji = m.Producto.Emoji,
                   Categoria = m.Producto.Categoria.Nombre,
                   LoteId = m.LoteId,
                   LoteCodigo = m.Lote != null ? m.Lote.Codigo : null,
                   LoteIngreso = m.Lote != null ? m.Lote.FechaIngreso : null,
                   DiasEnCamara = m.Lote != null
                       ? hoy.DayNumber - m.Lote.FechaIngreso.DayNumber
                       : null,
                   Cantidad = m.Cantidad,
                   CantidadRecuperada = m.CantidadRecuperada,
                   Destino = m.Destino.ToString(),
                   CalidadReingreso = m.CalidadReingreso != null
                       ? m.CalidadReingreso.ToString()
                       : null,
                   LoteRecuperacionId = m.LoteRecuperacionId,
                   LoteRecuperacionCodigo = lr != null ? lr.Codigo : null,
                   CostoRecuperadoUnitario = m.CostoRecuperadoUnitario,
                   Motivo = m.Motivo,
                   Detalle = m.Detalle,
                   CostoUnitario = m.CostoUnitario,
                   CostoTotal = m.CostoTotal,
                   CostoPerdido = m.CostoPerdido,
                   Usuario = m.Usuario != null ? m.Usuario.Nombre : null,
                   Revertida = m.Revertida,
                   RevertidaEn = m.RevertidaEn
               };
    }

    private static string? Limpiar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static DestinoMerma ADestino(string valor)
        => Enum.TryParse<DestinoMerma>(valor?.Trim().ToLowerInvariant(), out var d)
            ? d
            : throw new ExcepcionNegocio(
                "Destino no válido. Debe ser perdida, reingreso o devolucion_proveedor.");

    private static CalidadReingreso ACalidad(string valor)
        => Enum.TryParse<CalidadReingreso>(valor?.Trim().ToLowerInvariant(), out var c)
            ? c
            : throw new ExcepcionNegocio(
                "Calidad no válida. Debe ser optima, buena o limitada.");
}