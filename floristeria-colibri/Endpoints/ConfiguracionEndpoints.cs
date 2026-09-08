using System.Text.Json;
using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de CONFIGURACIÓN.
///
/// El GET es para cualquiera autenticado: el punto de venta necesita los
/// datos del local para el ticket y el valor del punto para mostrar el saldo
/// del cliente en pesos. Los PUT son solo de administración.
/// </summary>
public class ConfiguracionEndpoints : EndpointsBase
{
    public ConfiguracionEndpoints(ILogger<ConfiguracionEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var cfg = app.MapGroup("/api/configuracion")
            .WithTags("Configuración")
            .RequireAuthorization()
            .AddEndpointFilter<RespuestaFilter>();

        cfg.MapGet("/", Obtener)
            .WithName("ObtenerConfiguracion")
            .WithSummary("Las cuatro secciones juntas: local, ticket, venta y club")
            .Produces<ResponseDto>(200);

        // Literal antes que cualquier ruta con parámetro.
        cfg.MapGet("/club/impacto", Impacto)
            .WithName("ImpactoClub")
            .WithSummary("Qué mueve cambiar el valor del punto, antes de aplicarlo")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200);

        cfg.MapPut("/local", (JsonElement body, ConfiguracionBLL bll, HttpContext http, CancellationToken ct)
                => Guardar("local", body, bll, http, ct))
            .WithName("GuardarLocal")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cfg.MapPut("/ticket", (JsonElement body, ConfiguracionBLL bll, HttpContext http, CancellationToken ct)
                => Guardar("ticket", body, bll, http, ct))
            .WithName("GuardarTicket")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cfg.MapPut("/venta", (JsonElement body, ConfiguracionBLL bll, HttpContext http, CancellationToken ct)
                => Guardar("venta", body, bll, http, ct))
            .WithName("GuardarVenta")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cfg.MapPut("/club", GuardarClub)
            .WithName("GuardarClub")
            .WithSummary("Cambiar el valor del punto revalúa los saldos vigentes: pide confirmación")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);
    }

    public async Task<ResponseDto> Obtener(ConfiguracionBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Obtener(ct),
            "Configuración", "No hay configuración cargada.");

    public async Task<ResponseDto> Impacto(
        [FromQuery] int valorPunto, ConfiguracionBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Impacto(valorPunto, ct),
            "Impacto", "No se pudo calcular el impacto.");

    private async Task<ResponseDto> Guardar(
        string seccion, JsonElement body, ConfiguracionBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            var r = await bll.Guardar(seccion, body, UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Configuración guardada", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al guardar la sección {Seccion}", seccion);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al guardar: {ex.Message}", null);
        }
    }

    /// <summary>
    /// El 400 con confirmación pendiente NO es un error: es una pregunta. El
    /// front lo traduce a un diálogo con el impacto a la vista, y reenvía con
    /// confirmaRevaluacion en true si la persona acepta.
    /// </summary>
    public async Task<ResponseDto> GuardarClub(
        JsonElement body, ConfiguracionBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var confirma = body.TryGetProperty("confirmaRevaluacion", out var c)
                           && c.ValueKind == JsonValueKind.True;

            var (r, impacto) = await bll.GuardarClub(body, confirma, UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Club actualizado", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, impacto);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al guardar el club");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al guardar: {ex.Message}", null);
        }
    }
}
