namespace Colibri.Api.Domain.Entities;

public class Usuario
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public RolUsuario Rol { get; set; } = RolUsuario.vendedor;
    public bool Activo { get; set; } = true;
    public DateTimeOffset? UltimoAcceso { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();
    public ICollection<Merma> Mermas { get; set; } = new List<Merma>();
}

public class Configuracion
{
    /// <summary>Sección: local, ticket, venta o club.</summary>
    public string Clave { get; set; } = null!;

    /// <summary>Contenido en JSONB. Agregar un ajuste no obliga a migrar la tabla.</summary>
    public string Valor { get; set; } = "{}";

    public DateTimeOffset ActualizadoEn { get; set; }
    public int? ActualizadoPor { get; set; }
    public Usuario? UsuarioActualizo { get; set; }
}
