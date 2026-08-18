// Domain/Ubicacion.cs
namespace Colibri.Api.Domain;

/// <summary>
/// Dónde está la mercadería. Bodega es la cámara; venta es el mostrador, y es
/// lo único que un vendedor puede vender.
///
/// Los nombres calzan exactamente con el enum ubicacion_inventario de
/// PostgreSQL: se pasan como texto y la base los convierte.
/// </summary>
public enum Ubicacion
{
    bodega,
    venta
}