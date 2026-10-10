namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una fila de la planilla para contar (sp_inv_c_conteo). TipoFila: producto
/// (sin lotes) · balde · sin_balde (flor sin ningún balde) · partida
/// (mostrador) · armado (ramo listo en la vitrina).
/// </summary>
public class FilaConteo
{
    public string TipoFila { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int CategoriaId { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public int CategoriaOrden { get; set; }
    public bool ControlaLotes { get; set; }
    public int? LoteId { get; set; }
    public int? PartidaId { get; set; }

    /// <summary>El código del balde o la partida, o "sin balde".</summary>
    public string? Origen { get; set; }

    public DateOnly? FechaVencimiento { get; set; }

    /// <summary>Lo que dice el sistema al momento de pedir la planilla.</summary>
    public int Sistema { get; set; }

    /// <summary>Para valorizar la diferencia en pantalla.</summary>
    public int CostoUnitario { get; set; }
}

/// <summary>Un conteo del historial (sp_inv_c_conteos).</summary>
public class ConteoResumen
{
    public int Id { get; set; }
    public string Ubicacion { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? Usuario { get; set; }
    public string? AutorizadoPor { get; set; }
    public DateTime CreadoEn { get; set; }
    public int Lineas { get; set; }
    public int ConDiferencia { get; set; }
    public int Sobrantes { get; set; }
    public int Faltantes { get; set; }
    public int ValorSobrante { get; set; }
    public int ValorFaltante { get; set; }
}

/// <summary>Una línea de un conteo del historial (sp_inv_c_conteo_lineas).</summary>
public class ConteoLinea
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public string? Origen { get; set; }
    public int Sistema { get; set; }
    public int Contado { get; set; }
    public int Diferencia { get; set; }
    public int CostoUnitario { get; set; }

    /// <summary>Si faltaba: la merma "Faltante en conteo" que se registró.</summary>
    public int? MermaId { get; set; }
}

/// <summary>Un conteo con su detalle.</summary>
public class ConteoDetalle
{
    public ConteoResumen Conteo { get; set; } = new();
    public List<ConteoLinea> Lineas { get; set; } = [];
}
