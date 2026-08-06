namespace Colibri.Api.Common.Seguridad;

/// <summary>
/// Quién está haciendo la petición. Casi toda operación del sistema deja
/// rastro —movimientos de stock, anulaciones, cierres de caja— y necesita
/// saberlo. Se resuelve desde los claims del token, no consultando la base.
/// </summary>
public interface IUsuarioActual
{
    int? Id { get; }
    string? Email { get; }
    string? Nombre { get; }
    string? Rol { get; }
    bool EstaAutenticado { get; }
    bool EsAdmin { get; }

    /// <summary>
    /// Id del usuario, o excepción si no hay sesión. Para las operaciones
    /// que no tienen sentido sin responsable identificado.
    /// </summary>
    int IdRequerido();
}
