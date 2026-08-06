using Colibri.Api.Features.Reportes.Dtos;

namespace Colibri.Api.Features.Reportes;

public interface IReportesService
{
    /// <summary>Lo que se mira al llegar: cómo va el día y qué hay que atender.</summary>
    Task<PanelDto> PanelAsync(CancellationToken ct = default);

    Task<ResultadoPeriodoDto> ResultadoAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);

    Task<RendimientoProductosDto> ProductosAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);

    /// <summary>Cuánta plata hay dormida en la cámara ahora mismo.</summary>
    Task<ValorInventarioDto> InventarioAsync(CancellationToken ct = default);

    Task<RendimientoEquipoDto> EquipoAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);

    /// <summary>Desglose del turno, separando mostrador de abonos de eventos.</summary>
    Task<DesgloseTurnoDto> TurnoAsync(int cajaId, CancellationToken ct = default);
}