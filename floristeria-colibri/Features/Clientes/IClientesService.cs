using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Clientes.Dtos;

namespace Colibri.Api.Features.Clientes;

public interface IClientesService
{
    Task<ResultadoPagina<ClienteDto>> ListarAsync(ClienteFiltro filtro, CancellationToken ct = default);
    Task<ClienteDetalleDto> ObtenerAsync(int id, CancellationToken ct = default);

    /// <summary>Búsqueda por RUT para el punto de venta. Acepta cualquier formato.</summary>
    Task<ClienteDto?> BuscarPorRutAsync(string rut, CancellationToken ct = default);

    Task<ClienteDto> CrearAsync(GuardarClienteRequest peticion, CancellationToken ct = default);
    Task<ClienteDto> ActualizarAsync(int id, GuardarClienteRequest peticion, CancellationToken ct = default);
    Task<ClienteDto> CambiarEstadoAsync(int id, bool activo, CancellationToken ct = default);

    /// <summary>Regala o descuenta puntos a mano, con motivo.</summary>
    Task<ClienteDto> AjustarPuntosAsync(int id, AjustarPuntosRequest peticion, CancellationToken ct = default);

    Task<ResultadoPagina<CompraClienteDto>> HistorialComprasAsync(
        int id, ParametrosPagina parametros, CancellationToken ct = default);

    Task<ResultadoPagina<MovimientoPuntosDto>> MovimientosPuntosAsync(
        int id, ParametrosPagina parametros, CancellationToken ct = default);

    /// <summary>Quiénes cumplen años este mes, para la campaña.</summary>
    Task<IReadOnlyList<CumpleanosDto>> CumpleanosDelMesAsync(short? mes, CancellationToken ct = default);
}