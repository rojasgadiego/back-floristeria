using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Lo que devuelve GET /compras/{id}: la cabecera con sus líneas y, si ya
/// se recibió, los lotes que generó.
///
/// Va todo junto porque el front abre el detalle en un acordeón y necesita
/// las tres cosas de una vez. Tres peticiones para pintar una fila que se
/// expande sería un parpadeo por sección.
/// </summary>
public class CompraDetalle
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;

    public int ProveedorId { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? ProveedorRut { get; set; }

    public DateOnly Fecha { get; set; }
    public string? Documento { get; set; }
    public EstadoCompra Estado { get; set; }

    public int Neto { get; set; }
    public int Iva { get; set; }
    public int Total { get; set; }

    public long Lineas { get; set; }
    public long VarasTotales { get; set; }

    public string? Notas { get; set; }
    public string? Usuario { get; set; }
    public DateTime? RecibidaEn { get; set; }
    public DateTime CreadoEn { get; set; }

    public IReadOnlyList<CompraItem> Items { get; set; } = Array.Empty<CompraItem>();

    /// <summary>Vacío mientras esté en borrador: los lotes nacen al recibir.</summary>
    public IReadOnlyList<LoteResumen> Lotes { get; set; } = Array.Empty<LoteResumen>();
}

/// <summary>
/// Los chips del detalle. La versión con QR y todo lo demás es
/// LoteEtiqueta, que se usa solo al imprimir.
/// </summary>
public class LoteResumen
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int Varas { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasRestantes { get; set; }
    public EstadoLote Estado { get; set; }
}

/// <summary>
/// Cómo se movió el costo por vara entre compras. El front usa la primera
/// fila como "lo que se pagó la vez pasada".
/// </summary>
public class EvolucionCosto
{
    public int CompraId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string Presentacion { get; set; } = string.Empty;

    public int Cantidad { get; set; }
    public int CostoUnitario { get; set; }
    public int VarasTotales { get; set; }
    public decimal CostoPorVara { get; set; }

    /// <summary>% respecto de la compra anterior. Null en la más antigua.</summary>
    public decimal? Variacion { get; set; }
}
