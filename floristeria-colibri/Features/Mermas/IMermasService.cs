using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Mermas.Dtos;

namespace Colibri.Api.Features.Mermas;

public interface IMermasService
{
    Task<ResultadoPagina<MermaDto>> ListarAsync(MermaFiltro filtro, CancellationToken ct = default);
    Task<MermaDto> ObtenerAsync(int id, CancellationToken ct = default);

    Task<MermaDto> RegistrarAsync(RegistrarMermaRequest peticion, CancellationToken ct = default);

    /// <summary>Da de baja el lote completo con lo que le quede.</summary>
    Task<MermaDto> DescartarLoteAsync(int loteId, DescartarLoteRequest peticion, CancellationToken ct = default);

    Task<MermaDto> RevertirAsync(int id, RevertirMermaRequest peticion, CancellationToken ct = default);

    /// <summary>Plan sugerido para desarmar, con los lotes de origen rastreados.</summary>
    Task<PlanDesarmeDto> PlanDesarmeAsync(int productoId, int cantidad, CancellationToken ct = default);

    /// <summary>Desarma unidades armadas y clasifica sus varas una por una.</summary>
    Task<ResultadoDesarmeDto> DesarmarAsync(int productoId, DesarmarRequest peticion, CancellationToken ct = default);

    Task<ResumenMermasDto> ResumenAsync(DateOnly? desde, DateOnly? hasta, CancellationToken ct = default);

    IReadOnlyList<string> MotivosSugeridos();
}