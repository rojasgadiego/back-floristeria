using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Usuarios.Dtos;

public class UsuarioDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;
    public bool Activo { get; set; }
    public DateTimeOffset? UltimoAcceso { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>Boletas emitidas por esta cuenta, para el panel de equipo.</summary>
    public int Boletas { get; set; }
    public long Vendido { get; set; }
}

public class CrearUsuarioRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, MinimumLength = 2, ErrorMessage = "El nombre debe tener entre 2 y 120 caracteres.")]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    public string Password { get; set; } = string.Empty;

    /// <summary>admin, vendedor o bodega.</summary>
    [Required(ErrorMessage = "El rol es obligatorio.")]
    public string Rol { get; set; } = "vendedor";
}

public class ActualizarUsuarioRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El correo es obligatorio.")]
    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    public string Email { get; set; } = string.Empty;
}

public class CambiarRolRequest
{
    [Required(ErrorMessage = "El rol es obligatorio.")]
    public string Rol { get; set; } = string.Empty;
}

public class RestablecerPasswordRequest
{
    [Required(ErrorMessage = "La contraseña es obligatoria.")]
    [MinLength(8, ErrorMessage = "La contraseña debe tener al menos 8 caracteres.")]
    public string Password { get; set; } = string.Empty;
}

public class UsuarioFiltro : ParametrosPagina
{
    /// <summary>admin, vendedor o bodega.</summary>
    public string? Rol { get; set; }

    /// <summary>Null trae todas; true solo activas; false solo bloqueadas.</summary>
    public bool? Activo { get; set; }
}
