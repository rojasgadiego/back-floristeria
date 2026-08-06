using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Ventas.Dtos;

namespace Colibri.Api.Features.Ventas;

public interface ICajaService
{
    /// <summary>Turno abierto, o null si no hay ninguno.</summary>
    Task<ResumenCajaDto?> ActualAsync(CancellationToken ct = default);

    Task<CajaDto> AbrirAsync(AbrirCajaRequest peticion, CancellationToken ct = default);
    Task<ResumenCajaDto> CerrarAsync(CerrarCajaRequest peticion, CancellationToken ct = default);
    Task<ResumenCajaDto> ResumenAsync(int id, CancellationToken ct = default);
    Task<ResultadoPagina<CajaDto>> HistorialAsync(CajaFiltro filtro, CancellationToken ct = default);

    /// <summary>
    /// Caja abierta, o excepción. La usa el registro de ventas: sin turno
    /// abierto no hay dónde imputar la boleta.
    /// </summary>
    Task<int> CajaAbiertaIdAsync(CancellationToken ct = default);
}