using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Cotizaciones.Dtos;

namespace Colibri.Api.Features.Cotizaciones;

public interface ICotizacionesService
{
    Task<ResultadoPagina<CotizacionDto>> ListarAsync(CotizacionFiltro filtro, CancellationToken ct = default);
    Task<CotizacionDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);

    Task<CotizacionDetalleDto> CrearAsync(GuardarCotizacionRequest peticion, CancellationToken ct = default);
    Task<CotizacionDetalleDto> ActualizarAsync(int id, GuardarCotizacionRequest peticion, CancellationToken ct = default);
    Task<CotizacionDto> AprobarAsync(int id, CancellationToken ct = default);
    Task<CotizacionDto> AnularAsync(int id, AnularCotizacionRequest peticion, CancellationToken ct = default);

    // Pagos
    Task<PagoDto> RegistrarPagoAsync(int id, RegistrarPagoRequest peticion, CancellationToken ct = default);
    Task<PagoDto> AnularPagoAsync(int id, int pagoId, AnularPagoRequest peticion, CancellationToken ct = default);

    // Plan de cuotas
    Task<IReadOnlyList<CuotaDto>> GuardarCuotasAsync(int id, GuardarCuotasRequest peticion, CancellationToken ct = default);
    Task<IReadOnlyList<CuotaDto>> GenerarCuotasAsync(int id, GenerarCuotasRequest peticion, CancellationToken ct = default);

    /// <summary>Líneas sugeridas para la boleta final, con avisos de stock.</summary>
    Task<PreparacionCobroDto> PrepararCobroAsync(int id, CancellationToken ct = default);

    /// <summary>Eventos con saldo vencido: a quiénes hay que llamar.</summary>
    Task<IReadOnlyList<CotizacionDto>> PorCobrarAsync(CancellationToken ct = default);

    /// <summary>Eventos próximos, para preparar la compra de flor.</summary>
    Task<IReadOnlyList<CotizacionDto>> AgendaAsync(int dias, CancellationToken ct = default);
}