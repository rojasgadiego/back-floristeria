using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Inventario.Dtos;

/* ===================== Lectura ===================== */

public class ProductoDto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int CategoriaId { get; set; }
    public string Categoria { get; set; } = string.Empty;

    /// <summary>simple o armado.</summary>
    public string Tipo { get; set; } = string.Empty;

    public int Precio { get; set; }
    public int Minimo { get; set; }
    public bool Activo { get; set; }

    /// <summary>Existencias reales. Nulo en productos armados.</summary>
    public int? Stock { get; set; }

    /// <summary>Unidades ya hechas en cámara. Nulo en productos simples.</summary>
    public int? StockListo { get; set; }

    /// <summary>
    /// Vendible hoy. En un armado: las unidades hechas más las que alcanzan
    /// a armarse con el stock de tallos.
    /// </summary>
    public int Disponible { get; set; }

    /// <summary>Cuántas unidades más se pueden armar. Nulo en simples.</summary>
    public int? PosiblesDeArmar { get; set; }

    /// <summary>Para un armado: su receta más la mano de obra.</summary>
    public int CostoUnitario { get; set; }
    public decimal MargenPorcentaje { get; set; }

    public bool ControlaLotes { get; set; }
    public int? DiasVida { get; set; }
    public bool BajoMinimo { get; set; }
}

public class ProductoDetalleDto : ProductoDto
{
    public IReadOnlyList<IngredienteDto> Receta { get; set; } = Array.Empty<IngredienteDto>();

    /// <summary>Ramos que usan este producto como ingrediente.</summary>
    public IReadOnlyList<string> UsadoEn { get; set; } = Array.Empty<string>();

    public int? CostoArmado { get; set; }
}

public class IngredienteDto
{
    public int ProductoId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public int CostoUnitario { get; set; }

    /// <summary>Costo de este ingrediente dentro del ramo.</summary>
    public int Subtotal { get; set; }

    public int StockDisponible { get; set; }

    /// <summary>Para cuántos ramos alcanza este ingrediente por sí solo.</summary>
    public int AlcanzaPara { get; set; }
}

public class CategoriaDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public int Orden { get; set; }
    public int Productos { get; set; }
}

public class MovimientoDto
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? LoteCodigo { get; set; }
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Con signo: negativo si el producto sale.</summary>
    public int Cantidad { get; set; }

    public int? StockResultante { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? Usuario { get; set; }
    public string? ReferenciaTipo { get; set; }
    public int? ReferenciaId { get; set; }
}

/* ===================== Escritura ===================== */

