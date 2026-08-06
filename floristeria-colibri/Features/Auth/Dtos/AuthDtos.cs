using System.ComponentModel.DataAnnotations;

namespace Colibri.Api.Features.Auth.Dtos;

public class LoginRequest
{
    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;

    /// <summary>Cuándo deja de servir el token. El front puede avisar antes.</summary>
    public DateTimeOffset ExpiraEn { get; set; }

    public SesionDto Usuario { get; set; } = null!;
}

/// <summary>
/// Datos de la sesión. Nunca incluye el hash de la contraseña: lo que no se
/// serializa no se puede filtrar por error en un log o en una respuesta.
/// </summary>
public class SesionDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;

    /// <summary>
    /// Módulos que este rol puede ver. El front los usa para armar el menú;
    /// la barrera real son las políticas de cada endpoint.
    /// </summary>
    public IReadOnlyList<string> Permisos { get; set; } = Array.Empty<string>();
}

public class CambiarPasswordRequest
{
    [Required(ErrorMessage = "Indica tu contraseña actual.")]
    public string PasswordActual { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica la contraseña nueva.")]
    [MinLength(8, ErrorMessage = "La contraseña nueva debe tener al menos 8 caracteres.")]
    public string PasswordNueva { get; set; } = string.Empty;
}
