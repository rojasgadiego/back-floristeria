using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Lo que sale hacia el cliente. NO tiene password_hash y no debe tenerlo
/// nunca: si el hash entra a esta clase, tarde o temprano un endpoint lo
/// serializa sin que nadie se dé cuenta.
/// </summary>
public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; }
    public DateTime? UltimoAcceso { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }
    public string[] Permisos { get; set; } = [];
}

/// <summary>
/// Solo para el login. `internal` a propósito: vive dentro del ensamblado y no
/// puede escaparse a una respuesta HTTP por accidente.
///
/// Se llena únicamente desde sp_acc_c_usuario_login.
/// </summary>
internal class UsuarioAuth
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }
    public bool Activo { get; set; }
}
