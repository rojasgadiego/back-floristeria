using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Compras.Dtos;

namespace Colibri.Api.Features.Compras;

public interface IComprasService
{
    // Proveedores
    Task<ResultadoPagina<ProveedorDto>> ListarProveedoresAsync(ProveedorFiltro filtro, CancellationToken ct = default);
    Task<ProveedorDto> ObtenerProveedorAsync(int id, CancellationToken ct = default);
    Task<ProveedorDto> CrearProveedorAsync(GuardarProveedorRequest peticion, CancellationToken ct = default);
    Task<ProveedorDto> ActualizarProveedorAsync(int id, GuardarProveedorRequest peticion, CancellationToken ct = default);
    Task<ProveedorDto> CambiarEstadoProveedorAsync(int id, bool activo, CancellationToken ct = default);

    // Presentaciones
    Task<IReadOnlyList<PresentacionDto>> ListarPresentacionesAsync(int productoId, CancellationToken ct = default);
    Task<PresentacionDto> CrearPresentacionAsync(int productoId, GuardarPresentacionRequest peticion, CancellationToken ct = default);
    Task<PresentacionDto> ActualizarPresentacionAsync(int id, GuardarPresentacionRequest peticion, CancellationToken ct = default);
    Task EliminarPresentacionAsync(int id, CancellationToken ct = default);

    // Compras
    Task<ResultadoPagina<CompraDto>> ListarAsync(CompraFiltro filtro, CancellationToken ct = default);
    Task<CompraDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<CompraDetalleDto> CrearAsync(GuardarCompraRequest peticion, CancellationToken ct = default);
    Task<CompraDetalleDto> ActualizarAsync(int id, GuardarCompraRequest peticion, CancellationToken ct = default);
    Task<ResultadoRecepcionDto> RecibirAsync(int id, CancellationToken ct = default);
    Task<CompraDto> AnularAsync(int id, CancellationToken ct = default);

    // Análisis
    Task<IReadOnlyList<EvolucionCostoDto>> EvolucionCostoAsync(int productoId, CancellationToken ct = default);
}