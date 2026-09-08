using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>Una etiqueta lista para imprimir. Sale de sp_abs_c_lotes_compra.</summary>
public class LoteEtiqueta
{
    public int Id { get; set; }

    /// <summary>El código legible: LOT-000009. Es lo que se tipea si el QR falla.</summary>
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Lo que va DENTRO del QR, ya armado por el SP.</summary>
    public string Qr { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Presentacion { get; set; }
    public string? Proveedor { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }

    /// <summary>Negativo si ya venció. Null si el producto no tiene días de vida.</summary>
    public int? DiasRestantes { get; set; }

    public int VarasIniciales { get; set; }
    public int VarasDisponibles { get; set; }
    public decimal CostoPorVara { get; set; }
    public int? PrecioUnitario { get; set; }
    public EstadoLote Estado { get; set; }
}

/// <summary>
/// Lo que responde el escáner. Sale de sp_abs_c_lote_qr.
///
/// Se relee de la base a propósito: los datos impresos pueden tener semanas,
/// y el lote pudo agotarse o vencerse desde entonces.
/// </summary>
public class LoteEscaneado
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int Precio { get; set; }
    public decimal? PrecioRamo { get; set; }
    public decimal? PrecioLiquidacion { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasRestantes { get; set; }
    public bool Vencido { get; set; }

    public int VarasDisponibles { get; set; }
    public decimal CostoPorVara { get; set; }
    public EstadoLote Estado { get; set; }

    /// <summary>Todo lo que el punto de venta necesita saber, en un booleano.</summary>
    public bool Vendible { get; set; }

    /// <summary>
    /// Por qué no, cuando la respuesta es no. Un "no se puede" sin razón deja
    /// al vendedor con el cliente enfrente y sin qué decirle.
    /// </summary>
    public string? MotivoNoVendible { get; set; }
}