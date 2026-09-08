// Endpoints/RespuestaFilter.cs
namespace Colibri.Api.Endpoints;

/// <summary>
/// Los handlers devuelven ResponseDto, que minimal API serializaría como 200
/// siempre — el status quedaba solo dentro del JSON. Este filtro lo saca al
/// nivel HTTP, que es donde los clientes lo leen.
/// </summary>
public sealed class RespuestaFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext ctx, EndpointFilterDelegate next)
    {
        var resultado = await next(ctx);

        return resultado is Dto.ResponseDto dto
            ? Results.Json(dto, statusCode: dto.StatusCode)
            : resultado;
    }
}