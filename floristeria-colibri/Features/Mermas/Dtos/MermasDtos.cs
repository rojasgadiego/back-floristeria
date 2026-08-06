using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Mermas.Dtos;

/* ===================== Lectura ===================== */

public class MermaDto
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;

    public int? LoteId { get; set; }
    public string? LoteCodigo { get; set; }
    public DateOnly? LoteIngreso { get; set; }
    public int? DiasEnCamara { get; set; }

    /// <summary>Unidades que salieron del lote.</summary>
    public int Cantidad { get; set; }

    /// <summary>Cuántas de esas volvieron al inventario.</summary>
    public int CantidadRecuperada { get; set; }

    /// <summary>Lo que efectivamente se perdió.</summary>
    public int CantidadPerdida => Cantidad - CantidadRecuperada;

    /// <summary>perdida · reingreso · devolucion_proveedor</summary>
    public string Destino { get; set; } = string.Empty;

    /// <summary>optima · buena · limitada. Null si no volvió nada.</summary>
    public string? CalidadReingreso { get; set; }

    public int? LoteRecuperacionId { get; set; }
    public string? LoteRecuperacionCodigo { get; set; }

    /// <summary>Costo con que volvieron las varas recuperadas.</summary>
    public int? CostoRecuperadoUnitario { get; set; }

    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }

    /// <summary>Congelado al registrar: la pérdida de ayer no cambia si sube el precio.</summary>
    public int CostoUnitario { get; set; }

    /// <summary>Valor de todo lo que se movió, se haya perdido o no.</summary>
    public int CostoTotal { get; set; }

    /// <summary>
    /// Lo que realmente costó. Cero en una devolución al proveedor: la
    /// mercadería se abona.
    /// </summary>
    public int CostoPerdido { get; set; }

    public string? Usuario { get; set; }
    public bool Revertida { get; set; }
    public DateTimeOffset? RevertidaEn { get; set; }
}

public class ResumenMermasDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    public int Registros { get; set; }

    /// <summary>Todo lo que salió del inventario por merma.</summary>
    public int UnidadesMovidas { get; set; }

    public int UnidadesRecuperadas { get; set; }
    public int UnidadesPerdidas { get; set; }

    /// <summary>Valor de lo movido.</summary>
    public int CostoMovido { get; set; }

    /// <summary>
    /// Lo que realmente se perdió. Es el número que importa: sin separarlo,
    /// un arreglo devuelto en perfecto estado aparecería como pérdida total.
    /// </summary>
    public int CostoPerdido { get; set; }

    /// <summary>Valor que volvió al inventario o que el proveedor abona.</summary>
    public int CostoRecuperado { get; set; }

    /// <summary>Lo que se botó, a su costo completo.</summary>
    public int CostoBotado { get; set; }

    /// <summary>
    /// Lo que volvió valiendo menos. Es el precio de reutilizar: se recupera
    /// la flor, pero no su valor entero.
    /// </summary>
    public int CostoDesvalorizado { get; set; }

    public long Vendido { get; set; }

    /// <summary>
    /// Pérdida real sobre ventas. En una florería, sobre 5% es señal de que
    /// se está comprando más de lo que se alcanza a vender.
    /// </summary>
    public decimal PorcentajeSobreVentas { get; set; }

    public IReadOnlyList<MermaPorDestinoDto> PorDestino { get; set; }
        = Array.Empty<MermaPorDestinoDto>();

    public IReadOnlyList<MermaPorProductoDto> PorProducto { get; set; }
        = Array.Empty<MermaPorProductoDto>();

    public IReadOnlyList<MermaPorMotivoDto> PorMotivo { get; set; }
        = Array.Empty<MermaPorMotivoDto>();
}

public class MermaPorDestinoDto
{
    public string Destino { get; set; } = string.Empty;
    public int Registros { get; set; }
    public int Unidades { get; set; }
    public int CostoMovido { get; set; }
    public int CostoBotado { get; set; }
    public int CostoDesvalorizado { get; set; }
    public int CostoPerdido { get; set; }
}