public class CrearProductoRequest
{
    [Required(ErrorMessage = "El código es obligatorio.")]
    [StringLength(40)]
    public string Codigo { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(160, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [Required(ErrorMessage = "La categoría es obligatoria.")]
    public int CategoriaId { get; set; }

    /// <summary>simple (tallo, planta, insumo) o armado (ramo, arreglo).</summary>
    [Required(ErrorMessage = "El tipo es obligatorio.")]
    public string Tipo { get; set; } = "simple";

    [StringLength(8)]
    public string Emoji { get; set; } = "🌿";

    [Range(1, int.MaxValue, ErrorMessage = "El precio debe ser mayor que cero.")]
    public int Precio { get; set; }

    [Range(0, int.MaxValue)]
    public int Minimo { get; set; }

    // --- Solo productos simples ---
    [Range(0, int.MaxValue, ErrorMessage = "El costo no puede ser negativo.")]
    public int? Costo { get; set; }

    /// <summary>
    /// Stock inicial. Se ignora si el producto controla lotes: en ese caso
    /// las existencias entran recibiendo una compra.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int? StockInicial { get; set; }

    /// <summary>Las flores sí; un jarrón de vidrio no.</summary>
    public bool ControlaLotes { get; set; }

    /// <summary>Vida útil en cámara. Fija el vencimiento de cada lote.</summary>
    [Range(1, 3650)]
    public int? DiasVida { get; set; }

    // --- Solo productos armados ---
    [Range(0, int.MaxValue)]
    public int? CostoArmado { get; set; }

    /// <summary>
    /// Ingredientes. Obligatorio en un armado: sin receta no se puede armar
    /// ni costear, y quedaría un producto inutilizable.
    /// </summary>
    public List<LineaRecetaRequest> Receta { get; set; } = new();
}

public class ActualizarProductoRequest
{
    [Required]
    [StringLength(40)]
    public string Codigo { get; set; } = string.Empty;

    [Required]
    [StringLength(160, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [Required]
    public int CategoriaId { get; set; }

    [StringLength(8)]
    public string Emoji { get; set; } = "🌿";

    [Range(1, int.MaxValue)]
    public int Precio { get; set; }

    [Range(0, int.MaxValue)]
    public int Minimo { get; set; }

    /// <summary>Solo aplica a productos simples.</summary>
    [Range(0, int.MaxValue)]
    public int? Costo { get; set; }

    /// <summary>Solo aplica a productos armados.</summary>
    [Range(0, int.MaxValue)]
    public int? CostoArmado { get; set; }

    [Range(1, 3650)]
    public int? DiasVida { get; set; }
}

public class LineaRecetaRequest
{
    [Required]
    public int ProductoId { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; }
}

public class GuardarRecetaRequest
{
    /// <summary>
    /// Reemplaza la receta completa. Lista vacía la deja sin ingredientes,
    /// y en ese estado el producto no se puede armar.
    /// </summary>
    public List<LineaRecetaRequest> Ingredientes { get; set; } = new();
}

public class AjustarStockRequest
{
    /// <summary>Con signo: positivo entra, negativo sale.</summary>
    [Required(ErrorMessage = "La cantidad es obligatoria.")]
    public int Cantidad { get; set; }

    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(200, MinimumLength = 3)]
    public string Motivo { get; set; } = string.Empty;

    [StringLength(400)]
    public string? Detalle { get; set; }
}

public class ArmarRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Indica cuántas unidades vas a armar.")]
    public int Cantidad { get; set; } = 1;

    /// <summary>
    /// Lotes de flor recuperada que se autorizan para este armado.
    ///
    /// Esos lotes están fuera del reparto automático porque tienen precio
    /// propio: usarlos es una decisión, no algo que el sistema deba hacer
    /// solo. Un ramo para un matrimonio probablemente no debería llevarlos.
    /// </summary>
    public List<int> LotesAutorizados { get; set; } = new();
}

/// <summary>
/// Qué se puede armar hoy y con qué. Se consulta antes de armar para saber
/// si alcanza con flor de primera o si hay que echar mano de la recuperada.
/// </summary>
public class DisponibilidadArmadoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Solicitado { get; set; }

    /// <summary>Cuántas unidades alcanzan solo con flor de primera.</summary>
    public int PosiblesConPrimera { get; set; }

    /// <summary>Cuántas alcanzan sumando la flor recuperada disponible.</summary>
    public int PosiblesConRecuperada { get; set; }

    /// <summary>Si lo solicitado se puede armar sin usar flor recuperada.</summary>
    public bool AlcanzaConPrimera { get; set; }

    /// <summary>Si lo solicitado se puede armar usando también la recuperada.</summary>
    public bool AlcanzaConRecuperada { get; set; }

    public IReadOnlyList<FaltanteDto> Faltantes { get; set; } = Array.Empty<FaltanteDto>();

    /// <summary>
    /// Lotes de flor recuperada que cubrirían el faltante. La persona los
    /// revisa —estado, días en cámara, vencimiento— y decide.
    /// </summary>
    public IReadOnlyList<LoteSugeridoDto> LotesSugeridos { get; set; }
        = Array.Empty<LoteSugeridoDto>();
}

public class FaltanteDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;

    /// <summary>Varas que exige lo solicitado.</summary>
    public int Necesita { get; set; }

    /// <summary>Varas de primera disponibles.</summary>
    public int HayDePrimera { get; set; }

    /// <summary>Varas de flor recuperada disponibles.</summary>
    public int HayRecuperada { get; set; }

    /// <summary>Cuántas faltan aun contando la recuperada.</summary>
    public int Faltan { get; set; }
}

public class LoteSugeridoDto
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;

    /// <summary>optima · buena · limitada</summary>
    public string? Calidad { get; set; }

    public int VarasDisponibles { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int DiasEnCamara { get; set; }
    public string Alerta { get; set; } = string.Empty;

    /// <summary>Costo de estas varas. Menor al de la flor nueva.</summary>
    public decimal CostoPorVara { get; set; }

    public int? PrecioUnitario { get; set; }
}

public class ResultadoArmadoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Armadas { get; set; }
    public int StockListo { get; set; }

    /// <summary>Costo total de la producción, con los lotes realmente usados.</summary>
    public int CostoProduccion { get; set; }

    public IReadOnlyList<ConsumoDto> Consumos { get; set; } = Array.Empty<ConsumoDto>();
}

public class ConsumoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;

    /// <summary>Nulo si el producto no controla lotes.</summary>
    public string? LoteCodigo { get; set; }

    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }

    /// <summary>Verdadero si salió de un lote de flor recuperada.</summary>
    public bool EsRecuperado { get; set; }
}

public class CrearCategoriaRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(80, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [Range(0, 999)]
    public int Orden { get; set; }
}

/* ===================== Filtros ===================== */

public class ProductoFiltro : ParametrosPagina
{
    /// <summary>simple o armado.</summary>
    public string? Tipo { get; set; }

    public int? CategoriaId { get; set; }

    /// <summary>Null trae todos; true solo activos.</summary>
    public bool? Activo { get; set; } = true;

    /// <summary>Solo los que llegaron a su mínimo.</summary>
    public bool BajoMinimo { get; set; }

    /// <summary>Solo los que llevan control por lote.</summary>
    public bool? ControlaLotes { get; set; }
}

public class MovimientoFiltro : ParametrosPagina
{
    public int? ProductoId { get; set; }
    public int? LoteId { get; set; }

    /// <summary>alta, entrada, salida, ajuste, venta, consumo, armado, merma, baja.</summary>
    public string? Tipo { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}