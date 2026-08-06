using Colibri.Api.Common;
using Colibri.Api.Common.Inventario;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Inventario.Dtos;
using Colibri.Api.Context;
using Microsoft.EntityFrameworkCore;

namespace Colibri.Api.Features.Inventario;

public class InventarioService : IInventarioService
{
    private readonly ColibriDbContext _db;
    private readonly IConsumidorLotes _consumidor;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<InventarioService> _log;

    public InventarioService(
        ColibriDbContext db,
        IConsumidorLotes consumidor,
        IUsuarioActual usuarioActual,
        ILogger<InventarioService> log)
    {
        _db = db;
        _consumidor = consumidor;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       PRODUCTOS
       ================================================================== */

    /// <summary>
    /// La disponibilidad, el costo y el margen salen de vw_productos_disponibles:
    /// calcular en C# cuántos ramos alcanzan a armarse obligaría a traerse toda
    /// la tabla de recetas y el stock de cada ingrediente en cada consulta.
    /// La vista lo resuelve en la base, donde están los datos.
    /// </summary>
    public async Task<ResultadoPagina<ProductoDto>> ListarAsync(
        ProductoFiltro filtro, CancellationToken ct = default)
    {
        var consulta = Proyectar();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(p =>
                p.Nombre.ToLower().Contains(q) || p.Codigo.ToLower().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Tipo))
            consulta = consulta.Where(p => p.Tipo == ATipo(filtro.Tipo).ToString());

        if (filtro.CategoriaId.HasValue)
            consulta = consulta.Where(p => p.CategoriaId == filtro.CategoriaId.Value);

        if (filtro.Activo.HasValue)
            consulta = consulta.Where(p => p.Activo == filtro.Activo.Value);

        if (filtro.ControlaLotes.HasValue)
            consulta = consulta.Where(p => p.ControlaLotes == filtro.ControlaLotes.Value);

        if (filtro.BajoMinimo)
            consulta = consulta.Where(p => p.Disponible <= p.Minimo);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderBy(p => p.Categoria).ThenBy(p => p.Nombre)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<ProductoDto>.Crear(items, total, filtro);
    }

    public async Task<ProductoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var producto = await Proyectar().FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("El producto");

