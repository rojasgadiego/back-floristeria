namespace Colibri.Api.Common.Seguridad;

/// <summary>
/// Nombres de las políticas de autorización, en constantes.
/// Con cadenas sueltas en cada [Authorize], un typo no falla al compilar:
/// falla en producción dejando entrar a quien no debe.
/// </summary>
public static class Politicas
{
    /// <summary>Solo administración: precios, anulaciones, cierre de caja, equipo.</summary>
    public const string Admin = "Admin";

    /// <summary>Quien atiende el mesón: vender, cotizar, clientes.</summary>
    public const string Caja = "Caja";

    /// <summary>Quien mueve stock: recibir compras, armar, mermar.</summary>
    public const string Inventario = "Inventario";

    /// <summary>Consultar el inventario sin modificarlo.</summary>
    public const string VerInventario = "VerInventario";

    /// <summary>
    /// Bajar mercadería al mostrador y devolverla a bodega. Es exclusivo de
    /// administración porque define cuánto puede vender el equipo en el día: si
    /// el vendedor pudiera traspasar, el mostrador dejaría de ser un límite.
    /// </summary>
    public const string Mostrador = "Mostrador";

    /// <summary>
    /// Conteo físico. La única operación que puede hacer aparecer existencias sin
    /// causa física, así que la firma de quien la hace importa.
    /// </summary>
    public const string Conteo = "Conteo";
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Vendedor = "vendedor";
    public const string Bodega = "bodega";
}
