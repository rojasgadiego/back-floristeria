using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una fila de sp_inv_c_productos o sp_inv_c_producto. La grilla llena la
/// primera mitad; el detalle llena todo. Las que no vienen quedan en su valor
/// por defecto, y Dapper no se queja.
///
/// Propiedades con { get; set; }, no un record posicional: el binding por
/// constructor de Dapper combinado con MatchNamesWithUnderscores es territorio
/// de sorpresas silenciosas.
/// </summary>
public class Producto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    public int? CategoriaId { get; set; }
    public string? Categoria { get; set; }

    public TipoProducto Tipo { get; set; }

    // ─── Los tres precios ───

    /// <summary>La vara suelta, al mesón.</summary>
    public decimal Precio { get; set; }

    /// <summary>La misma vara dentro de un arreglo. Null = no aplica, usar Precio.</summary>
    public decimal? PrecioRamo { get; set; }

    /// <summary>
    /// La que ya no da para vitrina. Null = no aplica —un jarrón no se liquida
    /// por vencimiento—. Qué varas están en ese estado lo decide el lote, no
    /// esta columna.
    /// </summary>
    public decimal? PrecioLiquidacion { get; set; }

    // ─── Los dos lados ───

    /// <summary>
    /// Lo que hay atrás, en cámara. Es productos.stock, que desde el módulo de
    /// mostrador YA NO ES EL TOTAL: baja cuando se traspasa.
    /// </summary>
    public int EnBodega { get; set; }

    /// <summary>Lo que hay adelante, listo para vender. Sale de inventario_venta.</summary>
    public int EnVenta { get; set; }

    /// <summary>EnBodega + EnVenta. Lo calcula el SP; no se arma sumando acá.</summary>
    public int StockTotal { get; set; }

    /// <summary>
    /// Unidades de un ramo ya montadas. No confundir con EnVenta: un ramo puede
    /// estar armado y seguir en cámara.
    /// </summary>
    public int StockListo { get; set; }

    public int Minimo { get; set; }

    /// <summary>Se compara contra el total: 5 atrás y 40 adelante no es faltante.</summary>
    public bool BajoMinimo { get; set; }

    public bool ControlaLotes { get; set; }
    public bool Activo { get; set; }

    // ─── Solo en el detalle (sp_inv_c_producto) ───

    public decimal? Costo { get; set; }

    /// <summary>Lo que suma la receta. Null en los simples.</summary>
    public decimal? CostoArmado { get; set; }

    /// <summary>Calculado en el SP. Null si el precio es 0.</summary>
    public decimal? Margen { get; set; }

    public int? DiasVida { get; set; }

    public DateTime? CreadoEn { get; set; }
    public DateTime? ActualizadoEn { get; set; }

    /// <summary>
    /// El costo que corresponde según el tipo: `costo` en un simple,
    /// `costoArmado` en un armado. Lo resuelve el SP porque los CHECK
    /// productos_forma_* dejan uno de los dos en NULL siempre.
    /// </summary>
    public decimal? CostoEfectivo { get; set; }

    /// <summary>
    /// Viene del count(*) OVER () del SP para paginar sin una segunda consulta.
    /// No sale hacia el cliente: el total va en el sobre de ResultadoPagina.
    /// </summary>
    [JsonIgnore]
    public long TotalFilas { get; set; }
}