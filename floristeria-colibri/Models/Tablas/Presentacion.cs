using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Cómo viene el producto del proveedor.
///
///   "Paquete 25 varas"  → paquetes 1, varasPorPaquete 25  →  25 varas
///   "Caja 4x25"         → paquetes 4, varasPorPaquete 25  → 100 varas
///
/// `Paquetes` es la clave del QR: cada paquete es un balde con su propia
/// etiqueta. Comprar 3 cajas de 4x25 genera DOCE lotes de 25 varas.
/// </summary>
public class Presentacion
{
    public int Id { get; set; }

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public string Nombre { get; set; } = string.Empty;
    public TipoPresentacion Tipo { get; set; }

    public int Paquetes { get; set; }
    public int VarasPorPaquete { get; set; }

    /// <summary>Paquetes × varasPorPaquete. Lo calcula la base.</summary>
    public int VarasTotales { get; set; }

    /// <summary>La que el formulario de compra elige sola. Una por producto.</summary>
    public bool Predeterminada { get; set; }

    public bool Activa { get; set; }
}
