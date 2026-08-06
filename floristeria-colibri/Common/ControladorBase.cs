using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Common;

/// <summary>
/// Base de todos los controladores: fija el prefijo /api, exige autenticación
/// por defecto y ofrece los envoltorios de respuesta.
///
/// Autenticado por defecto y abierto por excepción, no al revés: olvidar un
/// [Authorize] deja un endpoint público sin que nadie lo note; olvidar un
/// [AllowAnonymous] se descubre al primer intento de uso.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Authorize]
[Produces("application/json")]
public abstract class ControladorBase : ControllerBase
{
    protected IActionResult Exito<T>(T datos, string? mensaje = null) =>
        Ok(ApiResponse<T>.Ok(datos, mensaje));

    protected IActionResult Exito<T>(ResultadoPagina<T> pagina) =>
        Ok(ApiResponse<ResultadoPagina<T>>.Ok(pagina));

    protected IActionResult Creado<T>(T datos, string? mensaje = null) =>
        StatusCode(StatusCodes.Status201Created, ApiResponse<T>.Ok(datos, mensaje));

    protected IActionResult SinContenido() => NoContent();
}
