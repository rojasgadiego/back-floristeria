using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

/// <summary>
/// Cambio de contraseña propio. Exige la actual: sin eso, un token robado o una
/// sesión abierta en un computador prestado alcanza para secuestrar la cuenta.
/// </summary>
public class CambiarPasswordRequest
{
    public string PasswordActual { get; set; } = string.Empty;
    public string PasswordNueva { get; set; } = string.Empty;
}

/// <summary>Reseteo por administrador. No pide la anterior, por eso es solo Admin.</summary>
public class ResetPasswordRequest
{
    public string PasswordNueva { get; set; } = string.Empty;
}

public class CrearUsuarioRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
}

public class ActualizarUsuarioRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
}
