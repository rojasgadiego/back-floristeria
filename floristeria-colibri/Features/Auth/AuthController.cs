using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

using Colibri.Api.Common;
using Colibri.Api.Features.Auth.Dtos;
using Colibri.Api.Startup;

namespace Colibri.Api.Features.Auth;

public class AuthController : ControladorBase
{
    private readonly IAuthService _auth;

    public AuthController(IAuthService auth) => _auth = auth;

    /// <summary>
    /// Inicia sesión y devuelve el token.
    /// </summary>
    /// <remarks>
    /// Limitado a 8 intentos por minuto y por IP: sin ese tope, probar
    /// contraseñas por fuerza bruta no le costaría nada al atacante.
    ///
    /// Cuentas de prueba: admin@colibri.cl / admin123
    /// </remarks>
    [HttpPost("login")]
    [AllowAnonymous]
    [EnableRateLimiting(ServiciosExtensions.LimiteLogin)]
    [ProducesResponseType(typeof(ApiResponse<LoginResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Login(
        [FromBody] LoginRequest peticion, CancellationToken ct)
    {
        var sesion = await _auth.LoginAsync(peticion, ct);
        return Exito(sesion, $"Hola, {sesion.Usuario.Nombre.Split(' ')[0]}");
    }

    /// <summary>
    /// Datos de la sesión activa.
    /// </summary>
    /// <remarks>
    /// El front lo llama al arrancar para restaurar la sesión guardada.
    /// Es también donde se detecta que la cuenta fue bloqueada después de
    /// emitido el token.
    /// </remarks>
    [HttpGet("me")]
    [ProducesResponseType(typeof(ApiResponse<SesionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Yo(CancellationToken ct)
        => Exito(await _auth.SesionActualAsync(ct));

    /// <summary>
    /// Cambia la contraseña propia. Exige la actual.
    /// </summary>
    [HttpPost("cambiar-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CambiarPassword(
        [FromBody] CambiarPasswordRequest peticion, CancellationToken ct)
    {
        await _auth.CambiarPasswordPropiaAsync(peticion, ct);
        return SinContenido();
    }
}
