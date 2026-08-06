using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Usuarios.Dtos;

namespace Colibri.Api.Features.Usuarios;

/// <summary>
/// Gestión de cuentas. Todo el módulo es exclusivo de administración: quien
/// puede crear cuentas puede darse a sí mismo cualquier permiso del sistema.
/// </summary>
[Authorize(Policy = Politicas.Admin)]
public class UsuariosController : ControladorBase
{
    private readonly IUsuariosService _usuarios;

    public UsuariosController(IUsuariosService usuarios) => _usuarios = usuarios;

    /// <summary>Lista las cuentas con sus ventas acumuladas.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<UsuarioDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] UsuarioFiltro filtro, CancellationToken ct)
        => Exito(await _usuarios.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _usuarios.ObtenerAsync(id, ct));

    /// <summary>Crea una cuenta. El correo es el identificador de acceso.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(
        [FromBody] CrearUsuarioRequest peticion, CancellationToken ct)
    {
        var usuario = await _usuarios.CrearAsync(peticion, ct);
        return Creado(usuario, $"Cuenta de {usuario.Nombre} creada");
    }

    /// <summary>Actualiza nombre y correo. El rol se cambia por su propio endpoint.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] ActualizarUsuarioRequest peticion, CancellationToken ct)
        => Exito(await _usuarios.ActualizarAsync(id, peticion, ct));

    /// <summary>
    /// Cambia el rol.
    /// </summary>
    /// <remarks>
    /// La base impide dejar el sistema sin administradoras activas: si esta
    /// es la última, la operación se rechaza.
    /// </remarks>
    [HttpPatch("{id:int}/rol")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> CambiarRol(
        int id, [FromBody] CambiarRolRequest peticion, CancellationToken ct)
        => Exito(await _usuarios.CambiarRolAsync(id, peticion, ct));

    /// <summary>
    /// Bloquea la cuenta.
    /// </summary>
    /// <remarks>
    /// Impide iniciar sesión de nuevo. Una sesión ya abierta sigue siendo
    /// válida hasta que expire su token: se corta cuando el front consulta
    /// /api/auth/me.
    /// </remarks>
    [HttpPatch("{id:int}/bloquear")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Bloquear(int id, CancellationToken ct)
        => Exito(await _usuarios.CambiarEstadoAsync(id, false, ct), "Cuenta bloqueada");

    [HttpPatch("{id:int}/reactivar")]
    [ProducesResponseType(typeof(ApiResponse<UsuarioDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivar(int id, CancellationToken ct)
        => Exito(await _usuarios.CambiarEstadoAsync(id, true, ct), "Cuenta reactivada");

    /// <summary>
    /// Restablece la contraseña sin pedir la anterior. Para cuando alguien
    /// la olvidó.
    /// </summary>
    [HttpPost("{id:int}/restablecer-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RestablecerPassword(
        int id, [FromBody] RestablecerPasswordRequest peticion, CancellationToken ct)
    {
        await _usuarios.RestablecerPasswordAsync(id, peticion, ct);
        return SinContenido();
    }
}
