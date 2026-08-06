using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Cotizaciones.Dtos;

/* ===================== Lectura ===================== */

public class CotizacionDto
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;

    public int? ClienteId { get; set; }
    public string ClienteNombre { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public DateOnly? FechaEvento { get; set; }
    public string? Contacto { get; set; }

    /// <summary>borrador, aprobada, cobrada o anulada.</summary>
    public string Estado { get; set; } = string.Empty;

    public int Traslado { get; set; }
    public int Montaje { get; set; }
    public int Total { get; set; }

    /// <summary>Suma de los pagos recibidos. La mantiene un trigger.</summary>
    public int Abono { get; set; }

    public int Saldo { get; set; }
    public decimal PorcentajePagado { get; set; }

    /// <summary>Negativo si el evento ya pasó.</summary>
    public int? DiasParaEvento { get; set; }

    /// <summary>Lo que ya debería estar pagado a esta fecha.</summary>
    public int ExigibleHoy { get; set; }

    /// <summary>Lo exigible que todavía no se abonó. Cero es estar al día.</summary>
    public int Vencido { get; set; }

    public DateOnly? ProximoVencimiento { get; set; }

    /// <summary>Texto listo para mostrar: "al día" o "debe $100.000 desde el 15-08".</summary>
    public string EstadoPago { get; set; } = string.Empty;

    public int Lineas { get; set; }
    public int Pagos { get; set; }
    public string? Notas { get; set; }
    public string? CreadaPor { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    /// <summary>Boleta final, si ya se cobró.</summary>
    public int? VentaId { get; set; }
    public string? VentaFolio { get; set; }
}

public class CotizacionDetalleDto : CotizacionDto
{
    public IReadOnlyList<LineaCotizacionDto> Items { get; set; }
        = Array.Empty<LineaCotizacionDto>();

    public IReadOnlyList<PagoDto> HistorialPagos { get; set; } = Array.Empty<PagoDto>();
    public IReadOnlyList<CuotaDto> PlanCuotas { get; set; } = Array.Empty<CuotaDto>();

    /// <summary>
    /// Flor comprometida que el stock actual no cubre. Un evento aprobado no
    /// reserva inventario —la flor de un matrimonio en tres semanas todavía
    /// no se compra— pero sí avisa lo que falta.
    /// </summary>
    public IReadOnlyList<FaltanteEventoDto> Faltantes { get; set; }
        = Array.Empty<FaltanteEventoDto>();

    /// <summary>Resultado del evento completo, cuando ya se cobró.</summary>
    public ResultadoEventoDto? Resultado { get; set; }
}

public class LineaCotizacionDto
{
    public int Id { get; set; }
    public int? ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int Precio { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }

    /// <summary>Línea sin producto de catálogo: un arco floral, por ejemplo.</summary>
    public bool AMedida { get; set; }

    /// <summary>Disponible hoy. Null en las líneas a medida.</summary>
    public int? Disponible { get; set; }
}

public class PagoDto
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public int Monto { get; set; }

    /// <summary>efectivo, debito, credito o transferencia.</summary>
    public string MedioPago { get; set; } = string.Empty;

    public int? VentaId { get; set; }
    public string? VentaFolio { get; set; }
    public string? Usuario { get; set; }
    public string? Notas { get; set; }

    public bool Anulado { get; set; }
    public string? MotivoAnulacion { get; set; }
}

public class CuotaDto
{
    public int Id { get; set; }
    public int Numero { get; set; }
    public int Monto { get; set; }
    public DateOnly Vence { get; set; }
    public string? Notas { get; set; }

    /// <summary>Negativo si ya venció.</summary>
    public int DiasParaVencer { get; set; }

    /// <summary>
    /// Cubierta por lo abonado hasta ahora. Se calcula acumulando: las
    /// cuotas no se marcan pagadas una por una, porque en un acuerdo de
    /// palabra nadie paga montos exactos.
    /// </summary>
    public bool Cubierta { get; set; }
}

public class FaltanteEventoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Comprometido { get; set; }
    public int Disponible { get; set; }
    public int Faltante { get; set; }
}

/// <summary>
/// Cómo resultó el evento. Junta las dos boletas —el abono y el cobro
/// final— porque ninguna de las dos, mirada sola, dice la verdad.
/// </summary>
public class ResultadoEventoDto
{
    public int Cobrado { get; set; }

    /// <summary>Costo real de la flor consumida, con los lotes que salieron.</summary>
    public int CostoFlor { get; set; }

    public int Margen { get; set; }
    public decimal MargenPorcentaje { get; set; }
    public int Boletas { get; set; }
}

