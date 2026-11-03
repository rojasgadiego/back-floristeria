using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de DESPACHOS.
///
/// A diferencia de /api/ventas, un vendedor ve los de todos: el ramo lo puede
/// vender uno y entregarlo otro. Por eso la respuesta no lleva montos.
/// </summary>
public class DespachosEndpoints : EndpointsBase
{
    public DespachosEndpoints(ILogger<DespachosEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var despachos = app.MapGroup("/api/despachos")
            .WithTags("Despachos")
            .RequireAuthorization(Politicas.Vender)
            .AddEndpointFilter<RespuestaFilter>();

        despachos.MapGet("/", Listar)
            .WithName("ListarDespachos")
            .WithSummary("Los de ese día (hoy por defecto) y, en hoy, los atrasados")
            .Produces<ResponseDto>(200);

        despachos.MapPatch("/{ventaId:int}/estado", CambiarEstado)
            .WithName("CambiarEstadoDespacho")
            .WithSummary("Pendiente, en camino o entregado. Permite volver atrás.")
            .Produces<ResponseDto>(200).Produces(400);
    }

    public async Task<ResponseDto> Listar(
        DateOnly? fecha, VentasBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Despachos(fecha, ct), "despachos");

    public async Task<ResponseDto> CambiarEstado(
        int ventaId, [FromBody] CambiarEstadoDespachoRequest peticion, VentasBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            var error = await bll.CambiarEstadoDespacho(
                ventaId, peticion?.Estado ?? "", UsuarioActual(http), ct);

            return string.IsNullOrEmpty(error)
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Despacho actualizado.", null)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, error, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cambiar el estado del despacho {Id}", ventaId);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al actualizar el despacho.", null);
        }
    }
}
