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
}

public static class Roles
{
    public const string Admin = "admin";
    public const string Vendedor = "vendedor";
    public const string Bodega = "bodega";
}
