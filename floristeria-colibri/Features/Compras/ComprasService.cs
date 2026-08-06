using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Compras.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Colibri.Api.Features.Compras;

public class ComprasService : IComprasService
{
    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<ComprasService> _log;

    public ComprasService(
        ColibriDbContext db, IUsuarioActual usuarioActual, ILogger<ComprasService> log)
    {
        _db = db;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       PROVEEDORES
       ================================================================== */

    public async Task<ResultadoPagina<ProveedorDto>> ListarProveedoresAsync(
        ProveedorFiltro filtro, CancellationToken ct = default)
    {
        var consulta = ProyectarProveedores();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(p =>
                p.Nombre.ToLower().Contains(q) ||
                (p.Rut != null && p.Rut.Contains(q)) ||
                (p.Contacto != null && p.Contacto.ToLower().Contains(q)));
        }

        if (filtro.Activo.HasValue)
            consulta = consulta.Where(p => p.Activo == filtro.Activo.Value);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderBy(p => p.Nombre)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<ProveedorDto>.Crear(items, total, filtro);
    }

    public async Task<ProveedorDto> ObtenerProveedorAsync(int id, CancellationToken ct = default)
        => await ProyectarProveedores().FirstOrDefaultAsync(p => p.Id == id, ct)
           ?? throw new NoEncontradoException("El proveedor");

    public async Task<ProveedorDto> CrearProveedorAsync(
        GuardarProveedorRequest peticion, CancellationToken ct = default)
    {
        var proveedor = new Proveedor
        {
            Nombre = peticion.Nombre.Trim(),
            Rut = Limpiar(peticion.Rut),
            Contacto = Limpiar(peticion.Contacto),
            Telefono = Limpiar(peticion.Telefono),
            Correo = Limpiar(peticion.Correo),
            Direccion = Limpiar(peticion.Direccion),
            Notas = Limpiar(peticion.Notas),
            Activo = true
        };

        _db.Proveedores.Add(proveedor);

        // El RUT tiene índice único ignorando puntos y guion: si se repite,
        // ManejadorExcepciones traduce el 23505 a un mensaje entendible.
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Proveedor creado: {Nombre} por {Autor}",
            proveedor.Nombre, _usuarioActual.Email);

