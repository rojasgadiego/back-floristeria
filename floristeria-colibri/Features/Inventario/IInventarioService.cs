using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Inventario.Dtos;

namespace Colibri.Api.Features.Inventario;

public interface IInventarioService
{
    // Productos
    Task<ResultadoPagina<ProductoDto>> ListarAsync(ProductoFiltro filtro, CancellationToken ct = default);
    Task<ProductoDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<ProductoDetalleDto> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);
    Task<ProductoDetalleDto> CrearAsync(CrearProductoRequest peticion, CancellationToken ct = default);
    Task<ProductoDetalleDto> ActualizarAsync(int id, ActualizarProductoRequest peticion, CancellationToken ct = default);
    Task<ProductoDto> CambiarEstadoAsync(int id, bool activo, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);

    // Recetas
    Task<IReadOnlyList<IngredienteDto>> ObtenerRecetaAsync(int id, CancellationToken ct = default);
    Task<ProductoDetalleDto> GuardarRecetaAsync(int id, GuardarRecetaRequest peticion, CancellationToken ct = default);

    // Movimientos de stock
    Task<ProductoDto> AjustarStockAsync(int id, AjustarStockRequest peticion, CancellationToken ct = default);
    Task<ResultadoArmadoDto> ArmarAsync(int id, ArmarRequest peticion, CancellationToken ct = default);
    /// <summary>Qué se puede armar hoy y con qué lotes.</summary>
    Task<DisponibilidadArmadoDto> DisponibilidadArmadoAsync(int id, int cantidad, CancellationToken ct = default);
    Task<ResultadoPagina<MovimientoDto>> ListarMovimientosAsync(MovimientoFiltro filtro, CancellationToken ct = default);

    // Apoyo
    Task<IReadOnlyList<ProductoDto>> BajoMinimoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<CategoriaDto>> ListarCategoriasAsync(CancellationToken ct = default);
    Task<CategoriaDto> CrearCategoriaAsync(CrearCategoriaRequest peticion, CancellationToken ct = default);
}