/// <summary>
/// Líneas sugeridas para la boleta final. Es una sugerencia editable: si el
/// arco quedó chico y se usaron 72 rosas en vez de 60, se cobra y descuenta
/// lo que realmente salió.
/// </summary>
public class PreparacionCobroDto
{
    public int CotizacionId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string ClienteNombre { get; set; } = string.Empty;
    public int? ClienteId { get; set; }

    public int TotalCotizado { get; set; }

    /// <summary>Lo ya recibido. Se aplica como abono previo, no como descuento.</summary>
    public int AbonoPrevio { get; set; }

    public int SaldoACobrar { get; set; }

    public IReadOnlyList<LineaCobroDto> Lineas { get; set; } = Array.Empty<LineaCobroDto>();

    /// <summary>Avisos sobre stock insuficiente u otros puntos a revisar.</summary>
    public IReadOnlyList<string> Advertencias { get; set; } = Array.Empty<string>();
}

public class LineaCobroDto
{
    public int? ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Precio { get; set; }
    public int Cantidad { get; set; }
    public bool EsServicio { get; set; }
    public int? Disponible { get; set; }
}

/* ===================== Escritura ===================== */

public class LineaCotizacionRequest
{
    /// <summary>Nulo en las líneas a medida.</summary>
    public int? ProductoId { get; set; }

    [StringLength(160)]
    public string? Nombre { get; set; }

    /// <summary>Se ignora si hay producto: el precio sale del catálogo.</summary>
    [Range(0, int.MaxValue)]
    public int? Precio { get; set; }

    [Range(1, 100000, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; } = 1;

    public bool AMedida { get; set; }
}

public class GuardarCotizacionRequest
{
    public int? ClienteId { get; set; }

    /// <summary>Puede ser un evento sin cliente registrado en el club.</summary>
    [Required(ErrorMessage = "Indica a nombre de quién va el evento.")]
    [StringLength(160, MinimumLength = 2)]
    public string ClienteNombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "El tipo de evento es obligatorio.")]
    [StringLength(80)]
    public string TipoEvento { get; set; } = string.Empty;

    public DateOnly? FechaEvento { get; set; }

    [StringLength(160)]
    public string? Contacto { get; set; }

    [Range(0, int.MaxValue)]
    public int Traslado { get; set; }

    [Range(0, int.MaxValue)]
    public int Montaje { get; set; }

    [StringLength(2000)]
    public string? Notas { get; set; }

    [MinLength(1, ErrorMessage = "El presupuesto necesita al menos una línea.")]
    public List<LineaCotizacionRequest> Items { get; set; } = new();
}

public class RegistrarPagoRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "El monto debe ser mayor que cero.")]
    public int Monto { get; set; }

    /// <summary>efectivo, debito, credito o transferencia.</summary>
    [Required(ErrorMessage = "El medio de pago es obligatorio.")]
    public string MedioPago { get; set; } = "efectivo";

    /// <summary>Solo en efectivo, para el vuelto.</summary>
    [Range(0, int.MaxValue)]
    public int? Recibido { get; set; }

    [StringLength(400)]
    public string? Notas { get; set; }
}

public class AnularPagoRequest
{
    [Required(ErrorMessage = "Explica por qué se anula.")]
    [StringLength(300, MinimumLength = 5)]
    public string Motivo { get; set; } = string.Empty;
}

public class CuotaRequest
{
    [Range(1, int.MaxValue)]
    public int Monto { get; set; }

    [Required]
    public DateOnly Vence { get; set; }

    [StringLength(200)]
    public string? Notas { get; set; }
}

public class GuardarCuotasRequest
{
    /// <summary>
    /// El plan completo. Debe sumar exactamente el saldo pendiente: una cuota
    /// faltante significa que el último pago va a ser una sorpresa.
    /// </summary>
    public List<CuotaRequest> Cuotas { get; set; } = new();
}

public class GenerarCuotasRequest
{
    [Range(1, 60, ErrorMessage = "Indica entre 1 y 60 cuotas.")]
    public int Cantidad { get; set; } = 3;

    /// <summary>Vencimiento de la primera. Sin fecha, en 30 días.</summary>
    public DateOnly? PrimerVencimiento { get; set; }

    /// <summary>Días entre una cuota y la siguiente.</summary>
    [Range(1, 365)]
    public int CadaDias { get; set; } = 30;
}

public class AnularCotizacionRequest
{
    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(300, MinimumLength = 5)]
    public string Motivo { get; set; } = string.Empty;
}

/* ===================== Filtros ===================== */

public class CotizacionFiltro : ParametrosPagina
{
    /// <summary>borrador, aprobada, cobrada o anulada.</summary>
    public string? Estado { get; set; }

    public int? ClienteId { get; set; }

    /// <summary>Solo las que tienen saldo vencido.</summary>
    public bool SoloVencidas { get; set; }

    /// <summary>Eventos dentro de los próximos N días.</summary>
    public int? ProximosDias { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}