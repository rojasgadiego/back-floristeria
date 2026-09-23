using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de COTIZACIONES Y EVENTOS.
///
/// El grupo es de la política Vender: el presupuesto se arma en el mesón,
/// con el cliente al frente. Aprobarla, anularla o anular un abono es de
/// administración: aprobar compromete al local, y el abono ya es una boleta
/// que toca el arqueo.
///
/// Privacidad: un vendedor ve y opera solo las cotizaciones que creó; el
/// administrador, todas. Lo decide el token (SoloDe), nunca el cliente.
/// </summary>
public class CotizacionesEndpoints : EndpointsBase
{
    public CotizacionesEndpoints(ILogger<CotizacionesEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var cot = app.MapGroup("/api/cotizaciones")
            .WithTags("Cotizaciones")
            .RequireAuthorization(Politicas.Vender)
            .AddEndpointFilter<RespuestaFilter>();

        // ═══ Literales antes que /{id:int} ═══

        cot.MapGet("/", Listar)
            .WithName("ListarCotizaciones")
            .Produces<ResponseDto>(200);

        cot.MapGet("/por-cobrar", PorCobrar)
            .WithName("CotizacionesPorCobrar")
            .WithSummary("Con saldo vencido, del más atrasado al menos")
            .Produces<ResponseDto>(200);

        cot.MapGet("/agenda", Agenda)
            .WithName("AgendaEventos")
            .WithSummary("Eventos aprobados de los próximos días")
            .Produces<ResponseDto>(200);

        cot.MapPost("/", Crear)
            .WithName("CrearCotizacion")
            .Produces<ResponseDto>(201).Produces(400);

        cot.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerCotizacion")
            .Produces<ResponseDto>(200).Produces(404);

        cot.MapPut("/{id:int}", Actualizar)
            .WithName("ActualizarCotizacion")
            .WithSummary("Solo un borrador")
            .Produces<ResponseDto>(200).Produces(400);

        // Solo administración: aprobar compromete al local con el cliente.
        cot.MapPatch("/{id:int}/aprobar", Aprobar)
            .WithName("AprobarCotizacion")
            .WithSummary("La pone en la agenda. No aparta inventario. Solo admin.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cot.MapPost("/{id:int}/anular", Anular)
            .WithName("AnularCotizacion")
            .WithSummary("Los abonos quedan como saldo a favor")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cot.MapPost("/{id:int}/pagos", RegistrarPago)
            .WithName("RegistrarAbono")
            .WithSummary("Emite una boleta en la caja abierta")
            .Produces<ResponseDto>(201).Produces(400);

        cot.MapPost("/{id:int}/pagos/{pagoId:int}/anular", AnularPago)
            .WithName("AnularAbono")
            .WithSummary("Anula el abono y su boleta")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cot.MapPut("/{id:int}/cuotas", GuardarCuotas)
            .WithName("GuardarCuotas")
            .WithSummary("Reemplaza el plan. Debe sumar el saldo exacto.")
            .Produces<ResponseDto>(200).Produces(400);

        cot.MapPost("/{id:int}/cuotas/generar", GenerarCuotas)
            .WithName("GenerarCuotas")
            .WithSummary("Reparte el saldo en cuotas iguales")
            .Produces<ResponseDto>(200).Produces(400);

        cot.MapGet("/{id:int}/preparar-cobro", PrepararCobro)
            .WithName("PrepararCobro")
            .WithSummary("Líneas sugeridas para la boleta final. No cobra.")
            .Produces<ResponseDto>(200).Produces(404);
    }

    /// <summary>Null para el administrador; el id del token para un vendedor.</summary>
    private static int? SoloDe(HttpContext http)
        => http.User.IsInRole("admin") ? null : UsuarioActual(http);

    #region Consultas

    public async Task<ResponseDto> Listar(
        [AsParameters] CotizacionFiltro filtro, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Consultar(() => bll.Listar(filtro, SoloDe(http), ct), "cotizaciones");

    public async Task<ResponseDto> Obtener(int id, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(() => bll.Obtener(id, SoloDe(http), ct),
            "Cotización", "La cotización no existe.");

    public async Task<ResponseDto> PorCobrar(CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Consultar(() => bll.PorCobrar(SoloDe(http), ct), "cotizaciones por cobrar");

    public async Task<ResponseDto> Agenda(
        [FromQuery] int? dias, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Consultar(() => bll.Agenda(dias, SoloDe(http), ct), "eventos");

    public async Task<ResponseDto> PrepararCobro(int id, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(() => bll.PrepararCobro(id, SoloDe(http), ct),
            "Cobro", "La cotización no existe.");

    #endregion

    #region Escritura

    public async Task<ResponseDto> Crear(
        [FromBody] CotizacionRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Crear(peticion, UsuarioActual(http), ct),
            c => $"{c.Folio} guardada en borrador", HttpStatusCodes.Created, "crear la cotización");

    public async Task<ResponseDto> Actualizar(
        int id, [FromBody] CotizacionRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Actualizar(id, peticion, SoloDe(http), ct),
            c => $"{c.Folio} actualizada", HttpStatusCodes.Ok, "actualizar la cotización");

    public async Task<ResponseDto> Aprobar(int id, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Aprobar(id, SoloDe(http), ct),
            c => $"{c.Folio} aprobada · ya está en la agenda", HttpStatusCodes.Ok, "aprobar la cotización");

    public async Task<ResponseDto> Anular(
        int id, [FromBody] MotivoRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Anular(id, peticion?.Motivo ?? "", UsuarioActual(http), ct),
            c => $"{c.Folio} anulada", HttpStatusCodes.Ok, "anular la cotización");

    public async Task<ResponseDto> RegistrarPago(
        int id, [FromBody] PagoCotizacionRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.RegistrarPago(id, peticion, UsuarioActual(http), SoloDe(http), ct),
            p => $"Abono de ${p.Monto:N0} · boleta {p.VentaFolio}", HttpStatusCodes.Created, "registrar el abono");

    public async Task<ResponseDto> AnularPago(
        int id, int pagoId, [FromBody] MotivoRequest peticion, CotizacionesBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.AnularPago(id, pagoId, peticion?.Motivo ?? "", UsuarioActual(http), ct),
            p => $"Abono y boleta {p.VentaFolio} anulados", HttpStatusCodes.Ok, "anular el abono");

    public async Task<ResponseDto> GuardarCuotas(
        int id, [FromBody] CuotasRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.GuardarCuotas(id, peticion, SoloDe(http), ct),
            q => q.Count == 0 ? "Plan borrado" : $"Plan de {q.Count} cuota(s) guardado",
            HttpStatusCodes.Ok, "guardar el plan de pago");

    public async Task<ResponseDto> GenerarCuotas(
        int id, [FromBody] GenerarCuotasRequest peticion, CotizacionesBLL bll, HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.GenerarCuotas(id, peticion, SoloDe(http), ct),
            q => $"Saldo repartido en {q.Count} cuota(s)", HttpStatusCodes.Ok, "generar las cuotas");

    #endregion

    private async Task<ResponseDto> Escribir<T>(
        Func<Task<ResultadoOp<T>>> operacion, Func<T, string> mensajeExito,
        int statusExito, string queSeHacia)
    {
        try
        {
            var r = await operacion();

            return r.Ok
                ? CustomUtilz.CreateResponse(statusExito, mensajeExito(r.Datos!), r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al {Que}", queSeHacia);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al {queSeHacia}: {ex.Message}", null);
        }
    }
}
