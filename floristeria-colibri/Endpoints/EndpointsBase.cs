using Colibri.Api.Dto;
using Colibri.Api.Utils;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Lo que todos los módulos repiten. Vive una vez, no copiado en cada clase.
/// </summary>
public abstract class EndpointsBase
{
    protected readonly ILogger Logger;

    protected EndpointsBase(ILogger logger) => Logger = logger;

    /// <summary>
    /// Listados: 200 con lista vacía en lugar de 204. El cliente necesita
    /// distinguir "sin datos" de un error, y un 204 obliga a adivinar.
    /// </summary>
    protected async Task<ResponseDto> Consultar<T>(Func<Task<T>> consulta, string queCosa)
    {
        try
        {
            var datos = await consulta();
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok, $"Listado de {queCosa} obtenido correctamente.", datos);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al obtener {QueCosa}", queCosa);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al obtener {queCosa}: {ex.Message}", null);
        }
    }

    /// <summary>Una fila: null es 404, no error.</summary>
    protected async Task<ResponseDto> ConsultarUno<T>(
        Func<Task<T?>> consulta, string queCosa, string noEncontrado)
    {
        try
        {
            var dato = await consulta();
            return dato is null
                ? CustomUtilz.CreateResponse(HttpStatusCodes.NotFound, noEncontrado, null)
                : CustomUtilz.CreateResponse(HttpStatusCodes.Ok, $"{queCosa} obtenido correctamente.", dato);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al obtener {QueCosa}", queCosa);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al obtener {queCosa}: {ex.Message}", null);
        }
    }

    /// <summary>
    /// El id del token manda sobre cualquier id que venga en el body: si no,
    /// cualquiera puede firmar un movimiento con el usuario de otro.
    /// </summary>
    protected static int UsuarioActual(HttpContext http, int? delBody = null)
    {
        var claim = http.User?.FindFirst("sub")?.Value
                    ?? http.User?.FindFirst("id")?.Value;

        return int.TryParse(claim, out var id) ? id : (delBody ?? 0);
    }
}
