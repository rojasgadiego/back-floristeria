namespace Colibri.Api.Dto.Requests;

/// <summary>Un conteo: lo que se contó en bodega o en la vitrina, línea por línea.</summary>
public class ConteoRequest
{
    /// <summary>"bodega" o "vitrina". La vendedora solo cuenta la vitrina.</summary>
    public string Ubicacion { get; set; } = "bodega";

    /// <summary>Nota del conteo ("Conteo mensual", "Inventario inicial"). Va en el historial.</summary>
    public string? Detalle { get; set; }

    public List<ConteoLineaRequest> Items { get; set; } = [];

    /// <summary>
    /// Código por correo, cuando los faltantes de quien no es admin pasan el
    /// tope: son mermas, y sin firma contar sería la forma de esconderlas.
    /// </summary>
    public AutorizacionCodigoRequest? Autorizacion { get; set; }
}

public class ConteoLineaRequest
{
    public int ProductoId { get; set; }

    /// <summary>El balde. Null en una flor sin balde (se le crea al contarla).</summary>
    public int? LoteId { get; set; }

    /// <summary>La partida del mostrador, en la vitrina.</summary>
    public int? PartidaId { get; set; }

    /// <summary>Lo que hay físicamente. La base calcula la diferencia.</summary>
    public int Contado { get; set; }
}
