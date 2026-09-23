using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de CAJA. Abrir y cerrar es de quien vende; el historial completo,
/// solo del admin.
/// </summary>
public class CajaEndpoints : EndpointsBase
{
    public CajaEndpoints(ILogger<CajaEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var caja = app.MapGroup("/api/caja")
            .WithTags("Caja")
            .RequireAuthorization(Politicas.Vender)
            .AddEndpointFilter<RespuestaFilter>();

        // Literales antes que /{id:int}.
        caja.MapGet("/actual", Actual)
            .WithName("CajaActual")
            .WithSummary("El turno abierto con su arqueo, o null si no hay ninguno")
            .Produces<ResponseDto>(200);

        caja.MapGet("/historial", Historial)
            .WithName("HistorialCajas")
            .WithSummary("Turnos cerrados. Un vendedor ve solo los suyos.")
            .Produces<ResponseDto>(200);

        caja.MapPost("/abrir", Abrir)
            .WithName("AbrirCaja")
            .WithSummary("Sin caja abierta no se vende. Solo puede haber una.")
            .Produces<ResponseDto>(201).Produces(400);

        caja.MapPost("/cerrar", Cerrar)
            .WithName("CerrarCaja")
            .WithSummary("Se informa lo contado; el esperado lo calcula el sistema.")
            .Produces<ResponseDto>(200).Produces(400);

        caja.MapGet("/{id:int}/resumen", Resumen)
            .WithName("ResumenCaja")
            .Produces<ResponseDto>(200).Produces(404);
    }

    /// <summary>
    /// 200 con datos en null cuando no hay caja abierta. Un 404 haría pensar
    /// que la ruta está mal; la ausencia de turno es el estado normal antes
    /// de que alguien abra.
    /// </summary>
    /// <summary>
    /// Null para el administrador (ve el turno completo); el id del token
    /// para cualquier otro. Un vendedor ve solo lo suyo, jamás lo de otros.
    /// </summary>
    private static int? SoloDe(HttpContext http)
        => http.User.IsInRole("admin") ? null : UsuarioActual(http);

    public async Task<ResponseDto> Actual(CajaBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var caja = await bll.Actual(SoloDe(http), ct);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok,
                caja is null ? "No hay ninguna caja abierta." : "Caja abierta.",
                caja);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al consultar la caja actual");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al consultar la caja.", null);
        }
    }

    public async Task<ResponseDto> Resumen(int id, CajaBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(() => bll.Resumen(id, SoloDe(http), ct), "Caja", $"No existe la caja {id}.");

    /// <summary>
    /// El usuarioId del filtro se IGNORA si viene del cliente: se arma desde
    /// el token. Un vendedor que editara la URL vería los turnos de otro, y
    /// eso incluye cuánta plata manejó y con cuánta diferencia cerró.
    /// </summary>
    public async Task<ResponseDto> Historial(
        [AsParameters] CajaFiltro filtro, CajaBLL bll, HttpContext http, CancellationToken ct)
    {
        var soloDe = SoloDe(http);

        // El admin puede filtrar por persona; a un vendedor se le ignora lo
        // que mande y ve los turnos en los que él vendió, con sus totales.
        if (soloDe is not null) filtro.UsuarioId = null;

        return await Consultar(() => bll.Historial(filtro, soloDe, ct), "turnos de caja");
    }

    public async Task<ResponseDto> Abrir(
        [FromBody] AbrirCajaRequest peticion, CajaBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var r = await bll.Abrir(peticion ?? new AbrirCajaRequest(), UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Created,
                    "Caja abierta · ya puedes vender", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.Conflict, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al abrir la caja");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al abrir la caja: {ex.Message}", null);
        }
    }

    /// <summary>
    /// El mensaje de éxito dice la diferencia, porque es lo único que la
    /// persona quiere saber en ese momento.
    /// </summary>
    public async Task<ResponseDto> Cerrar(
        [FromBody] CerrarCajaRequest peticion, CajaBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var ciego = SoloDe(http) is not null;
            var r = await bll.Cerrar(peticion, UsuarioActual(http), ciego, ct);

            if (!r.Ok)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);

            // Arqueo ciego: el vendedor no sabe si sobró o faltó. Ese número
            // incluye lo que cobraron los demás; lo revisa un administrador.
            if (ciego)
                return CustomUtilz.CreateResponse(HttpStatusCodes.Ok,
                    "Caja cerrada · el arqueo lo revisa un administrador", r.Datos);

            var d = r.Datos!.Diferencia ?? 0;
            var mensaje = d switch
            {
                0 => "Caja cerrada · cuadró exacto",
                > 0 => $"Caja cerrada · sobran ${d:N0}",
                < 0 => $"Caja cerrada · faltan ${-d:N0}"
            };

            return CustomUtilz.CreateResponse(HttpStatusCodes.Ok, mensaje, r.Datos);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cerrar la caja");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al cerrar la caja: {ex.Message}", null);
        }
    }
}