public class MermaPorProductoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int UnidadesMovidas { get; set; }
    public int UnidadesPerdidas { get; set; }
    public int CostoPerdido { get; set; }

    /// <summary>Unidades perdidas sobre las compradas en el período.</summary>
    public decimal? PorcentajeDeLoComprado { get; set; }
}

public class MermaPorMotivoDto
{
    public string Motivo { get; set; } = string.Empty;
    public int Registros { get; set; }
    public int Unidades { get; set; }
    public int CostoPerdido { get; set; }
}

/* ===================== Escritura ===================== */

public class RegistrarMermaRequest
{
    [Required(ErrorMessage = "Indica el producto.")]
    public int ProductoId { get; set; }

    /// <summary>
    /// Obligatorio si el producto se controla por lote: una flor perdida
    /// pertenece a un lote concreto, con su costo y su procedencia.
    /// </summary>
    public int? LoteId { get; set; }

    [Range(1, 100000, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; }

    /// <summary>
    /// Qué pasó con lo que salió: perdida, reingreso o devolucion_proveedor.
    /// </summary>
    [Required(ErrorMessage = "Indica el destino.")]
    public string Destino { get; set; } = "perdida";

    /// <summary>
    /// Cuántas de las unidades vuelven al inventario. Solo con destino
    /// reingreso.
    /// </summary>
    [Range(0, 100000)]
    public int CantidadRecuperada { get; set; }

    /// <summary>
    /// En qué estado vuelven. Óptima regresa al lote original; buena y
    /// limitada van a un lote de recuperación con su propio precio.
    /// </summary>
    public string? Calidad { get; set; }

    /// <summary>
    /// Precio del lote de recuperación. Si se indica, ese lote sale del
    /// reparto automático y solo se vende escaneándolo.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? PrecioRecuperado { get; set; }

    /// <summary>
    /// Costo con que vuelve la flor. Si no se indica, conserva el original.
    ///
    /// Bajarlo tiene consecuencia: la diferencia se reconoce como pérdida por
    /// deterioro en este mismo registro. Una rosa de $700 que vuelve valiendo
    /// $400 deja $300 de pérdida, y el ramo que se arme con ella costará $400.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? CostoRecuperado { get; set; }

    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(200, MinimumLength = 3,
        ErrorMessage = "Explica el motivo con al menos 3 caracteres.")]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(600)]
    public string? Detalle { get; set; }
}

public class DescartarLoteRequest
{
    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(200, MinimumLength = 3)]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(600)]
    public string? Detalle { get; set; }

    /// <summary>
    /// Devolución al proveedor en vez de pérdida. La mercadería sale del
    /// stock pero no cuenta como costo: se abona.
    /// </summary>
    public bool EsDevolucionProveedor { get; set; }
}

public class RevertirMermaRequest
{
    [Required(ErrorMessage = "Explica por qué se revierte.")]
    [StringLength(300, MinimumLength = 5)]
    public string Motivo { get; set; } = string.Empty;
}

/* ===================== Desarme ===================== */

/// <summary>
/// Destino de un grupo de tallos del ramo desarmado.
/// Del mismo componente pueden salir varias líneas: la clasificación es por
/// vara, no por conjunto.
/// </summary>
public class LineaDesarmeRequest
{
    [Required]
    public int ComponenteId { get; set; }

    [Range(1, 100000)]
    public int Cantidad { get; set; }

    /// <summary>perdida o reingreso.</summary>
    [Required(ErrorMessage = "Indica el destino de estas varas.")]
    public string Destino { get; set; } = "reingreso";

    /// <summary>optima, buena o limitada. Obligatorio si vuelven.</summary>
    public string? Calidad { get; set; }

