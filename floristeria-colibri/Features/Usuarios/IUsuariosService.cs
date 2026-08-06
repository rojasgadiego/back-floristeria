using Colibri.Api.Common.Paginacion;
using Colibri.Api.Features.Usuarios.Dtos;

namespace Colibri.Api.Features.Usuarios;

public interface IUsuariosService
{
    Task<ResultadoPagina<UsuarioDto>> ListarAsync(UsuarioFiltro filtro, CancellationToken ct = default);
    Task<UsuarioDto> ObtenerAsync(int id, CancellationToken ct = default);
    Task<UsuarioDto> CrearAsync(CrearUsuarioRequest peticion, CancellationToken ct = default);
    Task<UsuarioDto> ActualizarAsync(int id, ActualizarUsuarioRequest peticion, CancellationToken ct = default);
    Task<UsuarioDto> CambiarRolAsync(int id, CambiarRolRequest peticion, CancellationToken ct = default);
    Task<UsuarioDto> CambiarEstadoAsync(int id, bool activo, CancellationToken ct = default);
    Task RestablecerPasswordAsync(int id, RestablecerPasswordRequest peticion, CancellationToken ct = default);
}
