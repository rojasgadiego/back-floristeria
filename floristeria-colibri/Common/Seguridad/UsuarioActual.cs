using System.Security.Claims;

namespace Colibri.Api.Common.Seguridad;

public class UsuarioActual : IUsuarioActual
{
    private readonly ClaimsPrincipal? _principal;

    public UsuarioActual(IHttpContextAccessor accesor) => _principal = accesor.HttpContext?.User;

    public bool EstaAutenticado => _principal?.Identity?.IsAuthenticated == true;

    public int? Id => int.TryParse(_principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var id)
        ? id : null;

    public string? Email => _principal?.FindFirstValue(ClaimTypes.Email);
    public string? Nombre => _principal?.FindFirstValue(ClaimTypes.Name);
    public string? Rol => _principal?.FindFirstValue(ClaimTypes.Role);

    public bool EsAdmin => string.Equals(Rol, Roles.Admin, StringComparison.OrdinalIgnoreCase);

    public int IdRequerido() => Id
        ?? throw new ExcepcionNegocio("No hay sesión activa.", StatusCodes.Status401Unauthorized);
}
