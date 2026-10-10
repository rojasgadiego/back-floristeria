namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una fila de la planilla para contar (sp_inv_c_conteo). Sin lotes: una por
/// producto. Con lotes: una por balde activo.
/// </summary>
public class FilaConteo
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public bool ControlaLotes { get; set; }
    public int? LoteId { get; set; }
    public string? LoteCodigo { get; set; }
    public DateOnly? FechaVencimiento { get; set; }

    /// <summary>Lo que dice el sistema al momento de pedir la planilla.</summary>
    public int Sistema { get; set; }

    /// <summary>Para valorizar la diferencia en pantalla. En flores, el del balde.</summary>
    public int CostoUnitario { get; set; }
}

/// <summary>
/// Lo que pasó con cada línea al aplicar el conteo (sp_inv_i_conteo). La
/// diferencia se calcula contra el stock del momento de aplicar, no contra
/// el de la planilla.
/// </summary>
public class LineaConteoAplicada
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? LoteCodigo { get; set; }
    public int Sistema { get; set; }
    public int Contado { get; set; }
    public int Diferencia { get; set; }

    /// <summary>Si faltaba: la merma "Faltante en conteo" que se registró.</summary>
    public int? MermaId { get; set; }
}
