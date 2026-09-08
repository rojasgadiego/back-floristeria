namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Filtros de la grilla de lotes.
///
/// Los tipos de valor van nulables: con [AsParameters], un int no nulable se
/// vuelve obligatorio en el query string y el request falla con 400 si el
/// front no lo manda.
/// </summary>
public class LoteFiltro
{
    public string? Buscar { get; set; }
    public int? ProductoId { get; set; }
    public int? ProveedorId { get; set; }

    /// <summary>normal · por vencer · vencido · resto por liquidar</summary>
    public string? Alerta { get; set; }

    public bool? SoloRezagados { get; set; }

    /// <summary>
    /// Incluye agotados y descartados. Por defecto solo lo que tiene
    /// existencias: un lote agotado es historia y mezclarlo con lo que hay en
    /// cámara vuelve la lista inútil.
    /// </summary>
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
        Alerta = string.IsNullOrWhiteSpace(Alerta) ? null : Alerta.Trim();
    }
}

public class UbicacionRequest
{
    public string Ubicacion { get; set; } = string.Empty;
}

public class ValidarLoteRequest
{
    public string Codigo { get; set; } = string.Empty;
    public int Cantidad { get; set; } = 1;
}
