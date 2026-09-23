using System.Text.Json.Serialization;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una cotización con su plata ya calculada (vw_cot_resumen). Es lo que
/// lista la grilla, "a quiénes llamar" y la agenda: todos leen lo mismo.
///
/// El estado viaja como texto —borrador, aprobada, cobrada, anulada— para
/// no depender del mapeo de enums de Npgsql, que el resto del proyecto
/// tampoco usa.
/// </summary>
public class CotizacionResumen
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;

    public int? ClienteId { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;

    public string TipoEvento { get; set; } = string.Empty;
    public DateOnly? FechaEvento { get; set; }
    public string? Contacto { get; set; }

    public int Traslado { get; set; }
    public int Montaje { get; set; }
    public int Total { get; set; }

    /// <summary>Suma de los abonos no anulados.</summary>
    public int Abono { get; set; }
    public int Saldo { get; set; }
    public decimal PorcentajePagado { get; set; }

    public string Estado { get; set; } = "borrador";
    public string? Notas { get; set; }

    public int? CreadaPor { get; set; }
    public string? CreadaPorNombre { get; set; }

    public int? VentaId { get; set; }
    public DateTime? CobradaEn { get; set; }
    public DateTime CreadoEn { get; set; }
    public DateTime ActualizadoEn { get; set; }

    public long Lineas { get; set; }
    public int? DiasParaEvento { get; set; }

    /// <summary>Lo que ya debería estar pagado a hoy (cuotas vencidas o el evento).</summary>
    public int ExigibleHoy { get; set; }

    /// <summary>Lo exigible hasta ayer que sigue sin pagarse.</summary>
    public int Vencido { get; set; }
    public DateOnly? VencidoDesde { get; set; }

    /// <summary>Redactado en la base: "al día", "debe $100.000 desde el 15-08"…</summary>
    public string EstadoPago { get; set; } = string.Empty;

    public string? MotivoAnulacion { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>La ficha completa: el resumen más todo lo que cuelga de él.</summary>
public class CotizacionDetalle : CotizacionResumen
{
    public List<CotizacionItem> Items { get; set; } = [];
    public List<CotizacionFaltante> Faltantes { get; set; } = [];
    public List<CotizacionCuota> PlanCuotas { get; set; } = [];
    public List<CotizacionPago> HistorialPagos { get; set; } = [];

    /// <summary>Solo cuando está cobrada.</summary>
    public CotizacionResultado? Resultado { get; set; }
}

public class CotizacionItem
{
    public int Id { get; set; }
    public int? ProductoId { get; set; }
    public string? Emoji { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Precio { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }
    public bool AMedida { get; set; }

    /// <summary>Lo que hay hoy entre bodega y mesón. Null en lo hecho a medida.</summary>
    public int? Disponible { get; set; }
}

public class CotizacionFaltante
{
    public int ProductoId { get; set; }
    public string? Emoji { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Comprometido { get; set; }
    public int Disponible { get; set; }
    public int Faltante { get; set; }
}

public class CotizacionCuota
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int Monto { get; set; }
    public DateOnly Vence { get; set; }
    public string? Notas { get; set; }

    /// <summary>Lo abonado alcanza su acumulado. No se marca a mano.</summary>
    public bool Cubierta { get; set; }
    public int DiasParaVencer { get; set; }
}

public class CotizacionPago
{
    public int Id { get; set; }
    public int Monto { get; set; }
    public string MedioPago { get; set; } = string.Empty;
    public DateTime Fecha { get; set; }
    public int? VentaId { get; set; }
    public string? VentaFolio { get; set; }
    public string? Usuario { get; set; }
    public string? Notas { get; set; }
    public bool Anulado { get; set; }
    public DateTime? AnuladoEn { get; set; }
    public string? MotivoAnulacion { get; set; }
}

public class CotizacionResultado
{
    public int Cobrado { get; set; }
    public long Boletas { get; set; }
    public int CostoFlor { get; set; }
    public int Margen { get; set; }
    public decimal MargenPorcentaje { get; set; }
}

public class LineaCobro
{
    public int? ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public int Precio { get; set; }
    public bool EsServicio { get; set; }
    public int? Disponible { get; set; }
}

/// <summary>
/// La boleta final sugerida. NO cobra: el cobro ocurre en el punto de
/// venta, donde se ajusta a la flor que realmente salió.
/// </summary>
public class PreparacionCobro
{
    public int CotizacionId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public List<LineaCobro> Lineas { get; set; } = [];
    public int TotalCotizado { get; set; }
    public int AbonoPrevio { get; set; }
    public int SaldoACobrar { get; set; }
    public List<string> Advertencias { get; set; } = [];
}

public class ResultadoPagoCotizacion
{
    public int PagoId { get; set; }
    public int VentaId { get; set; }
    public string VentaFolio { get; set; } = string.Empty;
    public int Monto { get; set; }
    public int? Vuelto { get; set; }
    public int Saldo { get; set; }
}

public class ResultadoAnulacionPago
{
    public int PagoId { get; set; }
    public string? VentaFolio { get; set; }
    public int Monto { get; set; }
}
