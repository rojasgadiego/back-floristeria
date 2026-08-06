using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Features.Lotes.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using QRCoder;

namespace Colibri.Api.Features.Lotes;

public class LotesService : ILotesService
{
    private readonly ColibriDbContext _db;
    private readonly LotesOpciones _opciones;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<LotesService> _log;

    public LotesService(
        ColibriDbContext db,
        IOptions<LotesOpciones> opciones,
        IUsuarioActual usuarioActual,
        ILogger<LotesService> log)
    {
        _db = db;
        _opciones = opciones.Value;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    /// <summary>
    /// Lotes con existencias. Sale de vw_lotes_activos porque la vista ya
    /// calcula el orden FIFO con una función de ventana y la alerta: hacerlo
    /// en C# obligaría a traerse todos los lotes de cada producto para
    /// numerarlos.
    /// </summary>
    public async Task<ResultadoPagina<LoteDto>> ListarActivosAsync(
        LoteFiltro filtro, CancellationToken ct = default)
    {
        var consulta = ProyectarActivos();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(l =>
                l.Codigo.ToLower().Contains(q) ||
                l.Producto.ToLower().Contains(q) ||
                (l.Ubicacion != null && l.Ubicacion.ToLower().Contains(q)));
        }

        if (filtro.ProductoId.HasValue)
            consulta = consulta.Where(l => l.ProductoId == filtro.ProductoId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Alerta))
            consulta = consulta.Where(l => l.Alerta == filtro.Alerta.Trim().ToLower());

