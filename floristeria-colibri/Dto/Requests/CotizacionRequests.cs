namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Filtros de la grilla. Los tipos de valor van nulables: con [AsParameters]
/// uno no nulable se vuelve obligatorio en el query string.
/// </summary>
public class CotizacionFiltro
{
    public string? Buscar { get; set; }
    public string? Estado { get; set; }
    public int? ClienteId { get; set; }
    public bool? SoloVencidas { get; set; }
    public int? ProximosDias { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    public int? Pagina { get; set; }

    // El front manda porPagina; se acepta también tamano como el resto.
    public int? PorPagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal => Pagina is null or < 1 ? 1 : Pagina.Value;

    public int TamanoReal => (PorPagina ?? Tamano) switch
    {
        null or < 1 => 30,
        > 100 => 100,
        var t => t.Value
    };
}

public class CotizacionRequest
{
    public int? ClienteId { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public DateOnly? FechaEvento { get; set; }
    public string? Contacto { get; set; }
    public int Traslado { get; set; }
    public int Montaje { get; set; }
    public string? Notas { get; set; }
    public List<CotizacionLineaRequest> Items { get; set; } = [];
}

/// <summary>
/// Del catálogo solo viajan producto y cantidad: el precio lo pone la base.
/// Lo hecho a medida trae nombre y precio propios.
/// </summary>
public class CotizacionLineaRequest
{
    public int? ProductoId { get; set; }
    public string? Nombre { get; set; }
    public int? Precio { get; set; }
    public int Cantidad { get; set; }
    public bool AMedida { get; set; }
}

public class MotivoRequest
{
    public string Motivo { get; set; } = string.Empty;
}

public class PagoCotizacionRequest
{
    public int Monto { get; set; }
    public string MedioPago { get; set; } = "efectivo";

    /// <summary>Solo en efectivo: con esto se calcula el vuelto.</summary>
    public int? Recibido { get; set; }
    public string? Notas { get; set; }
}

public class CuotasRequest
{
    public List<CuotaRequest> Cuotas { get; set; } = [];
}

public class CuotaRequest
{
    public int Monto { get; set; }
    public DateOnly Vence { get; set; }
    public string? Notas { get; set; }
}

public class GenerarCuotasRequest
{
    public int Cantidad { get; set; } = 3;
    public DateOnly? PrimerVencimiento { get; set; }
    public int CadaDias { get; set; } = 30;
}