        return await ObtenerProveedorAsync(proveedor.Id, ct);
    }

    public async Task<ProveedorDto> ActualizarProveedorAsync(
        int id, GuardarProveedorRequest peticion, CancellationToken ct = default)
    {
        var proveedor = await _db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("El proveedor");

        proveedor.Nombre = peticion.Nombre.Trim();
        proveedor.Rut = Limpiar(peticion.Rut);
        proveedor.Contacto = Limpiar(peticion.Contacto);
        proveedor.Telefono = Limpiar(peticion.Telefono);
        proveedor.Correo = Limpiar(peticion.Correo);
        proveedor.Direccion = Limpiar(peticion.Direccion);
        proveedor.Notas = Limpiar(peticion.Notas);

        await _db.SaveChangesAsync(ct);
        return await ObtenerProveedorAsync(id, ct);
    }

    public async Task<ProveedorDto> CambiarEstadoProveedorAsync(
        int id, bool activo, CancellationToken ct = default)
    {
        var proveedor = await _db.Proveedores.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("El proveedor");

        // No se elimina: las compras históricas y los lotes lo referencian.
        proveedor.Activo = activo;
        await _db.SaveChangesAsync(ct);

        return await ObtenerProveedorAsync(id, ct);
    }

    /* ==================================================================
       PRESENTACIONES
       Cómo llega cada flor: paquete de 25, caja de 12 paquetes.
       ================================================================== */

    public async Task<IReadOnlyList<PresentacionDto>> ListarPresentacionesAsync(
        int productoId, CancellationToken ct = default)
        => await _db.Presentaciones.AsNoTracking()
            .Where(p => p.ProductoId == productoId)
            .OrderByDescending(p => p.Predeterminada).ThenBy(p => p.VarasTotales)
            .Select(p => new PresentacionDto
            {
                Id = p.Id,
                ProductoId = p.ProductoId,
                Producto = p.Producto.Nombre,
                Nombre = p.Nombre,
                Tipo = p.Tipo.ToString(),
                Paquetes = p.Paquetes,
                VarasPorPaquete = p.VarasPorPaquete,
                VarasTotales = p.VarasTotales,
                Predeterminada = p.Predeterminada,
                Activa = p.Activa
            })
            .ToListAsync(ct);

    public async Task<PresentacionDto> CrearPresentacionAsync(
        int productoId, GuardarPresentacionRequest peticion, CancellationToken ct = default)
    {
        var producto = await _db.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct)
            ?? throw new NoEncontradoException("El producto");

        if (producto.Tipo != TipoProducto.simple)
            throw new ExcepcionNegocio(
                "Solo los productos simples tienen presentación de compra: " +
                "un ramo se arma, no se compra.");

        var tipo = ATipoPresentacion(peticion.Tipo);

        // Una vara suelta es exactamente eso: la base lo verifica igual, pero
        // acá el mensaje explica qué se esperaba.
        if (tipo == TipoPresentacion.vara &&
            (peticion.Paquetes != 1 || peticion.VarasPorPaquete != 1))
        {
            throw new ExcepcionNegocio(
                "Una presentación de tipo vara equivale a 1 vara: usa paquete o caja.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (peticion.Predeterminada)
            await QuitarPredeterminadaAsync(productoId, ct);

        var presentacion = new Presentacion
        {
            ProductoId = productoId,
            Nombre = peticion.Nombre.Trim(),
            Tipo = tipo,
            Paquetes = peticion.Paquetes,
            VarasPorPaquete = peticion.VarasPorPaquete,
            Predeterminada = peticion.Predeterminada,
            Activa = true
        };

        _db.Presentaciones.Add(presentacion);
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ListarPresentacionesAsync(productoId, ct))
            .First(p => p.Id == presentacion.Id);
    }

    public async Task<PresentacionDto> ActualizarPresentacionAsync(
        int id, GuardarPresentacionRequest peticion, CancellationToken ct = default)
    {
        var presentacion = await _db.Presentaciones.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La presentación");

        // Cambiar la equivalencia recalcularía mal las compras ya recibidas:
        // los lotes se generaron con las varas de entonces.
        if (await _db.CompraItems.AnyAsync(ci => ci.PresentacionId == id, ct) &&
            (presentacion.Paquetes != peticion.Paquetes ||
             presentacion.VarasPorPaquete != peticion.VarasPorPaquete))
        {
            throw new ExcepcionNegocio(
                "Esta presentación ya se usó en una compra: no se puede cambiar su " +
                "equivalencia en varas. Crea una presentación nueva y desactiva esta.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (peticion.Predeterminada && !presentacion.Predeterminada)
            await QuitarPredeterminadaAsync(presentacion.ProductoId, ct);

        presentacion.Nombre = peticion.Nombre.Trim();
        presentacion.Tipo = ATipoPresentacion(peticion.Tipo);
        presentacion.Paquetes = peticion.Paquetes;
        presentacion.VarasPorPaquete = peticion.VarasPorPaquete;
        presentacion.Predeterminada = peticion.Predeterminada;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return (await ListarPresentacionesAsync(presentacion.ProductoId, ct))
            .First(p => p.Id == id);
    }

    public async Task EliminarPresentacionAsync(int id, CancellationToken ct = default)
    {
        var presentacion = await _db.Presentaciones.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La presentación");

        if (await _db.CompraItems.AnyAsync(ci => ci.PresentacionId == id, ct))
        {
            // Borrarla dejaría las compras históricas sin poder explicar de
            // dónde salieron las varas de cada lote.
            presentacion.Activa = false;
            await _db.SaveChangesAsync(ct);
            return;
        }

        _db.Presentaciones.Remove(presentacion);
        await _db.SaveChangesAsync(ct);
    }

    /* ==================================================================
       COMPRAS
       ================================================================== */

    public async Task<ResultadoPagina<CompraDto>> ListarAsync(
        CompraFiltro filtro, CancellationToken ct = default)
    {
        var consulta = ProyectarCompras();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(c =>
                c.Folio.ToLower().Contains(q) ||
                c.Proveedor.ToLower().Contains(q) ||
                (c.Documento != null && c.Documento.ToLower().Contains(q)));
        }

        if (filtro.ProveedorId.HasValue)
            consulta = consulta.Where(c => c.ProveedorId == filtro.ProveedorId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
            consulta = consulta.Where(c => c.Estado == AEstado(filtro.Estado).ToString());

        if (filtro.Desde.HasValue)
            consulta = consulta.Where(c => c.Fecha >= filtro.Desde.Value);

        if (filtro.Hasta.HasValue)
            consulta = consulta.Where(c => c.Fecha <= filtro.Hasta.Value);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(c => c.Fecha).ThenByDescending(c => c.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<CompraDto>.Crear(items, total, filtro);
    }

    public async Task<CompraDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var b = await ProyectarCompras().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("La compra");

        var items = await (from ci in _db.CompraItems.AsNoTracking()
                           where ci.CompraId == id
                           orderby ci.Producto.Nombre
                           select new CompraItemDto
                           {
                               Id = ci.Id,
                               ProductoId = ci.ProductoId,
                               Producto = ci.Producto.Nombre,
                               Emoji = ci.Producto.Emoji,
                               PresentacionId = ci.PresentacionId,
                               Presentacion = ci.Presentacion.Nombre,
                               Cantidad = ci.Cantidad,
                               CostoUnitario = ci.CostoUnitario,
                               VarasTotales = ci.VarasTotales,
                               CostoPorVara = ci.CostoPorVara,
                               Subtotal = ci.Cantidad * ci.CostoUnitario
                           }).ToListAsync(ct);

        // Costo por vara de la compra anterior de cada producto: es lo que
        // permite ver de un vistazo si el proveedor subió el precio.
        foreach (var item in items)
        {
            item.CostoAnterior = await _db.CompraItems.AsNoTracking()
                .Where(ci => ci.ProductoId == item.ProductoId
                          && ci.CompraId != id
                          && ci.Compra.Estado == EstadoCompra.recibida)
                .OrderByDescending(ci => ci.Compra.Fecha).ThenByDescending(ci => ci.Id)
                .Select(ci => (decimal?)ci.CostoPorVara)
                .FirstOrDefaultAsync(ct);
        }

        var lotes = await _db.Lotes.AsNoTracking()
            .Where(l => l.CompraId == id)
            .OrderBy(l => l.Codigo)
            .Select(l => new LoteGeneradoDto
            {
                Id = l.Id,
                Codigo = l.Codigo,
                Producto = l.Producto.Nombre,
                Varas = l.VarasIniciales,
                FechaVencimiento = l.FechaVencimiento
            })
            .ToListAsync(ct);

        return new CompraDetalleDto
        {
            Id = b.Id,
            Folio = b.Folio,
            ProveedorId = b.ProveedorId,
            Proveedor = b.Proveedor,
            Fecha = b.Fecha,
            Documento = b.Documento,
            Estado = b.Estado,
            Neto = b.Neto,
            Iva = b.Iva,
            Total = b.Total,
            Notas = b.Notas,
            Usuario = b.Usuario,
            RecibidaEn = b.RecibidaEn,
            Lineas = b.Lineas,
            VarasTotales = b.VarasTotales,
            Items = items,
            Lotes = lotes
        };
    }

    public async Task<CompraDetalleDto> CrearAsync(
        GuardarCompraRequest peticion, CancellationToken ct = default)
    {
        if (!await _db.Proveedores.AnyAsync(p => p.Id == peticion.ProveedorId, ct))
            throw new ExcepcionNegocio("El proveedor indicado no existe.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // El folio lo entrega la secuencia de la base: dos usuarios creando a
        // la vez no pueden obtener el mismo número.
        var numero = await SiguienteFolioAsync(ct);

        var compra = new Compra
        {
            Folio = $"OC-{numero}",
            ProveedorId = peticion.ProveedorId,
            Fecha = peticion.Fecha ?? DateOnly.FromDateTime(DateTime.Today),
            Documento = Limpiar(peticion.Documento),
            Notas = Limpiar(peticion.Notas),
            Estado = EstadoCompra.borrador,
            UsuarioId = _usuarioActual.Id
        };

        _db.Compras.Add(compra);
        await _db.SaveChangesAsync(ct);

        await ReemplazarItemsAsync(compra, peticion, ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Compra {Folio} creada por {Autor}", compra.Folio, _usuarioActual.Email);

        return await ObtenerAsync(compra.Id, ct);
    }

    public async Task<CompraDetalleDto> ActualizarAsync(
        int id, GuardarCompraRequest peticion, CancellationToken ct = default)
    {
        var compra = await _db.Compras.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("La compra");

        // Una compra recibida ya generó lotes con su costo por vara: cambiar
        // las líneas dejaría el inventario contando varas que no existen.
        if (compra.Estado != EstadoCompra.borrador)
            throw new ExcepcionNegocio(
                $"La compra {compra.Folio} está {compra.Estado}: solo se editan los borradores.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        compra.ProveedorId = peticion.ProveedorId;
        compra.Fecha = peticion.Fecha ?? compra.Fecha;
        compra.Documento = Limpiar(peticion.Documento);
        compra.Notas = Limpiar(peticion.Notas);

        var actuales = await _db.CompraItems.Where(ci => ci.CompraId == id).ToListAsync(ct);
        _db.CompraItems.RemoveRange(actuales);
        await _db.SaveChangesAsync(ct);

        await ReemplazarItemsAsync(compra, peticion, ct);
        await tx.CommitAsync(ct);

        return await ObtenerAsync(id, ct);
    }

    /// <summary>
    /// Recibe la compra: genera un lote por línea, con su código QR, su
    /// vencimiento y su costo por vara.
    ///
    /// Todo el trabajo lo hace fn_recibir_compra en la base. Replicarlo en C#
    /// obligaría a insertar lotes, actualizar stock y escribir movimientos en
    /// varios viajes; en la función es una sola transacción, y el trigger de
    /// sincronización deja el stock cuadrado sin que nadie lo escriba.
    /// </summary>
    public async Task<ResultadoRecepcionDto> RecibirAsync(int id, CancellationToken ct = default)
    {
        var compra = await _db.Compras.AsNoTracking()
            .Include(c => c.Proveedor)
            .FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("La compra");

        if (compra.Estado == EstadoCompra.recibida)
            throw new ExcepcionNegocio($"La compra {compra.Folio} ya fue recibida.");
        if (compra.Estado == EstadoCompra.anulada)
            throw new ExcepcionNegocio($"La compra {compra.Folio} está anulada.");

        if (!await _db.CompraItems.AnyAsync(ci => ci.CompraId == id, ct))
            throw new ExcepcionNegocio("La compra no tiene líneas: no hay nada que recibir.");

        var usuarioId = _usuarioActual.Id;

        var generados = await _db.RecepcionesLote
            .FromSqlInterpolated($"SELECT * FROM fn_recibir_compra({id}, {usuarioId})")
            .ToListAsync(ct);

        // Se relee para tomar el vencimiento que calculó la función
        var lotes = await _db.Lotes.AsNoTracking()
            .Where(l => l.CompraId == id)
            .OrderBy(l => l.Codigo)
            .Select(l => new LoteGeneradoDto
            {
                Id = l.Id,
                Codigo = l.Codigo,
                Producto = l.Producto.Nombre,
                Varas = l.VarasIniciales,
                FechaVencimiento = l.FechaVencimiento
            })
            .ToListAsync(ct);

        _log.LogInformation("Compra {Folio} recibida: {Lotes} lotes por {Autor}",
            compra.Folio, lotes.Count, _usuarioActual.Email);

        return new ResultadoRecepcionDto
        {
            CompraId = id,
            Folio = compra.Folio,
            Proveedor = compra.Proveedor.Nombre,
            LotesGenerados = lotes.Count,
            VarasIngresadas = generados.Sum(g => g.Varas),
            Lotes = lotes
        };
    }

    public async Task<CompraDto> AnularAsync(int id, CancellationToken ct = default)
    {
        var compra = await _db.Compras.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("La compra");

        // Anular una recibida obligaría a devolver varas que quizá ya se
        // vendieron o se armaron. Si la mercadería se devuelve al proveedor,
        // se registra como merma con su motivo: así queda el rastro.
        if (compra.Estado == EstadoCompra.recibida)
            throw new ExcepcionNegocio(
                $"La compra {compra.Folio} ya generó lotes. Para revertirla, registra " +
                "la merma de esos lotes indicando la devolución al proveedor.");

        compra.Estado = EstadoCompra.anulada;
        await _db.SaveChangesAsync(ct);

        return await ProyectarCompras().FirstAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyList<EvolucionCostoDto>> EvolucionCostoAsync(
        int productoId, CancellationToken ct = default)
        => await _db.EvolucionCostos.AsNoTracking()
            .Where(e => e.ProductoId == productoId)
            .OrderByDescending(e => e.Fecha)
            .Select(e => new EvolucionCostoDto
            {
                Fecha = e.Fecha,
                Proveedor = e.Proveedor,
                Presentacion = e.Presentacion,
                Cantidad = e.Cantidad,
                CostoUnitario = e.CostoUnitario,
                CostoPorVara = e.CostoPorVara,
                CostoAnterior = e.CostoAnterior,
                Variacion = e.Variacion
            })
            .ToListAsync(ct);

    /* ==================================================================
       INTERNO
       ================================================================== */

    /// <summary>
    /// Convierte las líneas en items, calculando las varas y el costo por
    /// vara desde la presentación. Ese reparto es lo que después permite
    /// valorizar cada vara consumida al armar o al vender.
    /// </summary>
    private async Task ReemplazarItemsAsync(
        Compra compra, GuardarCompraRequest peticion, CancellationToken ct)
    {
        if (peticion.Items.Count == 0)
            throw new ExcepcionNegocio("La compra necesita al menos una línea.");

        var presentacionIds = peticion.Items.Select(i => i.PresentacionId).Distinct().ToList();
        var presentaciones = await _db.Presentaciones
            .Where(p => presentacionIds.Contains(p.Id))
            .Select(p => new { p.Id, p.ProductoId, p.Nombre, p.VarasTotales, p.Activa })
            .ToListAsync(ct);

        var total = 0;

        foreach (var linea in peticion.Items)
        {
            var pres = presentaciones.FirstOrDefault(p => p.Id == linea.PresentacionId)
                ?? throw new ExcepcionNegocio(
                    $"La presentación {linea.PresentacionId} no existe.");

            if (pres.ProductoId != linea.ProductoId)
                throw new ExcepcionNegocio(
                    $"La presentación «{pres.Nombre}» no pertenece al producto indicado.");

            if (!pres.Activa)
                throw new ExcepcionNegocio($"La presentación «{pres.Nombre}» está desactivada.");

            var varas = linea.Cantidad * pres.VarasTotales;
            var subtotal = linea.Cantidad * linea.CostoUnitario;
            total += subtotal;

            _db.CompraItems.Add(new CompraItem
            {
                CompraId = compra.Id,
                ProductoId = linea.ProductoId,
                PresentacionId = linea.PresentacionId,
                Cantidad = linea.Cantidad,
                CostoUnitario = linea.CostoUnitario,
                VarasTotales = varas,
                // 4 decimales: repartir el costo de una caja entre 300 varas
                // rara vez da un entero, y ese redondeo se acumula.
                CostoPorVara = Math.Round((decimal)subtotal / varas, 4)
            });
        }

        var tasa = peticion.IvaTasa / 100m;
        compra.Total = total;
        compra.Neto = (int)Math.Round(total / (1 + tasa), MidpointRounding.AwayFromZero);
        compra.Iva = total - compra.Neto;

        await _db.SaveChangesAsync(ct);
    }

    private async Task<long> SiguienteFolioAsync(CancellationToken ct)
    {
        var conexion = _db.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
            await conexion.OpenAsync(ct);

        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT nextval('seq_folio_compra')";
        if (_db.Database.CurrentTransaction is not null)
            comando.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();

        var resultado = await comando.ExecuteScalarAsync(ct);
        return Convert.ToInt64(resultado);
    }

    private async Task QuitarPredeterminadaAsync(int productoId, CancellationToken ct)
    {
        var previas = await _db.Presentaciones
            .Where(p => p.ProductoId == productoId && p.Predeterminada)
            .ToListAsync(ct);

        foreach (var p in previas) p.Predeterminada = false;
        await _db.SaveChangesAsync(ct);
    }

    private IQueryable<ProveedorDto> ProyectarProveedores()
        => _db.Proveedores.AsNoTracking().Select(p => new ProveedorDto
        {
            Id = p.Id,
            Nombre = p.Nombre,
            Rut = p.Rut,
            Contacto = p.Contacto,
            Telefono = p.Telefono,
            Correo = p.Correo,
            Direccion = p.Direccion,
            Notas = p.Notas,
            Activo = p.Activo,
            Compras = p.Compras.Count(c => c.Estado == EstadoCompra.recibida),
            TotalComprado = p.Compras.Where(c => c.Estado == EstadoCompra.recibida)
                                     .Sum(c => (long)c.Total),
            UltimaCompra = p.Compras.Where(c => c.Estado == EstadoCompra.recibida)
                                    .Max(c => (DateOnly?)c.Fecha)
        });

    private IQueryable<CompraDto> ProyectarCompras()
        => _db.Compras.AsNoTracking().Select(c => new CompraDto
        {
            Id = c.Id,
            Folio = c.Folio,
            ProveedorId = c.ProveedorId,
            Proveedor = c.Proveedor.Nombre,
            Fecha = c.Fecha,
            Documento = c.Documento,
            Estado = c.Estado.ToString(),
            Neto = c.Neto,
            Iva = c.Iva,
            Total = c.Total,
            Notas = c.Notas,
            Usuario = c.Usuario != null ? c.Usuario.Nombre : null,
            RecibidaEn = c.RecibidaEn,
            Lineas = c.Items.Count,
            VarasTotales = c.Items.Sum(i => i.VarasTotales)
        });

    private static string? Limpiar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static TipoPresentacion ATipoPresentacion(string valor)
        => Enum.TryParse<TipoPresentacion>(valor?.Trim().ToLowerInvariant(), out var t)
            ? t
            : throw new ExcepcionNegocio("Tipo no válido. Debe ser vara, paquete o caja.");

    private static EstadoCompra AEstado(string valor)
        => Enum.TryParse<EstadoCompra>(valor?.Trim().ToLowerInvariant(), out var e)
            ? e
            : throw new ExcepcionNegocio("Estado no válido. Debe ser borrador, recibida o anulada.");
}