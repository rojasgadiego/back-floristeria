namespace Colibri.Api.Endpoints;

/// <summary>
/// Nombres de las políticas. Constantes y no strings sueltos: un typo en
/// "Inventario" no falla al compilar, deja el endpoint abierto o cerrado a
/// todos y te enteras en producción.
/// </summary>
public static class Politicas
{
    /// <summary>Consultar. Cualquier rol autenticado.</summary>
    public const string VerInventario = "VerInventario";

    /// <summary>Modificar el catálogo. Admin y bodega.</summary>
    public const string Inventario = "Inventario";

    /// <summary>Borrado definitivo y configuración. Solo admin.</summary>
    public const string Admin = "Admin";
        /// <summary>
    /// Operar el punto de venta: abrir caja, cobrar, ver el mesón.
    /// Admin y vendedor. Bodega no vende, así que no toca la caja.
    /// </summary>
    public const string Vender = "Vender";
}
