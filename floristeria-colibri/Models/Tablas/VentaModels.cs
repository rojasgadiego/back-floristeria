using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>Una fila del historial de boletas.</summary>
public class Venta
{
    public int Id { get; set; }

    /// <summary>Correlativo por año: B-2026-00147.</summary>
    public string Folio { get; set; } = string.Empty;

    /// <summary>
    /// Se reinicia cada día. Es lo que se grita en el mesón: "atención 14"
    /// tiene sentido, "boleta 3.847" no.
    /// </summary>
    public int NumeroAtencion { get; set; }

    public int CajaId { get; set; }
    public int UsuarioId { get; set; }
    public string? Usuario { get; set; }

    public int? ClienteId { get; set; }
    public string? Cliente { get; set; }

    public int Bruto { get; set; }
    public int DescuentoTotal { get; set; }
    public int Total { get; set; }

    public MedioPago MedioPago { get; set; }
    public int? Recibido { get; set; }
    public int? Vuelto { get; set; }

    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }

    public long Lineas { get; set; }
    public long Unidades { get; set; }

    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public DateTime? AnuladaEn { get; set; }

    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>
/// La boleta completa, con sus líneas y su plan de consumo.
///
/// `CostoTotal` y `Margen` salen de los consumos, no del costo de ficha: es
/// lo que efectivamente valían las varas que salieron de la cámara.
/// </summary>
public class VentaDetalle
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;
    public int NumeroAtencion { get; set; }

    public int CajaId { get; set; }
    public int UsuarioId { get; set; }
    public string? Usuario { get; set; }

    public int? ClienteId { get; set; }
    public string? Cliente { get; set; }
    public string? ClienteRut { get; set; }

    public int? PromocionId { get; set; }
    public string? Promocion { get; set; }
    public int? CotizacionId { get; set; }

    public int Bruto { get; set; }
    public int DescuentoPromo { get; set; }
    public int DescuentoManual { get; set; }
    public int DescuentoCanje { get; set; }
    public int DescuentoTotal { get; set; }

    public decimal IvaTasa { get; set; }

    /// <summary>El IVA va incluido en el precio: el neto se desarma del total.</summary>
    public int Neto { get; set; }
    public int IvaMonto { get; set; }
    public int Total { get; set; }

    public MedioPago MedioPago { get; set; }
    public int? Recibido { get; set; }
    public int? Vuelto { get; set; }

    public int? AutorizadoPor { get; set; }
    public string? Autorizador { get; set; }

    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }

    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public int? AnuladaPor { get; set; }
    public string? Anulador { get; set; }
    public DateTime? AnuladaEn { get; set; }

    public DateTime CreadoEn { get; set; }

    public decimal CostoTotal { get; set; }
    public decimal? Margen { get; set; }

    public IReadOnlyList<VentaItem> Items { get; set; } = Array.Empty<VentaItem>();

    /// <summary>Qué salió, de qué lote y a qué costo real.</summary>
    public IReadOnlyList<VentaConsumo> Consumos { get; set; } = Array.Empty<VentaConsumo>();

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

public class VentaItem
{
    public int Id { get; set; }
    public int ProductoId { get; set; }

    /// <summary>Se guarda el nombre del momento: si el producto se renombra, la boleta no cambia.</summary>
    public string Nombre { get; set; } = string.Empty;

    public string? Emoji { get; set; }
    public int PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }
    public bool EsServicio { get; set; }
}

public class VentaConsumo
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    /// <summary>Null en los armados: consumen unidades montadas, no varas de un balde.</summary>
    public int? LoteId { get; set; }
    public string? LoteCodigo { get; set; }

    public TipoConsumo Tipo { get; set; }
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public decimal CostoTotal { get; set; }
}

/// <summary>Lo que devuelve sp_ven_u_venta_anular.</summary>
public class ResultadoAnulacion
{
    public int VentaId { get; set; }
    public string Folio { get; set; } = string.Empty;

    /// <summary>Unidades que volvieron al inventario.</summary>
    public int Devuelto { get; set; }

    /// <summary>Ajuste de puntos: positivo si se devolvieron canjeados.</summary>
    public int Puntos { get; set; }
}

/// <summary>Una promoción que aplica al carrito, con el descuento ya calculado.</summary>
public class PromocionAplicable
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }
    public TipoPromocion Tipo { get; set; }
    public int Valor { get; set; }
    public AlcancePromocion Alcance { get; set; }
    public int Minimo { get; set; }

    /// <summary>
    /// Estimación sobre los precios actuales. Al cobrar se recalcula: esto
    /// no compromete el monto final.
    /// </summary>
    public int Descuento { get; set; }
}
