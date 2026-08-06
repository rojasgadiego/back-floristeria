using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Promociones.Dtos;

namespace Colibri.Api.Features.Promociones;

public interface IPromocionesService
{
    Task<ResultadoPagina<PromocionDto>> ListarAsync(PromocionFiltro filtro, CancellationToken ct = default);
    Task<PromocionDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>Las que corren hoy. Es lo que el punto de venta ofrece.</summary>
    Task<IReadOnlyList<PromocionDto>> VigentesAsync(CancellationToken ct = default);

    Task<PromocionDetalleDto> CrearAsync(GuardarPromocionRequest peticion, CancellationToken ct = default);
    Task<PromocionDetalleDto> ActualizarAsync(int id, GuardarPromocionRequest peticion, CancellationToken ct = default);
    Task<PromocionDto> CambiarEstadoAsync(int id, bool activa, CancellationToken ct = default);
    Task EliminarAsync(int id, CancellationToken ct = default);

    /// <summary>
    /// Evalúa la promoción contra las ventas reales del período: cuánto
    /// habría descontado y sobre cuántas boletas.
    /// </summary>
    Task<SimulacionDto> SimularAsync(SimularRequest peticion, CancellationToken ct = default);
}