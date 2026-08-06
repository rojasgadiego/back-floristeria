using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Ventas.Dtos;

namespace Colibri.Api.Features.Ventas;

public interface IVentasService
{
    Task<VentaDetalleDto> RegistrarAsync(RegistrarVentaRequest peticion, CancellationToken ct = default);
    Task<ResultadoPagina<VentaDto>> ListarAsync(VentaFiltro filtro, CancellationToken ct = default);
    Task<VentaDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<TicketDto> TicketAsync(int id, CancellationToken ct = default);
    Task<VentaDto> AnularAsync(int id, AnularVentaRequest peticion, CancellationToken ct = default);

    /// <summary>
    /// Promociones que aplican a un carrito, con el descuento ya calculado.
    /// El punto de venta las muestra para elegir la que más conviene.
    /// </summary>
    Task<IReadOnlyList<PromocionAplicableDto>> PromocionesAplicablesAsync(
        List<LineaVentaRequest> items, CancellationToken ct = default);
}