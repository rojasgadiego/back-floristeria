namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Bajar un lote al mesón.
///
/// `Lote` acepta el código, el QR completo o el id como texto: el escáner
/// manda una cosa y el vendedor tipea otra.
/// </summary>
public class TraspasoLoteRequest
{
    public string Lote { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string? Notas { get; set; }
}

/// <summary>Para lo que no controla lotes: jarrones, cintas, tarjetas.</summary>
public class TraspasoSimpleRequest
{
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }
    public string? Notas { get; set; }
}

public class RetornoPartidaRequest
{
    public string Partida { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string? Notas { get; set; }
}

public class PartidaFiltro
{
    public string? Buscar { get; set; }
    public int? ProductoId { get; set; }

    /// <summary>Incluye las agotadas. Por defecto solo lo que hay ahora.</summary>
    public bool? Historial { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 50;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;
        TamanoReal = Tamano switch { null or < 1 => 50, > 200 => 200, _ => Tamano.Value };
        Buscar = string.IsNullOrWhiteSpace(Buscar) ? null : Buscar.Trim();
    }
}
