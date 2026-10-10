namespace Colibri.Api.Dto.Requests;

/// <summary>Un conteo de bodega: lo que se contó, línea por línea.</summary>
public class ConteoRequest
{
    /// <summary>Nota del conteo ("Conteo mensual", "Inventario inicial"). Va en cada movimiento.</summary>
    public string? Detalle { get; set; }

    public List<ConteoLineaRequest> Items { get; set; } = [];
}

public class ConteoLineaRequest
{
    public int ProductoId { get; set; }

    /// <summary>El balde, si el producto se maneja por lotes. Null si no.</summary>
    public int? LoteId { get; set; }

    /// <summary>Lo que hay físicamente. La base calcula la diferencia.</summary>
    public int Contado { get; set; }
}