        // Rezagado: es el primero de la fila FIFO, ya empezado, y hay lotes
        // más nuevos esperando detrás.
        if (filtro.SoloRezagados)
            consulta = consulta.Where(l =>
                l.OrdenFifo == 1 &&
                l.VarasDisponibles < l.VarasIniciales &&
                _db.LotesActivos.Any(o => o.ProductoId == l.ProductoId && o.Id != l.Id));

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderBy(l => l.Producto).ThenBy(l => l.OrdenFifo)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<LoteDto>.Crear(items, total, filtro);
    }

    /// <summary>
    /// Todos los lotes, incluidos los agotados y descartados. Sale de la tabla
    /// porque la vista solo muestra los activos.
    /// </summary>
    public async Task<ResultadoPagina<LoteDto>> ListarHistorialAsync(
        HistorialLoteFiltro filtro, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var consulta = _db.Lotes.AsNoTracking().Select(l => new LoteDto
        {
            Id = l.Id,
            Codigo = l.Codigo,
            ProductoId = l.ProductoId,
            Producto = l.Producto.Nombre,
            Emoji = l.Producto.Emoji,
            Proveedor = l.Proveedor != null ? l.Proveedor.Nombre : null,
            Presentacion = l.Presentacion != null ? l.Presentacion.Nombre : null,
            FechaIngreso = l.FechaIngreso,
            FechaVencimiento = l.FechaVencimiento,
            DiasEnCamara = hoy.DayNumber - l.FechaIngreso.DayNumber,
            VarasIniciales = l.VarasIniciales,
            VarasDisponibles = l.VarasDisponibles,
            VarasConsumidas = l.VarasIniciales - l.VarasDisponibles,
            CostoPorVara = l.CostoPorVara,
            Ubicacion = l.Ubicacion,
            Alerta = l.Estado.ToString()
        });

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(l =>
                l.Codigo.ToLower().Contains(q) || l.Producto.ToLower().Contains(q));
        }

        if (filtro.ProductoId.HasValue)
            consulta = consulta.Where(l => l.ProductoId == filtro.ProductoId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
            consulta = consulta.Where(l => l.Alerta == AEstado(filtro.Estado).ToString());

        if (filtro.Desde.HasValue)
            consulta = consulta.Where(l => l.FechaIngreso >= filtro.Desde.Value);

        if (filtro.Hasta.HasValue)
            consulta = consulta.Where(l => l.FechaIngreso <= filtro.Hasta.Value);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(l => l.FechaIngreso).ThenByDescending(l => l.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<LoteDto>.Crear(items, total, filtro);
    }

    public async Task<LoteDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var codigo = await _db.Lotes.AsNoTracking()
            .Where(l => l.Id == id).Select(l => l.Codigo).FirstOrDefaultAsync(ct)
            ?? throw new NoEncontradoException("El lote");

        return await ObtenerPorCodigoAsync(codigo, ct);
    }

    public async Task<LoteDetalleDto> ObtenerPorCodigoAsync(
        string codigo, CancellationToken ct = default)
    {
        var limpio = Normalizar(codigo);
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var lote = await _db.Lotes.AsNoTracking()
            .Where(l => l.Codigo == limpio)
            .Select(l => new
            {
                l.Id,
                l.Codigo,
                l.ProductoId,
                Producto = l.Producto.Nombre,
                l.Producto.Emoji,
                Proveedor = l.Proveedor != null ? l.Proveedor.Nombre : null,
                Presentacion = l.Presentacion != null ? l.Presentacion.Nombre : null,
                l.FechaIngreso,
                l.FechaVencimiento,
                l.VarasIniciales,
                l.VarasDisponibles,
                l.CostoPorVara,
                l.Estado,
                l.Ubicacion,
                l.Notas,
                l.CompraId,
                CompraFolio = l.Compra != null ? l.Compra.Folio : null,
                Documento = l.Compra != null ? l.Compra.Documento : null
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NoEncontradoException($"El lote {limpio}");

        // El orden FIFO solo tiene sentido entre lotes activos
        var orden = lote.Estado == EstadoLote.activo
            ? await _db.LotesActivos.AsNoTracking()
                .Where(l => l.Id == lote.Id).Select(l => l.OrdenFifo).FirstOrDefaultAsync(ct)
            : 0;

        var movimientos = await _db.MovimientosInventario.AsNoTracking()
            .Where(m => m.LoteId == lote.Id)
            .OrderByDescending(m => m.CreadoEn)
            .Select(m => new MovimientoLoteDto
            {
                Id = m.Id,
                Fecha = m.CreadoEn,
                Tipo = m.Tipo.ToString(),
                Cantidad = m.Cantidad,
                Motivo = m.Motivo,
                Usuario = m.Usuario != null ? m.Usuario.Nombre : null
            })
            .ToListAsync(ct);

        var consumidas = lote.VarasIniciales - lote.VarasDisponibles;

        return new LoteDetalleDto
        {
            Id = lote.Id,
            Codigo = lote.Codigo,
            ProductoId = lote.ProductoId,
            Producto = lote.Producto,
            Emoji = lote.Emoji,
            Proveedor = lote.Proveedor,
            Presentacion = lote.Presentacion,
            FechaIngreso = lote.FechaIngreso,
            FechaVencimiento = lote.FechaVencimiento,
            DiasEnCamara = hoy.DayNumber - lote.FechaIngreso.DayNumber,
            DiasParaVencer = lote.FechaVencimiento.HasValue
                ? lote.FechaVencimiento.Value.DayNumber - hoy.DayNumber
                : null,
            VarasIniciales = lote.VarasIniciales,
            VarasDisponibles = lote.VarasDisponibles,
            VarasConsumidas = consumidas,
            PorcentajeVendido = lote.VarasIniciales > 0
                ? Math.Round(100m * consumidas / lote.VarasIniciales, 1)
                : 0,
            CostoPorVara = lote.CostoPorVara,
            ValorRestante = (int)Math.Round(lote.CostoPorVara * lote.VarasDisponibles),
            Ubicacion = lote.Ubicacion,
            OrdenFifo = orden,
            Alerta = lote.Estado.ToString(),
            Estado = lote.Estado.ToString(),
            CompraId = lote.CompraId,
            CompraFolio = lote.CompraFolio,
            Documento = lote.Documento,
            Notas = lote.Notas,
            ContenidoQr = ContenidoQr(lote.Codigo),
            Movimientos = movimientos
        };
    }

    /* ==================================================================
       ESCANEO
       ================================================================== */

    /// <summary>
    /// Lo que responde el escaneo del QR en el punto de venta.
    ///
    /// Es una consulta informativa: sirve para avisar antes de perder el
    /// tiempo. La validación que manda ocurre al cobrar, dentro de
    /// fn_consumir_lotes, que bloquea las filas —si entre escanear y cobrar
    /// otra caja se llevó el stock, la venta falla ahí, que es donde debe.
    /// </summary>
    public async Task<ValidacionDto> ValidarAsync(
        ValidarLoteRequest peticion, CancellationToken ct = default)
    {
        var codigo = Normalizar(peticion.Codigo);
        var cantidad = Math.Max(1, peticion.Cantidad);

        // fn_validar_lote lanza excepción si el código no existe;
        // ManejadorExcepciones traduce el P0001 a un 400 con su texto.
        var fila = await _db.ValidacionesLote
            .FromSqlInterpolated($"SELECT * FROM fn_validar_lote({codigo}, {cantidad})")
            .AsNoTracking()
            .FirstOrDefaultAsync(ct)
            ?? throw new NoEncontradoException($"El lote {codigo}");

        var producto = await _db.Productos.AsNoTracking()
            .Where(p => p.Id == fila.ProductoId)
            .Select(p => new { p.Emoji, p.Precio })
            .FirstAsync(ct);

        // Que no alcance NO impide vender: el resto lo completa el siguiente
        // lote por antigüedad. Lo que impide vender es que esté agotado o
        // descartado, y eso llega en la advertencia.
        var bloqueado = fila.VarasDisponibles == 0;

        return new ValidacionDto
        {
            LoteId = fila.LoteId,
            Codigo = fila.Codigo,
            ProductoId = fila.ProductoId,
            Producto = fila.Producto,
            Emoji = producto.Emoji,
            Precio = producto.Precio,
            VarasDisponibles = fila.VarasDisponibles,
            CantidadPedida = cantidad,
            FechaIngreso = fila.FechaIngreso,
            FechaVencimiento = fila.FechaVencimiento,
            DiasEnCamara = fila.DiasEnCamara,
            Vencido = fila.Vencido,
            Alcanza = fila.Alcanza,
            SePuedeVender = !bloqueado,
            HayLoteAnterior = fila.HayLoteAnterior,
            LoteAnterior = fila.LoteAnterior,
            VarasAnteriores = fila.VarasAnteriores,
            Advertencia = fila.Advertencia
        };
    }

    /* ==================================================================
       ALERTAS
       ================================================================== */

    /// <summary>
    /// Restos: lotes viejos con varas que quedaron atrás porque se empezó a
    /// vender de uno nuevo. Son los candidatos a merma si no se liquidan.
    /// </summary>
    public async Task<IReadOnlyList<LoteDto>> RezagadosAsync(CancellationToken ct = default)
        => await ProyectarActivos()
            .Where(l => l.OrdenFifo == 1
                     && l.VarasDisponibles < l.VarasIniciales
                     && _db.LotesActivos.Any(o => o.ProductoId == l.ProductoId && o.Id != l.Id))
            .OrderByDescending(l => l.DiasEnCamara)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<LoteDto>> PorVencerAsync(
        int dias, CancellationToken ct = default)
        => await ProyectarActivos()
            .Where(l => l.DiasParaVencer != null && l.DiasParaVencer <= dias)
            .OrderBy(l => l.DiasParaVencer)
            .ToListAsync(ct);

    /// <summary>
    /// Costo promedio ponderado de lo que hay en cámara. Con lotes de $900 y
    /// $800 mezclados no es el promedio simple: pesa cuántas varas quedan de
    /// cada uno.
    /// </summary>
    public async Task<IReadOnlyList<CostoPromedioDto>> CostoPromedioAsync(
        CancellationToken ct = default)
        => await (from c in _db.CostosPromedio.AsNoTracking()
                  join p in _db.Productos on c.ProductoId equals p.Id
                  orderby p.Nombre
                  select new CostoPromedioDto
                  {
                      ProductoId = c.ProductoId,
                      Producto = p.Nombre,
                      Varas = c.Varas,
                      ValorTotal = c.ValorTotal,
                      CostoPromedio = c.Costo,
                      CostoReferencia = p.Costo
                  }).ToListAsync(ct);

    /// <summary>
    /// El balde aparte: flor que volvió del desarme o de un pedido no usado.
    /// Se vende escaneando, a su propio precio.
    /// </summary>
    public async Task<IReadOnlyList<LoteRecuperadoDto>> RecuperadosAsync(
        CancellationToken ct = default)
        => await _db.LotesRecuperados.AsNoTracking()
            .OrderBy(l => l.Producto).ThenBy(l => l.FechaIngreso)
            .Select(l => new LoteRecuperadoDto
            {
                Id = l.Id,
                Codigo = l.Codigo,
                ProductoId = l.ProductoId,
                Producto = l.Producto,
                Emoji = l.Emoji,
                Proveedor = l.Proveedor,
                Presentacion = l.Presentacion,
                FechaIngreso = l.FechaIngreso,
                FechaVencimiento = l.FechaVencimiento,
                DiasEnCamara = l.DiasEnCamara,
                DiasParaVencer = l.DiasParaVencer,
                VarasIniciales = l.VarasIniciales,
                VarasDisponibles = l.VarasDisponibles,
                VarasConsumidas = l.VarasConsumidas,
                PorcentajeVendido = l.PorcentajeVendido,
                CostoPorVara = l.CostoPorVara,
                ValorRestante = l.ValorRestante,
                Ubicacion = l.Ubicacion,
                OrdenFifo = l.OrdenFifo,
                Calidad = l.Calidad != null ? l.Calidad.ToString() : null,
                OrigenLoteId = l.OrigenLoteId,
                LoteOrigen = l.LoteOrigen,
                PrecioUnitario = l.PrecioUnitario,
                PrecioVenta = l.PrecioVenta,
                EsRecuperado = l.EsRecuperado,
                RequiereEscaneo = l.RequiereEscaneo,
                Alerta = l.Alerta,
                Rebaja = l.Rebaja,
                RebajaPorcentaje = l.RebajaPorcentaje
            })
            .ToListAsync(ct);

    /* ==================================================================
       ESCRITURA
       ================================================================== */

    /// <summary>
    /// Registra dónde está el paquete. Es lo único editable de un lote: las
    /// varas se mueven recibiendo, vendiendo o mermando, nunca escribiéndolas.
    ///
    /// Anotarla vale la pena para el caso incómodo: encontrar un balde sin
    /// etiqueta y no saber qué lote es.
    /// </summary>
    public async Task<LoteDto> ActualizarUbicacionAsync(
        int id, ActualizarUbicacionRequest peticion, CancellationToken ct = default)
    {
        var lote = await _db.Lotes.FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NoEncontradoException("El lote");

        lote.Ubicacion = peticion.Ubicacion.Trim();
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Lote {Codigo} movido a {Ubicacion} por {Autor}",
            lote.Codigo, lote.Ubicacion, _usuarioActual.Email);

        return await ProyectarActivos().FirstOrDefaultAsync(l => l.Id == id, ct)
            ?? throw new NoEncontradoException("El lote");
    }

    /* ==================================================================
       ETIQUETAS Y QR
       ================================================================== */

    public async Task<IReadOnlyList<EtiquetaDto>> EtiquetasAsync(
        IEnumerable<int> ids, CancellationToken ct = default)
    {
        var lista = ids?.Distinct().ToList() ?? new List<int>();
        if (lista.Count == 0)
            throw new ExcepcionNegocio("Indica al menos un lote.");

        return await ConstruirEtiquetasAsync(l => lista.Contains(l.Id), ct);
    }

    /// <summary>
    /// Etiquetas de todos los lotes de una compra: es el flujo real, porque
    /// se imprimen al recibir la mercadería, antes de meterla a la cámara.
    /// </summary>
    public async Task<IReadOnlyList<EtiquetaDto>> EtiquetasDeCompraAsync(
        int compraId, CancellationToken ct = default)
    {
        var etiquetas = await ConstruirEtiquetasAsync(l => l.CompraId == compraId, ct);

        if (etiquetas.Count == 0)
            throw new ExcepcionNegocio(
                "Esa compra no tiene lotes. Recíbela primero para generarlos.");

        return etiquetas;
    }

    /// <summary>
    /// Imagen PNG del QR. Contiene la URL que abre la ficha del lote; ningún
    /// dato del lote viaja en el papel, así que una etiqueta perdida se
    /// reimprime sin consecuencias.
    /// </summary>
    public async Task<byte[]> GenerarQrAsync(string codigo, CancellationToken ct = default)
    {
        var limpio = Normalizar(codigo);

        if (!await _db.Lotes.AsNoTracking().AnyAsync(l => l.Codigo == limpio, ct))
            throw new NoEncontradoException($"El lote {limpio}");

        using var generador = new QRCodeGenerator();
        // ECCLevel.Q tolera un 25% de daño: una etiqueta en cámara fría se
        // moja, se raya y se despega en los bordes.
        using var datos = generador.CreateQrCode(
            ContenidoQr(limpio), QRCodeGenerator.ECCLevel.Q);

        var png = new PngByteQRCode(datos);
        return png.GetGraphic(_opciones.PixelesPorModulo);
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    private async Task<IReadOnlyList<EtiquetaDto>> ConstruirEtiquetasAsync(
        System.Linq.Expressions.Expression<Func<Domain.Entities.Lote, bool>> filtro,
        CancellationToken ct)
        => await _db.Lotes.AsNoTracking()
            .Where(filtro)
            .OrderBy(l => l.Codigo)
            .Select(l => new EtiquetaDto
            {
                LoteId = l.Id,
                Codigo = l.Codigo,
                Producto = l.Producto.Nombre,
                Emoji = l.Producto.Emoji,
                Proveedor = l.Proveedor != null ? l.Proveedor.Nombre : null,
                FechaIngreso = l.FechaIngreso,
                FechaVencimiento = l.FechaVencimiento,
                Varas = l.VarasIniciales,
                Ubicacion = l.Ubicacion,
                ContenidoQr = _opciones.UrlBase.TrimEnd('/') + "/" + l.Codigo,
                UrlQr = "/api/lotes/" + l.Codigo + "/qr"
            })
            .ToListAsync(ct);

    private IQueryable<LoteDto> ProyectarActivos()
        => from l in _db.LotesActivos.AsNoTracking()
           select new LoteDto
           {
               Id = l.Id,
               Codigo = l.Codigo,
               ProductoId = l.ProductoId,
               Producto = l.Producto,
               Emoji = l.Emoji,
               Proveedor = l.Proveedor,
               Presentacion = l.Presentacion,
               FechaIngreso = l.FechaIngreso,
               FechaVencimiento = l.FechaVencimiento,
               DiasEnCamara = l.DiasEnCamara,
               DiasParaVencer = l.DiasParaVencer,
               VarasIniciales = l.VarasIniciales,
               VarasDisponibles = l.VarasDisponibles,
               VarasConsumidas = l.VarasConsumidas,
               PorcentajeVendido = l.PorcentajeVendido,
               CostoPorVara = l.CostoPorVara,
               ValorRestante = l.ValorRestante,
               Ubicacion = l.Ubicacion,
               OrdenFifo = l.OrdenFifo,
               Calidad = l.Calidad != null ? l.Calidad.ToString() : null,
               OrigenLoteId = l.OrigenLoteId,
               LoteOrigen = l.LoteOrigen,
               PrecioUnitario = l.PrecioUnitario,
               PrecioVenta = l.PrecioVenta,
               EsRecuperado = l.EsRecuperado,
               RequiereEscaneo = l.RequiereEscaneo,
               Alerta = l.Alerta
           };

    private string ContenidoQr(string codigo)
        => _opciones.UrlBase.TrimEnd('/') + "/" + codigo;

    /// <summary>
    /// El código va en mayúsculas y sin espacios: si alguien lo tipea a mano
    /// porque el QR se borró, debe encontrarlo igual.
    /// </summary>
    private static string Normalizar(string? codigo)
        => (codigo ?? string.Empty).Trim().ToUpperInvariant();

    private static EstadoLote AEstado(string valor)
        => Enum.TryParse<EstadoLote>(valor?.Trim().ToLowerInvariant(), out var e)
            ? e
            : throw new ExcepcionNegocio("Estado no válido. Debe ser activo, agotado o descartado.");
}