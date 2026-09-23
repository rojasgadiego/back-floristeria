using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// Cotizaciones y eventos.
///
/// `soloDe` en cada método es la regla de privacidad: null = administrador,
/// ve y opera todas; con un id, el vendedor ve y opera solo las que creó.
/// Para él una cotización ajena NO EXISTE: se responde igual que a un id
/// inventado, sin confirmar que haya algo detrás.
/// </summary>
public class CotizacionesBLL
{
    private readonly CotizacionesDAL _dal;

    public CotizacionesBLL(CotizacionesDAL dal) => _dal = dal;

    private const string NoExiste = "La cotización no existe.";

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResultadoPagina<CotizacionResumen>> Listar(
        CotizacionFiltro filtro, int? soloDe, CancellationToken ct = default)
    {
        var filas = (await _dal.Listar(filtro, soloDe, ct)).ToList();

        return new ResultadoPagina<CotizacionResumen>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    /// <summary>
    /// La ficha: cinco consultas porque son listas distintas, y unirlas en un
    /// SELECT multiplicaría las filas de cada una por las de las otras.
    /// </summary>
    public async Task<CotizacionDetalle?> Obtener(int id, int? soloDe, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var c = await _dal.Cabecera(id, soloDe, ct);
        if (c is null) return null;

        c.Items = (await _dal.Items(id, ct)).ToList();
        c.Faltantes = (await _dal.Faltantes(id, ct)).ToList();
        c.PlanCuotas = (await _dal.Cuotas(id, ct)).ToList();
        c.HistorialPagos = (await _dal.Pagos(id, ct)).ToList();
        c.Resultado = c.Estado == "cobrada" ? await _dal.Resultado(id, ct) : null;

        return c;
    }

    public async Task<IEnumerable<CotizacionResumen>> PorCobrar(int? soloDe, CancellationToken ct = default)
        => await _dal.PorCobrar(soloDe, ct);

    public async Task<IEnumerable<CotizacionResumen>> Agenda(
        int? dias, int? soloDe, CancellationToken ct = default)
        => await _dal.Agenda(dias is null or < 1 or > 365 ? 30 : dias.Value, soloDe, ct);

    /// <summary>
    /// La boleta final sugerida. NO cobra. Las advertencias se arman acá
    /// porque cruzan el estado, la fecha y el stock, y se leen como una lista.
    /// </summary>
    public async Task<PreparacionCobro?> PrepararCobro(int id, int? soloDe, CancellationToken ct = default)
    {
        var c = await Obtener(id, soloDe, ct);
        if (c is null) return null;

        var p = new PreparacionCobro
        {
            CotizacionId = c.Id,
            Folio = c.Folio,
            Lineas = (await _dal.LineasCobro(id, ct)).ToList(),
            TotalCotizado = c.Total,
            AbonoPrevio = c.Abono,
            SaldoACobrar = c.Saldo
        };

        if (c.Estado != "aprobada")
            p.Advertencias.Add($"Solo se cobra una cotización aprobada; esta está {c.Estado}.");

        foreach (var f in c.Faltantes)
            p.Advertencias.Add($"{f.Producto}: se cotizaron {f.Comprometido} y hay {f.Disponible}. Faltan {f.Faltante}.");

        if (c.FechaEvento is { } fecha && fecha > DateOnly.FromDateTime(DateTime.Today))
            p.Advertencias.Add($"El evento es el {fecha:dd-MM}: normalmente se cobra al entregar.");

        if (c.Items.Any(i => i.AMedida))
            p.Advertencias.Add("Las líneas a medida no descuentan inventario: revisa qué flor se usó.");

        return p;
    }

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<ResultadoOp<CotizacionDetalle>> Crear(
        CotizacionRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<CotizacionDetalle>.Error("Sesión inválida.");

        var error = ValidarPresupuesto(r);
        if (error is not null) return ResultadoOp<CotizacionDetalle>.Error(error);

        var (id, err) = await _dal.Crear(r, usuarioId, ct);
        return await Releer(id, err, null, ct);
    }

    public async Task<ResultadoOp<CotizacionDetalle>> Actualizar(
        int id, CotizacionRequest r, int? soloDe, CancellationToken ct = default)
    {
        if (!await EsVisible(id, soloDe, ct)) return ResultadoOp<CotizacionDetalle>.Error(NoExiste);

        var error = ValidarPresupuesto(r);
        if (error is not null) return ResultadoOp<CotizacionDetalle>.Error(error);

        var (_, err) = await _dal.Actualizar(id, r, ct);
        return await Releer(id, err, soloDe, ct);
    }

    public async Task<ResultadoOp<CotizacionDetalle>> Aprobar(int id, int? soloDe, CancellationToken ct = default)
    {
        if (!await EsVisible(id, soloDe, ct)) return ResultadoOp<CotizacionDetalle>.Error(NoExiste);

        var (_, err) = await _dal.Aprobar(id, ct);
        return await Releer(id, err, soloDe, ct);
    }

    /// <summary>Solo administración: lo exige el endpoint.</summary>
    public async Task<ResultadoOp<CotizacionDetalle>> Anular(
        int id, string motivo, int usuarioId, CancellationToken ct = default)
    {
        if ((motivo ?? "").Trim().Length < 5)
            return ResultadoOp<CotizacionDetalle>.Error("Explica el motivo, con al menos 5 caracteres.");

        var (_, err) = await _dal.Anular(id, motivo!.Trim(), usuarioId, ct);
        return await Releer(id, err, null, ct);
    }

    public async Task<ResultadoOp<ResultadoPagoCotizacion>> RegistrarPago(
        int id, PagoCotizacionRequest r, int usuarioId, int? soloDe, CancellationToken ct = default)
    {
        if (!await EsVisible(id, soloDe, ct)) return ResultadoOp<ResultadoPagoCotizacion>.Error(NoExiste);
        if (usuarioId <= 0) return ResultadoOp<ResultadoPagoCotizacion>.Error("Sesión inválida.");
        if (r.Monto < 1) return ResultadoOp<ResultadoPagoCotizacion>.Error("El monto debe ser mayor que cero.");

        return await _dal.RegistrarPago(id, r, usuarioId, ct);
    }

    /// <summary>Solo administración: lo exige el endpoint.</summary>
    public async Task<ResultadoOp<ResultadoAnulacionPago>> AnularPago(
        int id, int pagoId, string motivo, int usuarioId, CancellationToken ct = default)
    {
        if ((motivo ?? "").Trim().Length < 5)
            return ResultadoOp<ResultadoAnulacionPago>.Error("Explica el motivo, con al menos 5 caracteres.");

        return await _dal.AnularPago(id, pagoId, motivo!.Trim(), usuarioId, ct);
    }

    public async Task<ResultadoOp<List<CotizacionCuota>>> GuardarCuotas(
        int id, CuotasRequest r, int? soloDe, CancellationToken ct = default)
    {
        if (!await EsVisible(id, soloDe, ct)) return ResultadoOp<List<CotizacionCuota>>.Error(NoExiste);

        var (_, err) = await _dal.GuardarCuotas(id, r ?? new CuotasRequest(), ct);
        return await ReleerCuotas(id, err, ct);
    }

    public async Task<ResultadoOp<List<CotizacionCuota>>> GenerarCuotas(
        int id, GenerarCuotasRequest r, int? soloDe, CancellationToken ct = default)
    {
        if (!await EsVisible(id, soloDe, ct)) return ResultadoOp<List<CotizacionCuota>>.Error(NoExiste);

        var (_, err) = await _dal.GenerarCuotas(id, r ?? new GenerarCuotasRequest(), ct);
        return await ReleerCuotas(id, err, ct);
    }

    // ============================================================
    // Apoyo
    // ============================================================

    /// <summary>
    /// La usa también el cobro en el punto de venta: un vendedor no puede
    /// cerrar un evento que no es suyo.
    /// </summary>
    public async Task<bool> EsVisible(int id, int? soloDe, CancellationToken ct = default)
        => id > 0 && await _dal.Cabecera(id, soloDe, ct) is not null;

    /// <summary>La respuesta de una escritura es la ficha tal como quedó.</summary>
    private async Task<ResultadoOp<CotizacionDetalle>> Releer(
        int id, string error, int? soloDe, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<CotizacionDetalle>.Error(error);

        var c = await Obtener(id, soloDe, ct);
        return c is null
            ? ResultadoOp<CotizacionDetalle>.Error("Se guardó, pero no se pudo leer la cotización.")
            : ResultadoOp<CotizacionDetalle>.Exito(c);
    }

    private async Task<ResultadoOp<List<CotizacionCuota>>> ReleerCuotas(
        int id, string error, CancellationToken ct)
        => string.IsNullOrWhiteSpace(error)
            ? ResultadoOp<List<CotizacionCuota>>.Exito((await _dal.Cuotas(id, ct)).ToList())
            : ResultadoOp<List<CotizacionCuota>>.Error(error);

    /// <summary>Lo que se puede decir sin ir a la base; el resto lo valida el SP.</summary>
    private static string? ValidarPresupuesto(CotizacionRequest r)
    {
        if (r is null) return "Request inválido.";
        if ((r.ClienteNombre ?? "").Trim().Length < 2) return "Indica a nombre de quién va el evento.";
        if (string.IsNullOrWhiteSpace(r.TipoEvento)) return "Indica el tipo de evento.";
        if (r.Items is null || r.Items.Count == 0) return "El presupuesto necesita al menos una línea.";
        if (r.Items.Any(l => l.Cantidad < 1)) return "Todas las cantidades deben ser al menos 1.";
        if (r.Items.Any(l => !l.AMedida && l.ProductoId is null)) return "Falta el producto en una línea.";
        if (r.Traslado < 0 || r.Montaje < 0) return "El traslado y el montaje no pueden ser negativos.";
        return null;
    }
}
