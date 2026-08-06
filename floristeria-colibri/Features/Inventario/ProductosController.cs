using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Inventario.Dtos;

namespace Colibri.Api.Features.Inventario;

/// <summary>
/// Catálogo de productos, recetas y movimientos de stock.
/// Consultar lo puede hacer cualquier rol; modificar, solo Admin y Bodega.
/// </summary>
[Authorize(Policy = Politicas.VerInventario)]
public class ProductosController : ControladorBase
{
    private readonly IInventarioService _inventario;

    public ProductosController(IInventarioService inventario) => _inventario = inventario;

    /// <summary>Lista el catálogo con disponibilidad, costo y margen.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<ProductoDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] ProductoFiltro filtro, CancellationToken ct)
        => Exito(await _inventario.ListarAsync(filtro, ct));

    /// <summary>Detalle con la receta y, en los simples, en qué ramos se usa.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ProductoDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _inventario.ObtenerAsync(id, ct));

    /// <summary>Búsqueda por código de barras, para el punto de venta.</summary>
    [HttpGet("codigo/{codigo}")]
    [ProducesResponseType(typeof(ApiResponse<ProductoDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PorCodigo(string codigo, CancellationToken ct)
        => Exito(await _inventario.ObtenerPorCodigoAsync(codigo, ct));

    /// <summary>
    /// Crea un producto.
    /// </summary>
    /// <remarks>
    /// Un producto simple necesita costo. Uno armado necesita receta: sin
    /// ingredientes no se puede armar ni costear.
    ///
    /// Si el producto controla lotes, el stock inicial se ignora: las
    /// existencias entran recibiendo una compra, que es lo que genera el
    /// lote con su procedencia y su vencimiento.
    /// </remarks>
    [HttpPost]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<ProductoDetalleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(
        [FromBody] CrearProductoRequest peticion, CancellationToken ct)
    {
        var producto = await _inventario.CrearAsync(peticion, ct);
        return Creado(producto, $"{producto.Nombre} agregado al inventario");
    }

    /// <summary>
    /// Actualiza la ficha. El tipo y el stock no se tocan acá: el tipo es
    /// inmutable y el stock se mueve con ajustes, ventas o mermas.
    /// </summary>
    [HttpPut("{id:int}")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<ProductoDetalleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] ActualizarProductoRequest peticion, CancellationToken ct)
        => Exito(await _inventario.ActualizarAsync(id, peticion, ct));

    [HttpPatch("{id:int}/activar")]
    [Authorize(Policy = Politicas.Inventario)]
    public async Task<IActionResult> Activar(int id, CancellationToken ct)
        => Exito(await _inventario.CambiarEstadoAsync(id, true, ct), "Producto activado");

    /// <summary>
    /// Lo saca del punto de venta conservando su historial. Se rechaza si el
    /// producto es ingrediente de algún ramo.
    /// </summary>
    [HttpPatch("{id:int}/desactivar")]
    [Authorize(Policy = Politicas.Inventario)]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
        => Exito(await _inventario.CambiarEstadoAsync(id, false, ct), "Producto desactivado");

    /// <summary>
    /// Borrado definitivo. Solo para productos creados por error: si tiene
    /// movimientos, ventas o lotes, se rechaza.
    /// </summary>
    [HttpDelete("{id:int}")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await _inventario.EliminarAsync(id, ct);
        return SinContenido();
    }

    /* ---------------- Recetas ---------------- */

    /// <summary>Ingredientes del ramo, con cuánto alcanza cada uno.</summary>
    [HttpGet("{id:int}/receta")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<IngredienteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Receta(int id, CancellationToken ct)
        => Exito(await _inventario.ObtenerRecetaAsync(id, ct));

    /// <summary>
    /// Reemplaza la receta completa. Solo acepta productos simples como
    /// ingredientes: no puede haber ramos dentro de ramos.
    /// </summary>
    [HttpPut("{id:int}/receta")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<ProductoDetalleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GuardarReceta(
        int id, [FromBody] GuardarRecetaRequest peticion, CancellationToken ct)
        => Exito(await _inventario.GuardarRecetaAsync(id, peticion, ct), "Receta guardada");

    /* ---------------- Movimientos ---------------- */

    /// <summary>
    /// Ajuste manual de stock.
    /// </summary>
    /// <remarks>
    /// Solo para productos SIN control por lote: jarrones, papel, tarjetas.
    /// En una flor las existencias pertenecen a un lote concreto, y sumar
    /// unidades sueltas dejaría stock sin procedencia ni vencimiento.
    ///
    /// Exige motivo: es lo que después permite responder por qué el
    /// inventario no cuadra.
    /// </remarks>
    [HttpPost("{id:int}/ajustar-stock")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<ProductoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AjustarStock(
        int id, [FromBody] AjustarStockRequest peticion, CancellationToken ct)
        => Exito(await _inventario.AjustarStockAsync(id, peticion, ct), "Movimiento registrado");

    /// <summary>
    /// Qué se puede armar hoy y con qué.
    /// </summary>
    /// <remarks>
    /// Separa lo que alcanza con flor de primera de lo que alcanzaría usando
    /// también la recuperada, y sugiere qué lotes la cubrirían: su calidad,
    /// sus días en cámara y su vencimiento.
    ///
    /// La flor recuperada no se usa sola: está fuera del reparto automático
    /// porque un ramo para un matrimonio probablemente no debería llevarla.
    /// La persona revisa y decide.
    /// </remarks>
    [HttpGet("{id:int}/disponibilidad-armado")]
    [ProducesResponseType(typeof(ApiResponse<DisponibilidadArmadoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> DisponibilidadArmado(
        int id, [FromQuery] int cantidad = 1, CancellationToken ct = default)
        => Exito(await _inventario.DisponibilidadArmadoAsync(id, cantidad, ct));

    /// <summary>
    /// Arma N unidades de un ramo.
    /// </summary>
    /// <remarks>
    /// Descuenta los ingredientes y suma unidades listas. Los que llevan lote
    /// se consumen por FIFO: primero el más antiguo.
    ///
    /// Con `lotesAutorizados` se puede incluir flor recuperada, que de otro
    /// modo queda fuera del reparto automático. Esos lotes se consumen
    /// primero y su costo menor abarata la producción: un ramo con dos varas
    /// reutilizadas cuesta menos que uno con flor toda nueva.
    ///
    /// La respuesta detalla de qué lote salió cada tallo, si era recuperado,
    /// y el costo real de la producción.
    /// </remarks>
    [HttpPost("{id:int}/armar")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<ResultadoArmadoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Armar(
        int id, [FromBody] ArmarRequest peticion, CancellationToken ct)
    {
        var resultado = await _inventario.ArmarAsync(id, peticion, ct);
        return Exito(resultado, $"{resultado.Armadas} unidad(es) armada(s)");
    }
}