using System.Text.Json.Serialization;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Lo que bajó de un balde concreto al mesón, con su propio código y QR.
///
/// Existe porque `inventario_venta` guardaba solo cantidad por producto, sin
/// memoria de dónde salió. Con eso, anular una boleta no sabría a qué lote
/// devolver las varas, y un lote con precio propio perdía su precio al bajar.
/// </summary>
public class Partida
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    /// <summary>Lo que va dentro del QR, ya armado por la vista.</summary>
    public string Qr { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    /// <summary>Null en lo que no controla lotes: un jarrón no viene de un balde.</summary>
    public int? LoteId { get; set; }
    public string? LoteCodigo { get; set; }

    public int CantidadInicial { get; set; }
    public int CantidadDisponible { get; set; }

    /// <summary>
    /// El precio con que bajó. Se copia en el momento del traspaso: si el
    /// precio del producto cambia mañana, lo que está en vitrina con su
    /// etiqueta impresa no cambia de valor sin que alguien lo decida.
    /// </summary>
    public int PrecioUnitario { get; set; }

    public int PrecioProducto { get; set; }
    public decimal? CostoPorVara { get; set; }

    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }
    public int DiasEnMeson { get; set; }

    public DateTime TraspasadoEn { get; set; }
    public string? TraspasadoPor { get; set; }

    public bool Vendible { get; set; }
    public string? Notas { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>
/// Lo que devuelve el escaneo en el mesón. Es la consulta más usada del
/// punto de venta: el vendedor apunta la cámara y esto le dice qué es, a
/// cuánto sale y si puede venderlo.
/// </summary>
public class PartidaEscaneada
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int? LoteId { get; set; }
    public string? LoteCodigo { get; set; }

    public int CantidadDisponible { get; set; }

    public int PrecioUnitario { get; set; }
    public int PrecioProducto { get; set; }
    public decimal? PrecioRamo { get; set; }
    public decimal? PrecioLiquidacion { get; set; }

    /// <summary>Cuánto menos que el precio normal. Cero si no hay rebaja.</summary>
    public int Rebaja { get; set; }

    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }
    public int DiasEnMeson { get; set; }

    public bool Vendible { get; set; }

    /// <summary>Por qué no, cuando la respuesta es no. Redactado para mostrar.</summary>
    public string? Motivo { get; set; }

    /// <summary>Se puede vender, pero conviene decirlo en voz alta.</summary>
    public string? Advertencia { get; set; }
}

/// <summary>Las partidas de un producto, en orden de consumo.</summary>
public class PartidaOrden
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int? LoteId { get; set; }
    public int CantidadDisponible { get; set; }
    public int PrecioUnitario { get; set; }
    public decimal? CostoPorVara { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }

    /// <summary>1 es el que se vende primero.</summary>
    public int Orden { get; set; }
}

/// <summary>Lo que devuelven los traspasos.</summary>
public class ResultadoTraspaso
{
    public int PartidaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Qr { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? LoteCodigo { get; set; }

    public int Cantidad { get; set; }
    public int EnBodega { get; set; }
    public int EnMostrador { get; set; }
    public int Precio { get; set; }
    public DateOnly? Vencimiento { get; set; }
}

public class ResultadoRetorno
{
    public int PartidaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public int Devuelto { get; set; }
    public int EnPartida { get; set; }
    public int EnBodega { get; set; }
}