    /// <summary>
    /// Lote al que vuelven si la calidad es óptima. Si no se indica, el
    /// sistema lo rastrea desde el armado.
    /// </summary>
    public int? LoteOrigenId { get; set; }

    /// <summary>Precio propio del lote de recuperación.</summary>
    [Range(1, int.MaxValue)]
    public int? PrecioUnitario { get; set; }

    /// <summary>
    /// Costo con que vuelven estas varas. Si no se indica, conservan el
    /// original. La diferencia se reconoce como pérdida por deterioro.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? CostoRecuperado { get; set; }
}

public class DesarmarRequest
{
    [Range(1, 1000, ErrorMessage = "Indica cuántas unidades vas a desarmar.")]
    public int Cantidad { get; set; } = 1;

    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(200, MinimumLength = 3)]
    public string Motivo { get; set; } = "Desarme de producto sin vender";

    [StringLength(600)]
    public string? Detalle { get; set; }

    /// <summary>
    /// Una línea por cada grupo de varas con el mismo destino y calidad.
    /// Las cantidades de cada componente deben sumar lo que dice la receta.
    /// </summary>
    [MinLength(1, ErrorMessage = "Indica qué pasa con las varas del desarme.")]
    public List<LineaDesarmeRequest> Lineas { get; set; } = new();
}

/// <summary>
/// Plan sugerido para desarmar: cuántas varas de cada componente salen y de
/// qué lote vinieron. La interfaz lo muestra pre-llenado en óptima y la
/// persona solo mueve lo que corresponda.
/// </summary>
public class PlanDesarmeDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public int StockListo { get; set; }
    public IReadOnlyList<LineaPlanDto> Lineas { get; set; } = Array.Empty<LineaPlanDto>();
}

public class LineaPlanDto
{
    public int ComponenteId { get; set; }
    public string Componente { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;

    /// <summary>Varas que salen en total por este componente.</summary>
    public int Cantidad { get; set; }

    /// <summary>Lote del que salieron al armar, si se pudo rastrear.</summary>
    public int? LoteOrigenId { get; set; }

    public string? LoteOrigenCodigo { get; set; }
    public DateOnly? LoteIngreso { get; set; }

    /// <summary>Días desde que la flor llegó al local, no desde el armado.</summary>
    public int? DiasEnCamara { get; set; }

    /// <summary>Precio de lista, como referencia para fijar el rebajado.</summary>
    public int PrecioLista { get; set; }
}

public class ResultadoDesarmeDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Desarmadas { get; set; }
    public int StockListo { get; set; }

    public int VarasRecuperadas { get; set; }
    public int VarasPerdidas { get; set; }
    public int CostoPerdido { get; set; }

    public IReadOnlyList<ResultadoLineaDesarmeDto> Lineas { get; set; }
        = Array.Empty<ResultadoLineaDesarmeDto>();
}

public class ResultadoLineaDesarmeDto
{
    public int ComponenteId { get; set; }
    public string Componente { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string Destino { get; set; } = string.Empty;
    public string? Calidad { get; set; }

    /// <summary>Dónde quedaron. Null si se perdieron.</summary>
    public string? LoteDestino { get; set; }

    /// <summary>Falso cuando volvieron a su lote original.</summary>
    public bool EsLoteNuevo { get; set; }

    public int? PrecioUnitario { get; set; }

    /// <summary>Costo con que quedaron en inventario.</summary>
    public int? CostoRecuperado { get; set; }
}

/* ===================== Filtros ===================== */

public class MermaFiltro : ParametrosPagina
{
    public int? ProductoId { get; set; }
    public int? LoteId { get; set; }
    public string? Motivo { get; set; }

    /// <summary>perdida, reingreso o devolucion_proveedor.</summary>
    public string? Destino { get; set; }

    /// <summary>Null trae todas; false excluye las revertidas.</summary>
    public bool? Revertida { get; set; } = false;

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}