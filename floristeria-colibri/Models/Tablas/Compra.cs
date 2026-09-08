using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

public class Compra
{
    public int Id { get; set; }

    /// <summary>Correlativo por año: OC-2026-0001.</summary>
    public string Folio { get; set; } = string.Empty;

    public int ProveedorId { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? ProveedorRut { get; set; }

    public DateOnly Fecha { get; set; }

    /// <summary>La factura o guía del proveedor. Opcional al abrir el borrador.</summary>
    public string? Documento { get; set; }

    public EstadoCompra Estado { get; set; }

    public int Neto { get; set; }
    public int Iva { get; set; }
    public int Total { get; set; }

    public long Lineas { get; set; }
    public long VarasTotales  { get; set; }

    /// <summary>Cuántos lotes generó al recibirse. 0 mientras esté en borrador.</summary>
    public long Lotes { get; set; }

    public string? Notas { get; set; }
    public int? UsuarioId { get; set; }
    public string? Usuario { get; set; }

    public DateTime? RecibidaEn { get; set; }
    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

public class CompraItem
{
    public int Id { get; set; }
    public int CompraId { get; set; }

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int PresentacionId { get; set; }
    public string Presentacion { get; set; } = string.Empty;
    public int Paquetes { get; set; }
    public int VarasPorPaquete { get; set; }

    /// <summary>Cuántas unidades de la presentación (cajas, paquetes).</summary>
    public int Cantidad { get; set; }

    /// <summary>
    /// Cuántos QR van a salir al recibir esta línea. Verlo antes evita la
    /// sorpresa de imprimir cuarenta etiquetas sin haberlo previsto.
    /// </summary>
    public int PaquetesTotales { get; set; }

    public int VarasTotales { get; set; }

    /// <summary>Lo que cuesta una unidad de la presentación.</summary>
    public int CostoUnitario { get; set; }

    public decimal CostoPorVara { get; set; }
    public int Subtotal { get; set; }

    public long LotesGenerados { get; set; }
}

/// <summary>
/// Lo que devuelve sp_abs_u_compra_recibir.
///
/// Los nombres son los que el front ya esperaba: aparecen en la pantalla de
/// "mercadería ingresada" justo antes de imprimir.
/// </summary>
public class ResultadoRecepcion
{
    public int CompraId { get; set; }
    public string Folio { get; set; } = string.Empty;

    /// <summary>Uno por paquete. Son las etiquetas a imprimir.</summary>
    public int LotesGenerados { get; set; }

    public int VarasIngresadas { get; set; }
    public int Productos { get; set; }

    /// <summary>
    /// Los lotes recién creados, para mostrarlos sin una segunda consulta.
    /// Lo llena el BLL después de recibir.
    /// </summary>
    public IReadOnlyList<LoteResumen> Lotes { get; set; } = Array.Empty<LoteResumen>();
}