        return await ConDetalleAsync(producto, ct);
    }

    /// <summary>Búsqueda por código de barras, para el punto de venta.</summary>
    public async Task<ProductoDetalleDto> ObtenerPorCodigoAsync(
        string codigo, CancellationToken ct = default)
    {
        var limpio = (codigo ?? string.Empty).Trim();

        var producto = await Proyectar().FirstOrDefaultAsync(p => p.Codigo == limpio, ct)
            ?? throw new NoEncontradoException($"El producto con código {limpio}");

        return await ConDetalleAsync(producto, ct);
    }

    public async Task<ProductoDetalleDto> CrearAsync(
        CrearProductoRequest peticion, CancellationToken ct = default)
    {
        var tipo = ATipo(peticion.Tipo);
        var codigo = peticion.Codigo.Trim();

        if (await _db.Productos.AnyAsync(p => p.Codigo == codigo, ct))
            throw new ExcepcionNegocio("Ya existe un producto con ese código.",
                StatusCodes.Status409Conflict);

        if (!await _db.Categorias.AnyAsync(c => c.Id == peticion.CategoriaId, ct))
            throw new ExcepcionNegocio("La categoría indicada no existe.");

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = peticion.Nombre.Trim(),
            CategoriaId = peticion.CategoriaId,
            Tipo = tipo,
            Emoji = string.IsNullOrWhiteSpace(peticion.Emoji) ? "🌿" : peticion.Emoji,
            Precio = peticion.Precio,
            Minimo = peticion.Minimo,
            Activo = true
        };

        if (tipo == TipoProducto.simple)
        {
            if (peticion.Costo is null)
                throw new ExcepcionNegocio("Un producto simple necesita costo unitario.");

            producto.Costo = peticion.Costo;
            producto.ControlaLotes = peticion.ControlaLotes;
            producto.DiasVida = peticion.DiasVida;

            // Un producto con lotes NACE en cero: sus existencias entran
            // recibiendo una compra, que es lo que genera el lote con su
            // procedencia, su costo y su vencimiento.
            producto.Stock = peticion.ControlaLotes ? 0 : (peticion.StockInicial ?? 0);

            if (peticion.ControlaLotes && peticion.StockInicial is > 0)
            {
                _log.LogWarning(
                    "Se ignoró el stock inicial de {Codigo}: controla lotes y las " +
                    "existencias entran por recepción de compra.", codigo);
            }
        }
        else
        {
            // Un ramo sin receta no se puede armar ni costear: sería un
            // producto inutilizable en el catálogo.
            if (peticion.Receta.Count == 0)
                throw new ExcepcionNegocio("Un producto armado necesita al menos un ingrediente.");

            producto.StockListo = 0;
            producto.CostoArmado = peticion.CostoArmado ?? 0;
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        _db.Productos.Add(producto);
        await _db.SaveChangesAsync(ct);

        if (tipo == TipoProducto.armado)
            await ReemplazarRecetaAsync(producto.Id, peticion.Receta, ct);

        // Movimiento de alta, para que el libro del producto parta cuadrado
        if (tipo == TipoProducto.simple && producto.Stock > 0)
        {
            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.alta,
                Cantidad = producto.Stock.Value,
                StockResultante = producto.Stock,
                Motivo = "Alta de producto con stock inicial",
                UsuarioId = _usuarioActual.Id
            });
            await _db.SaveChangesAsync(ct);
        }

        await tx.CommitAsync(ct);

        _log.LogInformation("Producto creado: {Codigo} · {Nombre} por {Autor}",
            producto.Codigo, producto.Nombre, _usuarioActual.Email);

        return await ObtenerAsync(producto.Id, ct);
    }

    public async Task<ProductoDetalleDto> ActualizarAsync(
        int id, ActualizarProductoRequest peticion, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);
        var codigo = peticion.Codigo.Trim();

        if (await _db.Productos.AnyAsync(p => p.Codigo == codigo && p.Id != id, ct))
            throw new ExcepcionNegocio("Ya existe otro producto con ese código.",
                StatusCodes.Status409Conflict);

        producto.Codigo = codigo;
        producto.Nombre = peticion.Nombre.Trim();
        producto.CategoriaId = peticion.CategoriaId;
        producto.Emoji = string.IsNullOrWhiteSpace(peticion.Emoji) ? producto.Emoji : peticion.Emoji;
        producto.Precio = peticion.Precio;
        producto.Minimo = peticion.Minimo;

        // El tipo no se toca: hay un trigger que lo impide, y con razón.
        // El stock tampoco: se mueve, no se escribe.
        if (producto.Tipo == TipoProducto.simple)
        {
            if (peticion.Costo.HasValue) producto.Costo = peticion.Costo;
            if (peticion.DiasVida.HasValue) producto.DiasVida = peticion.DiasVida;
        }
        else if (peticion.CostoArmado.HasValue)
        {
            producto.CostoArmado = peticion.CostoArmado;
        }

        await _db.SaveChangesAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<ProductoDto> CambiarEstadoAsync(
        int id, bool activo, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (!activo)
        {
            // Desactivar un tallo que está en la receta de un ramo dejaría a
            // ese ramo sin poder armarse, sin ninguna señal visible.
            var usadoEn = await _db.Recetas
                .Where(r => r.ComponenteId == id)
                .Select(r => r.Producto.Nombre)
                .ToListAsync(ct);

            if (usadoEn.Count > 0)
            {
                throw new ExcepcionNegocio(
                    $"No se puede desactivar: es ingrediente de {string.Join(", ", usadoEn)}.");
            }
        }

        producto.Activo = activo;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Producto {Codigo} {Estado} por {Autor}",
            producto.Codigo, activo ? "activado" : "desactivado", _usuarioActual.Email);

        return await Proyectar().FirstAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// Borrado definitivo. Solo para productos creados por error: si tiene
    /// movimientos, ventas o lotes, se desactiva en vez de borrarse, porque
    /// eliminarlo dejaría boletas históricas apuntando a la nada.
    /// </summary>
    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (await _db.MovimientosInventario.AnyAsync(m => m.ProductoId == id, ct))
            throw new ExcepcionNegocio(
                "Tiene movimientos registrados. Desactívalo en vez de eliminarlo, " +
                "para no perder el historial.");

        if (await _db.VentaItems.AnyAsync(v => v.ProductoId == id, ct))
            throw new ExcepcionNegocio("Se vendió alguna vez. Desactívalo en vez de eliminarlo.");

        if (await _db.Lotes.AnyAsync(l => l.ProductoId == id, ct))
            throw new ExcepcionNegocio("Tiene lotes asociados. Desactívalo en vez de eliminarlo.");

        var usadoEn = await _db.Recetas.Where(r => r.ComponenteId == id)
            .Select(r => r.Producto.Nombre).ToListAsync(ct);

        if (usadoEn.Count > 0)
            throw new ExcepcionNegocio(
                $"Es ingrediente de {string.Join(", ", usadoEn)}. Quítalo de esas recetas primero.");

        _db.Productos.Remove(producto);
        await _db.SaveChangesAsync(ct);

        _log.LogWarning("Producto {Codigo} eliminado definitivamente por {Autor}",
            producto.Codigo, _usuarioActual.Email);
    }

    /* ==================================================================
       RECETAS
       ================================================================== */

    public async Task<IReadOnlyList<IngredienteDto>> ObtenerRecetaAsync(
        int id, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo los productos armados tienen receta.");

        return await LeerRecetaAsync(id, ct);
    }

    public async Task<ProductoDetalleDto> GuardarRecetaAsync(
        int id, GuardarRecetaRequest peticion, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo los productos armados tienen receta.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        await ReemplazarRecetaAsync(id, peticion.Ingredientes, ct);
        await tx.CommitAsync(ct);

        return await ObtenerAsync(id, ct);
    }

    /* ==================================================================
       MOVIMIENTOS DE STOCK
       ================================================================== */

    /// <summary>
    /// Ajuste manual: compra sin orden, conteo físico, corrección.
    /// Solo para productos SIN control por lote. En una flor, las existencias
    /// pertenecen a un lote concreto —con su proveedor, su costo y su fecha—
    /// y sumar unidades sueltas dejaría stock que no pertenece a ninguno.
    /// La base lo rechaza igual; acá se explica el motivo.
    /// </summary>
    public async Task<ProductoDto> AjustarStockAsync(
        int id, AjustarStockRequest peticion, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (producto.Tipo != TipoProducto.simple)
            throw new ExcepcionNegocio(
                "Los productos armados no se ajustan: se arman o se venden.");

        if (producto.ControlaLotes)
            throw new ExcepcionNegocio(
                $"{producto.Nombre} se controla por lote. Sus existencias entran " +
                "recibiendo una compra y salen vendiendo o registrando merma.");

        if (peticion.Cantidad == 0)
            throw new ExcepcionNegocio("La cantidad debe ser distinta de cero.");

        var resultante = (producto.Stock ?? 0) + peticion.Cantidad;
        if (resultante < 0)
            throw new ExcepcionNegocio(
                $"No puedes descontar {Math.Abs(peticion.Cantidad)}: solo hay {producto.Stock}.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        producto.Stock = resultante;

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = id,
            Tipo = peticion.Cantidad > 0 ? TipoMovimiento.entrada : TipoMovimiento.salida,
            Cantidad = peticion.Cantidad,
            StockResultante = resultante,
            Motivo = peticion.Motivo.Trim(),
            Detalle = peticion.Detalle?.Trim(),
            UsuarioId = _usuarioActual.Id
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Ajuste de {Codigo}: {Cantidad} · {Motivo} por {Autor}",
            producto.Codigo, peticion.Cantidad, peticion.Motivo, _usuarioActual.Email);

        return await Proyectar().FirstAsync(p => p.Id == id, ct);
    }

    /// <summary>
    /// Arma N unidades: descuenta los ingredientes y suma unidades listas.
    /// Es el momento en que la flor suelta se convierte en producto terminado.
    ///
    /// Los ingredientes que llevan lote se descuentan con fn_consumir_lotes,
    /// que aplica FIFO y bloquea las filas: dos personas armando a la vez no
    /// pueden llevarse la misma vara.
    /// </summary>
    public async Task<ResultadoArmadoDto> ArmarAsync(
        int id, ArmarRequest peticion, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo los productos armados se pueden armar.");
        if (peticion.Cantidad < 1)
            throw new ExcepcionNegocio("Indica cuántas unidades vas a armar.");

        var receta = await _db.Recetas
            .Where(r => r.ProductoId == id)
            .Select(r => new {
                r.ComponenteId,
                r.Cantidad,
                r.Componente.Nombre,
                r.Componente.ControlaLotes,
                r.Componente.Stock
            })
            .ToListAsync(ct);

        if (receta.Count == 0)
            throw new ExcepcionNegocio("Este producto no tiene receta: no se puede armar.");

        // Comprobación previa, para dar un mensaje entendible antes de tocar
        // nada. La garantía real es el bloqueo de filas dentro de la transacción.
        // Comprobación previa, para dar un mensaje entendible antes de tocar
        // nada. La garantía real es el bloqueo de filas dentro de la transacción.
        //
        // El stock del producto incluye la flor recuperada, pero esa no entra
        // en el reparto automático: solo cuenta si se autorizó su lote.
        var disponibilidad = await DisponibilidadArmadoAsync(id, peticion.Cantidad, ct);
        var autorizados = peticion.LotesAutorizados ?? new List<int>();

        var posibles = autorizados.Count > 0
            ? disponibilidad.PosiblesConRecuperada
            : disponibilidad.PosiblesConPrimera;

        if (peticion.Cantidad > posibles)
        {
            var faltante = disponibilidad.Faltantes.FirstOrDefault();
            var mensaje = $"Con el stock disponible solo alcanza para {posibles} unidad(es).";

            if (faltante is not null)
                mensaje += $" El ingrediente más escaso es {faltante.Producto}.";

            // Si hay flor recuperada sin autorizar, se ofrece en vez de
            // dejar a la persona adivinando por qué no alcanza.
            if (autorizados.Count == 0 && disponibilidad.PosiblesConRecuperada > posibles)
            {
                mensaje += $" Hay flor recuperada que permitiría armar " +
                           $"{disponibilidad.PosiblesConRecuperada}: consulta " +
                           $"/api/productos/{id}/disponibilidad-armado e indícala " +
                           "en lotesAutorizados si quieres usarla.";
            }

            throw new ExcepcionNegocio(mensaje);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var consumos = new List<ConsumoDto>();
        decimal costoInsumos = 0;

        foreach (var linea in receta)
        {
            var necesita = linea.Cantidad * peticion.Cantidad;

            if (linea.ControlaLotes)
            {
                // Los lotes autorizados se toman primero: son flor recuperada,
                // que está fuera del reparto automático porque usarla es una
                // decisión y no algo que el sistema deba hacer solo.
                var filas = await _consumidor.ConsumirAsync(
                    linea.ComponenteId, necesita,
                    $"Armado de {producto.Nombre}", _usuarioActual.Id,
                    "armado", id, TipoMovimiento.consumo,
                    lotePreferido: null,
                    lotesAutorizados: peticion.LotesAutorizados,
                    ct: ct);

                foreach (var fila in filas)
                {
                    costoInsumos += fila.CostoUnitario * fila.Cantidad;
                    consumos.Add(new ConsumoDto
                    {
                        ProductoId = linea.ComponenteId,
                        Producto = linea.Nombre,
                        LoteCodigo = fila.Codigo,
                        Cantidad = fila.Cantidad,
                        CostoUnitario = fila.CostoUnitario,
                        EsRecuperado = fila.Codigo.StartsWith("REC-")
                    });
                }
            }
            else
            {
                var componente = await _db.Productos
                    .FirstAsync(p => p.Id == linea.ComponenteId, ct);

                if ((componente.Stock ?? 0) < necesita)
                    throw new ExcepcionNegocio(
                        $"No alcanza el stock de {componente.Nombre}: " +
                        $"se necesitan {necesita} y hay {componente.Stock}.");

                componente.Stock -= necesita;
                costoInsumos += (componente.Costo ?? 0) * necesita;

                _db.MovimientosInventario.Add(new MovimientoInventario
                {
                    ProductoId = componente.Id,
                    Tipo = TipoMovimiento.consumo,
                    Cantidad = -necesita,
                    StockResultante = componente.Stock,
                    Motivo = $"Armado de {producto.Nombre}",
                    UsuarioId = _usuarioActual.Id,
                    ReferenciaTipo = "armado",
                    ReferenciaId = id
                });

                consumos.Add(new ConsumoDto
                {
                    ProductoId = componente.Id,
                    Producto = componente.Nombre,
                    Cantidad = necesita,
                    CostoUnitario = componente.Costo ?? 0
                });
            }
        }

        producto.StockListo = (producto.StockListo ?? 0) + peticion.Cantidad;

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = id,
            Tipo = TipoMovimiento.armado,
            Cantidad = peticion.Cantidad,
            StockResultante = producto.StockListo,
            Motivo = "Producción",
            UsuarioId = _usuarioActual.Id
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        var manoObra = (producto.CostoArmado ?? 0) * peticion.Cantidad;

        _log.LogInformation("Armadas {Cantidad} de {Producto} por {Autor}",
            peticion.Cantidad, producto.Nombre, _usuarioActual.Email);

        return new ResultadoArmadoDto
        {
            ProductoId = id,
            Producto = producto.Nombre,
            Armadas = peticion.Cantidad,
            StockListo = producto.StockListo ?? 0,
            CostoProduccion = (int)Math.Round(costoInsumos) + manoObra,
            Consumos = consumos
        };
    }

    /// <summary>
    /// Qué se puede armar hoy y con qué.
    ///
    /// Separa la flor de primera de la recuperada porque no son
    /// intercambiables sin criterio: la recuperada está fuera del reparto
    /// automático y usarla es una decisión de quien arma. Un ramo para un
    /// matrimonio probablemente no debería llevarla.
    /// </summary>
    public async Task<DisponibilidadArmadoDto> DisponibilidadArmadoAsync(
        int id, int cantidad, CancellationToken ct = default)
    {
        var producto = await BuscarAsync(id, ct);

        if (producto.Tipo != TipoProducto.armado)
            throw new ExcepcionNegocio("Solo los productos armados se pueden armar.");

        var solicitado = Math.Max(1, cantidad);

        var receta = await _db.Recetas.AsNoTracking()
            .Where(r => r.ProductoId == id)
            .Select(r => new
            {
                r.ComponenteId,
                r.Cantidad,
                r.Componente.Nombre,
                r.Componente.Emoji,
                r.Componente.ControlaLotes,
                r.Componente.Stock
            })
            .ToListAsync(ct);

        if (receta.Count == 0)
            throw new ExcepcionNegocio("Este producto no tiene receta: no se puede armar.");

        var ids = receta.Select(r => r.ComponenteId).ToList();

        // Un lote con precio propio es flor recuperada y no participa del
        // reparto automático. El resto sí.
        var porLote = await _db.Lotes.AsNoTracking()
            .Where(l => ids.Contains(l.ProductoId) && l.Estado == EstadoLote.activo)
            .GroupBy(l => new { l.ProductoId, EsRecuperado = l.PrecioUnitario != null })
            .Select(g => new
            {
                g.Key.ProductoId,
                g.Key.EsRecuperado,
                Varas = g.Sum(x => x.VarasDisponibles)
            })
            .ToListAsync(ct);

        var faltantes = new List<FaltanteDto>();
        var posiblesPrimera = int.MaxValue;
        var posiblesTotal = int.MaxValue;

        foreach (var ingrediente in receta)
        {
            int primera, recuperada;

            if (ingrediente.ControlaLotes)
            {
                primera = porLote
                    .Where(l => l.ProductoId == ingrediente.ComponenteId && !l.EsRecuperado)
                    .Sum(l => l.Varas);
                recuperada = porLote
                    .Where(l => l.ProductoId == ingrediente.ComponenteId && l.EsRecuperado)
                    .Sum(l => l.Varas);
            }
            else
            {
                primera = ingrediente.Stock ?? 0;
                recuperada = 0;
            }

            posiblesPrimera = Math.Min(posiblesPrimera, primera / ingrediente.Cantidad);
            posiblesTotal = Math.Min(posiblesTotal, (primera + recuperada) / ingrediente.Cantidad);

            var necesita = ingrediente.Cantidad * solicitado;
            if (primera + recuperada < necesita || primera < necesita)
            {
                faltantes.Add(new FaltanteDto
                {
                    ProductoId = ingrediente.ComponenteId,
                    Producto = ingrediente.Nombre,
                    Emoji = ingrediente.Emoji,
                    Necesita = necesita,
                    HayDePrimera = primera,
                    HayRecuperada = recuperada,
                    Faltan = Math.Max(0, necesita - primera - recuperada)
                });
            }
        }

        if (posiblesPrimera == int.MaxValue) posiblesPrimera = 0;
        if (posiblesTotal == int.MaxValue) posiblesTotal = 0;

        // Solo se sugieren lotes de los ingredientes que efectivamente faltan:
        // ofrecer flor recuperada de algo que sobra sería ruido.
        var conFaltante = faltantes.Select(f => f.ProductoId).ToList();

        var sugeridos = conFaltante.Count == 0
            ? new List<LoteSugeridoDto>()
            : await _db.LotesActivos.AsNoTracking()
                .Where(l => conFaltante.Contains(l.ProductoId) && l.RequiereEscaneo)
                .OrderBy(l => l.Producto).ThenBy(l => l.FechaIngreso)
                .Select(l => new LoteSugeridoDto
                {
                    LoteId = l.Id,
                    Codigo = l.Codigo,
                    ProductoId = l.ProductoId,
                    Producto = l.Producto,
                    Calidad = l.Calidad != null ? l.Calidad.ToString() : null,
                    VarasDisponibles = l.VarasDisponibles,
                    FechaIngreso = l.FechaIngreso,
                    FechaVencimiento = l.FechaVencimiento,
                    DiasEnCamara = l.DiasEnCamara,
                    Alerta = l.Alerta,
                    CostoPorVara = l.CostoPorVara,
                    PrecioUnitario = l.PrecioUnitario
                })
                .ToListAsync(ct);

        return new DisponibilidadArmadoDto
        {
            ProductoId = producto.Id,
            Producto = producto.Nombre,
            Solicitado = solicitado,
            PosiblesConPrimera = posiblesPrimera,
            PosiblesConRecuperada = posiblesTotal,
            AlcanzaConPrimera = posiblesPrimera >= solicitado,
            AlcanzaConRecuperada = posiblesTotal >= solicitado,
            Faltantes = faltantes,
            LotesSugeridos = sugeridos
        };
    }

    public async Task<ResultadoPagina<MovimientoDto>> ListarMovimientosAsync(
        MovimientoFiltro filtro, CancellationToken ct = default)
    {
        var consulta = _db.MovimientosInventario.AsNoTracking()
            .Select(m => new MovimientoDto
            {
                Id = m.Id,
                Fecha = m.CreadoEn,
                ProductoId = m.ProductoId,
                Producto = m.Producto.Nombre,
                LoteCodigo = m.Lote != null ? m.Lote.Codigo : null,
                Tipo = m.Tipo.ToString(),
                Cantidad = m.Cantidad,
                StockResultante = m.StockResultante,
                Motivo = m.Motivo,
                Detalle = m.Detalle,
                Usuario = m.Usuario != null ? m.Usuario.Nombre : null,
                ReferenciaTipo = m.ReferenciaTipo,
                ReferenciaId = m.ReferenciaId
            });

        if (filtro.ProductoId.HasValue)
            consulta = consulta.Where(m => m.ProductoId == filtro.ProductoId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Tipo))
            consulta = consulta.Where(m => m.Tipo == filtro.Tipo.Trim().ToLower());

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(m =>
                m.Producto.ToLower().Contains(q) || m.Motivo.ToLower().Contains(q));
        }

        if (filtro.Desde.HasValue)
        {
            var desde = filtro.Desde.Value.ToDateTime(TimeOnly.MinValue);
            consulta = consulta.Where(m => m.Fecha >= new DateTimeOffset(desde, TimeSpan.Zero));
        }

        if (filtro.Hasta.HasValue)
        {
            var hasta = filtro.Hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue);
            consulta = consulta.Where(m => m.Fecha < new DateTimeOffset(hasta, TimeSpan.Zero));
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<MovimientoDto>.Crear(items, total, filtro);
    }

    /* ==================================================================
       APOYO
       ================================================================== */

    public async Task<IReadOnlyList<ProductoDto>> BajoMinimoAsync(CancellationToken ct = default)
        => await Proyectar()
            .Where(p => p.Activo && p.Disponible <= p.Minimo)
            .OrderBy(p => p.Disponible).ThenBy(p => p.Nombre)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<CategoriaDto>> ListarCategoriasAsync(CancellationToken ct = default)
        => await _db.Categorias.AsNoTracking()
            .OrderBy(c => c.Orden).ThenBy(c => c.Nombre)
            .Select(c => new CategoriaDto
            {
                Id = c.Id,
                Nombre = c.Nombre,
                Orden = c.Orden,
                Productos = c.Productos.Count(p => p.Activo)
            })
            .ToListAsync(ct);

    public async Task<CategoriaDto> CrearCategoriaAsync(
        CrearCategoriaRequest peticion, CancellationToken ct = default)
    {
        var nombre = peticion.Nombre.Trim();

        if (await _db.Categorias.AnyAsync(c => c.Nombre.ToLower() == nombre.ToLower(), ct))
            throw new ExcepcionNegocio("Ya existe una categoría con ese nombre.",
                StatusCodes.Status409Conflict);

        var categoria = new Categoria { Nombre = nombre, Orden = peticion.Orden };
        _db.Categorias.Add(categoria);
        await _db.SaveChangesAsync(ct);

        return new CategoriaDto
        {
            Id = categoria.Id,
            Nombre = categoria.Nombre,
            Orden = categoria.Orden,
            Productos = 0
        };
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    /// <summary>
    /// Une la tabla de productos con la vista de disponibilidad. La vista no
    /// trae emoji ni control de lotes; la tabla no trae disponible ni margen.
    /// Es un solo SQL: EF resuelve el join en la base.
    /// </summary>
    private IQueryable<ProductoDto> Proyectar()
        => from p in _db.Productos.AsNoTracking()
           join d in _db.ProductosDisponibles on p.Id equals d.Id
           select new ProductoDto
           {
               Id = p.Id,
               Codigo = p.Codigo,
               Nombre = p.Nombre,
               Emoji = p.Emoji,
               CategoriaId = p.CategoriaId,
               Categoria = d.Categoria,
               Tipo = p.Tipo.ToString(),
               Precio = p.Precio,
               Minimo = p.Minimo,
               Activo = p.Activo,
               Stock = p.Stock,
               StockListo = p.StockListo,
               Disponible = d.Disponible,
               PosiblesDeArmar = d.PosiblesDeArmar,
               CostoUnitario = d.CostoUnitario,
               MargenPorcentaje = d.MargenPorcentaje,
               ControlaLotes = p.ControlaLotes,
               DiasVida = p.DiasVida,
               BajoMinimo = d.Disponible <= p.Minimo
           };

    private async Task<ProductoDetalleDto> ConDetalleAsync(ProductoDto b, CancellationToken ct)
    {
        var detalle = new ProductoDetalleDto
        {
            Id = b.Id,
            Codigo = b.Codigo,
            Nombre = b.Nombre,
            Emoji = b.Emoji,
            CategoriaId = b.CategoriaId,
            Categoria = b.Categoria,
            Tipo = b.Tipo,
            Precio = b.Precio,
            Minimo = b.Minimo,
            Activo = b.Activo,
            Stock = b.Stock,
            StockListo = b.StockListo,
            Disponible = b.Disponible,
            PosiblesDeArmar = b.PosiblesDeArmar,
            CostoUnitario = b.CostoUnitario,
            MargenPorcentaje = b.MargenPorcentaje,
            ControlaLotes = b.ControlaLotes,
            DiasVida = b.DiasVida,
            BajoMinimo = b.BajoMinimo,
            CostoArmado = await _db.Productos.Where(p => p.Id == b.Id)
                .Select(p => p.CostoArmado).FirstOrDefaultAsync(ct)
        };

        if (b.Tipo == nameof(TipoProducto.armado))
            detalle.Receta = await LeerRecetaAsync(b.Id, ct);
        else
            detalle.UsadoEn = await _db.Recetas.Where(r => r.ComponenteId == b.Id)
                .Select(r => r.Producto.Nombre).ToListAsync(ct);

        return detalle;
    }

    private async Task<IReadOnlyList<IngredienteDto>> LeerRecetaAsync(int id, CancellationToken ct)
        => await (from r in _db.Recetas.AsNoTracking()
                  join c in _db.Productos on r.ComponenteId equals c.Id
                  join d in _db.ProductosDisponibles on c.Id equals d.Id
                  where r.ProductoId == id
                  orderby c.Nombre
                  select new IngredienteDto
                  {
                      ProductoId = c.Id,
                      Codigo = c.Codigo,
                      Nombre = c.Nombre,
                      Emoji = c.Emoji,
                      Cantidad = r.Cantidad,
                      CostoUnitario = d.CostoUnitario,
                      Subtotal = d.CostoUnitario * r.Cantidad,
                      StockDisponible = d.Disponible,
                      AlcanzaPara = d.Disponible / r.Cantidad
                  }).ToListAsync(ct);

    /// <summary>
    /// Reemplaza la receta completa. Se valida que los ingredientes existan,
    /// sean simples y no se repitan; el trigger de la base lo comprueba igual,
    /// pero acá el mensaje es entendible.
    /// </summary>
    private async Task ReemplazarRecetaAsync(
        int productoId, List<LineaRecetaRequest> lineas, CancellationToken ct)
    {
        var repetidos = lineas.GroupBy(l => l.ProductoId).Where(g => g.Count() > 1).ToList();
        if (repetidos.Count > 0)
            throw new ExcepcionNegocio("Hay ingredientes repetidos en la receta.");

        if (lineas.Any(l => l.ProductoId == productoId))
            throw new ExcepcionNegocio("Un producto no puede ser ingrediente de sí mismo.");

        var ids = lineas.Select(l => l.ProductoId).ToList();
        var componentes = await _db.Productos
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Nombre, p.Tipo, p.Activo })
            .ToListAsync(ct);

        var faltantes = ids.Except(componentes.Select(c => c.Id)).ToList();
        if (faltantes.Count > 0)
            throw new ExcepcionNegocio($"No existen los productos: {string.Join(", ", faltantes)}.");

        var armados = componentes.Where(c => c.Tipo == TipoProducto.armado).ToList();
        if (armados.Count > 0)
            throw new ExcepcionNegocio(
                $"Una receta solo acepta productos simples. {string.Join(", ", armados.Select(a => a.Nombre))} " +
                "son productos armados: no puede haber ramos dentro de ramos.");

        var inactivos = componentes.Where(c => !c.Activo).ToList();
        if (inactivos.Count > 0)
            throw new ExcepcionNegocio(
                $"Están desactivados: {string.Join(", ", inactivos.Select(i => i.Nombre))}.");

        var actuales = await _db.Recetas.Where(r => r.ProductoId == productoId).ToListAsync(ct);
        _db.Recetas.RemoveRange(actuales);

        _db.Recetas.AddRange(lineas.Select(l => new Receta
        {
            ProductoId = productoId,
            ComponenteId = l.ProductoId,
            Cantidad = l.Cantidad
        }));

        await _db.SaveChangesAsync(ct);
    }

    private async Task<Producto> BuscarAsync(int id, CancellationToken ct)
        => await _db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct)
           ?? throw new NoEncontradoException("El producto");

    private static TipoProducto ATipo(string valor)
        => Enum.TryParse<TipoProducto>(valor?.Trim().ToLowerInvariant(), out var tipo)
            ? tipo
            : throw new ExcepcionNegocio("Tipo no válido. Debe ser simple o armado.");
}