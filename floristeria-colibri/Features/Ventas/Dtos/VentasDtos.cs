using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Ventas.Dtos;

/* ===================== Escritura ===================== */

public class LineaVentaRequest
{
    /// <summary>Nulo solo en servicios (despacho, traslado).</summary>
    public int? ProductoId { get; set; }

    [Range(1, 100000, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; } = 1;

    /// <summary>
    /// Lote escaneado con el QR. Ese lote se consume primero; el resto se
    /// completa por antigüedad.
    /// </summary>
    public int? LoteId { get; set; }

    /// <summary>
    /// Lotes de flor recuperada autorizados para esta línea.
    ///
    /// Aplica a los ingredientes cuando el ramo se arma al momento: esos
    /// lotes están fuera del reparto automático y usarlos es una decisión.
    /// </summary>
    public List<int> LotesAutorizados { get; set; } = new();

    /// <summary>Solo para servicios: no tocan inventario.</summary>
    public bool EsServicio { get; set; }

    [StringLength(160)]
    public string? Nombre { get; set; }

    [Range(0, int.MaxValue)]
    public int? Precio { get; set; }
}

/// <summary>
/// Credenciales de quien autoriza un descuento sobre el umbral.
/// Se verifican contra la base: no basta con que la pantalla diga que alguien
/// autorizó.
/// </summary>
public class AutorizacionRequest
{
    [Required(ErrorMessage = "Indica el correo de quien autoriza.")]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Indica la contraseña de quien autoriza.")]
    public string Password { get; set; } = string.Empty;
}

public class RegistrarVentaRequest
{
    public int? ClienteId { get; set; }

    /// <summary>
    /// Promoción a aplicar. El servidor verifica su vigencia y RECALCULA el
    /// descuento: nunca acepta un monto enviado por el cliente.
    /// </summary>
    public int? PromocionId { get; set; }

    public int? CotizacionId { get; set; }

    [MinLength(1, ErrorMessage = "La venta necesita al menos un producto.")]
    public List<LineaVentaRequest> Items { get; set; } = new();

    /// <summary>efectivo, debito, credito o transferencia.</summary>
    [Required(ErrorMessage = "El medio de pago es obligatorio.")]
    public string MedioPago { get; set; } = "efectivo";

    /// <summary>Con cuánto paga. Solo se usa en efectivo, para el vuelto.</summary>
    [Range(0, int.MaxValue)]
    public int? Recibido { get; set; }

    /// <summary>Rebaja a mano. Sobre el umbral exige autorización.</summary>
    [Range(0, int.MaxValue)]
    public int DescuentoManual { get; set; }

    [StringLength(200)]
    public string? MotivoDescuento { get; set; }

    /// <summary>Puntos del cliente a canjear. Se validan contra su saldo real.</summary>
    [Range(0, int.MaxValue)]
    public int PuntosACanjear { get; set; }

    public AutorizacionRequest? Autorizacion { get; set; }
}

public class AnularVentaRequest
{
    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(300, MinimumLength = 5,
        ErrorMessage = "Explica el motivo con al menos 5 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}

/* ===================== Lectura ===================== */

public class VentaDto
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string NumeroAtencion { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; }

    public int CajaId { get; set; }
    public string Vendedor { get; set; } = string.Empty;
    public int? ClienteId { get; set; }
    public string? Cliente { get; set; }

    public int Bruto { get; set; }
    public int DescuentoPromo { get; set; }
    public int DescuentoManual { get; set; }
    public int DescuentoCanje { get; set; }
    public int DescuentoTotal { get; set; }

    public decimal IvaTasa { get; set; }
    public int Neto { get; set; }
    public int IvaMonto { get; set; }
    public int Total { get; set; }

    public string MedioPago { get; set; } = string.Empty;
    public int? Recibido { get; set; }
    public int? Vuelto { get; set; }

    /// <summary>Quién autorizó el descuento manual, si hubo.</summary>
    public string? AutorizadoPor { get; set; }

    public string? Promocion { get; set; }
    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }

    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public string? AnuladaPor { get; set; }
    public DateTimeOffset? AnuladaEn { get; set; }

    public int Lineas { get; set; }
}

public class VentaDetalleDto : VentaDto
{
    public IReadOnlyList<VentaItemDto> Items { get; set; } = Array.Empty<VentaItemDto>();

    /// <summary>
    /// Lo que salió del inventario y de qué lote. No es lo mismo que Items:
    /// un ramo puede haber consumido tallos de dos lotes distintos.
    /// </summary>
    public IReadOnlyList<VentaConsumoDto> Consumos { get; set; } = Array.Empty<VentaConsumoDto>();

    /// <summary>Costo real de lo vendido, con los lotes que se consumieron.</summary>
    public int CostoVendido { get; set; }

    public int UtilidadBruta { get; set; }
}

public class VentaItemDto
{
    public int Id { get; set; }
    public int? ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }
    public bool EsServicio { get; set; }
}

public class VentaConsumoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;

    /// <summary>listo (unidad ya armada) o simple (tallo).</summary>
    public string Tipo { get; set; } = string.Empty;

    public string? LoteCodigo { get; set; }
    public int Cantidad { get; set; }
    public decimal? CostoUnitario { get; set; }
}

/// <summary>Todo lo que necesita la impresora del mesón.</summary>
public class TicketDto
{
    public string Folio { get; set; } = string.Empty;
    public string NumeroAtencion { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; }
    public string Vendedor { get; set; } = string.Empty;

    public string LocalNombre { get; set; } = string.Empty;
    public string? LocalRut { get; set; }
    public string? LocalDireccion { get; set; }
    public string? LocalTelefono { get; set; }
    public string? LocalInstagram { get; set; }

    public string? Cliente { get; set; }
    public IReadOnlyList<VentaItemDto> Items { get; set; } = Array.Empty<VentaItemDto>();

    public int Bruto { get; set; }
    public int DescuentoTotal { get; set; }
    public string? Promocion { get; set; }
    public int Neto { get; set; }
    public int IvaMonto { get; set; }
    public decimal IvaTasa { get; set; }
    public int Total { get; set; }

    public string MedioPago { get; set; } = string.Empty;
    public int? Recibido { get; set; }
    public int? Vuelto { get; set; }

    public bool MostrarPuntos { get; set; }
    public int PuntosGanados { get; set; }
    public int? SaldoPuntos { get; set; }

    public string Mensaje { get; set; } = string.Empty;
    public string Leyenda { get; set; } = string.Empty;
    public bool Anulada { get; set; }
}

/// <summary>Promoción que aplica a un carrito, con el descuento ya calculado.</summary>
public class PromocionAplicableDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int Valor { get; set; }
    public string Alcance { get; set; } = string.Empty;

    /// <summary>Cuánto rebajaría en este carrito concreto.</summary>
    public int Descuento { get; set; }
}

public class VentaFiltro : ParametrosPagina
{
    public int? CajaId { get; set; }
    public int? ClienteId { get; set; }
    public int? UsuarioId { get; set; }

    /// <summary>efectivo, debito, credito o transferencia.</summary>
    public string? MedioPago { get; set; }

    /// <summary>Null trae todas; false excluye las anuladas.</summary>
    public bool? Anulada { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}