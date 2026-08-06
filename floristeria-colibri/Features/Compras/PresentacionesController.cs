using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Compras.Dtos;

namespace Colibri.Api.Features.Compras;

/// <summary>
/// Cómo llega cada flor del proveedor. La equivalencia en varas depende de la
/// especie —25 por paquete en rosas, 10 en maule—, así que vive por producto
/// y no en una constante del sistema.
/// </summary>
[Authorize(Policy = Politicas.Inventario)]
public class PresentacionesController : ControladorBase
{
    private readonly IComprasService _compras;

    public PresentacionesController(IComprasService compras) => _compras = compras;

    [HttpGet("producto/{productoId:int}")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PresentacionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar(int productoId, CancellationToken ct)
        => Exito(await _compras.ListarPresentacionesAsync(productoId, ct));

    [HttpPost("producto/{productoId:int}")]
    [ProducesResponseType(typeof(ApiResponse<PresentacionDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(
        int productoId, [FromBody] GuardarPresentacionRequest peticion, CancellationToken ct)
        => Creado(await _compras.CrearPresentacionAsync(productoId, peticion, ct));

    /// <summary>
    /// Edita la presentación. Si ya se usó en una compra, su equivalencia en
    /// varas queda congelada: cambiarla recalcularía mal los lotes que ya
    /// ingresaron.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PresentacionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarPresentacionRequest peticion, CancellationToken ct)
        => Exito(await _compras.ActualizarPresentacionAsync(id, peticion, ct));

    /// <summary>Si ya se usó en una compra, se desactiva en vez de borrarse.</summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await _compras.EliminarPresentacionAsync(id, ct);
        return SinContenido();
    }
}