using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Lotes.Dtos;

namespace Colibri.Api.Features.Lotes;

public interface ILotesService
{
    Task<ResultadoPagina<LoteDto>> ListarActivosAsync(LoteFiltro filtro, CancellationToken ct = default);
    Task<ResultadoPagina<LoteDto>> ListarHistorialAsync(HistorialLoteFiltro filtro, CancellationToken ct = default);
    Task<LoteDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<LoteDetalleDto> ObtenerPorCodigoAsync(string codigo, CancellationToken ct = default);

    /// <summary>Respuesta del escaneo del QR.</summary>
    Task<ValidacionDto> ValidarAsync(ValidarLoteRequest peticion, CancellationToken ct = default);

    Task<IReadOnlyList<LoteDto>> RezagadosAsync(CancellationToken ct = default);
    Task<IReadOnlyList<LoteDto>> PorVencerAsync(int dias, CancellationToken ct = default);
    Task<IReadOnlyList<CostoPromedioDto>> CostoPromedioAsync(CancellationToken ct = default);
    /// <summary>Flor recuperada: el balde aparte, con su rebaja.</summary>
    Task<IReadOnlyList<LoteRecuperadoDto>> RecuperadosAsync(CancellationToken ct = default);

    Task<LoteDto> ActualizarUbicacionAsync(int id, ActualizarUbicacionRequest peticion, CancellationToken ct = default);

    Task<IReadOnlyList<EtiquetaDto>> EtiquetasAsync(IEnumerable<int> ids, CancellationToken ct = default);
    Task<IReadOnlyList<EtiquetaDto>> EtiquetasDeCompraAsync(int compraId, CancellationToken ct = default);
    Task<byte[]> GenerarQrAsync(string codigo, CancellationToken ct = default);
}