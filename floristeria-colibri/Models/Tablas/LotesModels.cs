using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una fila de la grilla de lotes.
///
/// Tres campos que no salen de ninguna columna y los calcula el SP:
///
/// OrdenFifo    posición en la fila de consumo, por producto. El 1 es el que
///              debería venderse ahora. Null en los que requieren escaneo.
///
/// Alerta       normal · por vencer · vencido · resto por liquidar.
///
/// RequiereEscaneo  el lote está fuera del reparto automático porque tiene
///              precio propio. Si entrara en la fila normal, una venta sin
///              escaneo cobraría flor de segunda a precio de primera.
/// </summary>
public class Lote
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public string? Proveedor { get; set; }
    public string? Presentacion { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int DiasEnCamara { get; set; }

    /// <summary>Negativo si ya venció. Null si el producto no tiene días de vida.</summary>
    public int? DiasParaVencer { get; set; }

    public int VarasIniciales { get; set; }
    public int VarasDisponibles { get; set; }
    public decimal PorcentajeVendido { get; set; }

    public decimal CostoPorVara { get; set; }
    public decimal ValorRestante { get; set; }

    /// <summary>El precio de ESTE lote, que puede no ser el del producto.</summary>
    public int PrecioVenta { get; set; }

    /// <summary>Precio propio. Null = usa el del producto.</summary>
    public int? PrecioUnitario { get; set; }

    public int Rebaja { get; set; }
    public decimal? RebajaPorcentaje { get; set; }

    public int? OrdenFifo { get; set; }
    public string Alerta { get; set; } = "normal";

    public bool EsRecuperado { get; set; }
    public bool RequiereEscaneo { get; set; }
    public CalidadReingreso? Calidad { get; set; }

    /// <summary>Código del lote del que salió, si es recuperado.</summary>
    public string? LoteOrigen { get; set; }

    /// <summary>Dónde está el balde: 'Cámara 1, estante 3'. Nada que ver con bodega/mostrador.</summary>
    public string? Ubicacion { get; set; }

    public EstadoLote Estado { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>La ficha completa: agrega procedencia y movimientos.</summary>
public class LoteDetalle : Lote
{
    public string? CompraFolio { get; set; }
    public string? Documento { get; set; }
    public string? Notas { get; set; }

    public IReadOnlyList<MovimientoLote> Movimientos { get; set; } = Array.Empty<MovimientoLote>();
}

public class MovimientoLote
{
    public int Id { get; set; }
    public DateTime Fecha { get; set; }
    public TipoMovimiento Tipo { get; set; }

    /// <summary>Negativa en las salidas: el signo dice la dirección.</summary>
    public int Cantidad { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? Usuario { get; set; }
}

/// <summary>Las listas cortas del encabezado: rezagados, por vencer, recuperados.</summary>
public class LoteAlerta
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int VarasDisponibles { get; set; }
    public decimal ValorRestante { get; set; }

    public int DiasEnCamara { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }
    public string? Alerta { get; set; }

    // Solo en recuperados
    public CalidadReingreso? Calidad { get; set; }
    public string? LoteOrigen { get; set; }
    public int PrecioVenta { get; set; }
    public int Rebaja { get; set; }
    public decimal? RebajaPorcentaje { get; set; }
}

/// <summary>
/// El valor real de la cámara, ponderado por lote. Con lotes de $900 y $800
/// mezclados, stock × costo de ficha da otro número.
/// </summary>
public class CostoPromedio
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public long Lotes { get; set; }
    public long Varas { get; set; }

    /// <summary>Ponderado por varas disponibles, no promedio simple de lotes.</summary>
    public decimal CostoPromedioPonderado { get; set; }

    public decimal CostoMinimo { get; set; }
    public decimal CostoMaximo { get; set; }
    public decimal ValorTotal { get; set; }
}

/// <summary>
/// Respuesta del escaneo en el punto de venta.
///
/// INFORMATIVO: la validación que manda ocurre al cobrar, cuando las filas se
/// bloquean. Si otra caja se lleva las últimas varas entremedio, la venta
/// falla ahí y no acá.
/// </summary>
public class ValidacionLote
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int PrecioVenta { get; set; }
    public int PrecioProducto { get; set; }
    public int Rebaja { get; set; }

    public int VarasDisponibles { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }

    public bool EsRecuperado { get; set; }
    public bool RequiereEscaneo { get; set; }
    public CalidadReingreso? Calidad { get; set; }
    public EstadoLote Estado { get; set; }

    public bool Puede { get; set; }

    /// <summary>Por qué no, cuando la respuesta es no. Redactado para mostrar.</summary>
    public string? Motivo { get; set; }

    /// <summary>Se puede vender, pero conviene decirlo en voz alta.</summary>
    public string? Advertencia { get; set; }
}

/// <summary>
/// Los datos para imprimir una etiqueta. El QR se pide aparte como PNG:
/// mandarlo en base64 dentro del JSON multiplicaría el peso de la respuesta
/// por cincuenta etiquetas.
/// </summary>
public class EtiquetaLote
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Proveedor { get; set; }
    public string? Presentacion { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int Varas { get; set; }
    public string? Ubicacion { get; set; }
}
