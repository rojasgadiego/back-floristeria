using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Compras.Dtos;

namespace Colibri.Api.Features.Compras;

[Authorize(Policy = Politicas.Inventario)]
public class ProveedoresController : ControladorBase
{
    private readonly IComprasService _compras;

    public ProveedoresController(IComprasService compras) => _compras = compras;

    /// <summary>Lista con lo comprado a cada uno y la fecha del último pedido.</summary>
    [HttpGet]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<ProveedorDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] ProveedorFiltro filtro, CancellationToken ct)
        => Exito(await _compras.ListarProveedoresAsync(filtro, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ProveedorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _compras.ObtenerProveedorAsync(id, ct));

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ProveedorDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(
        [FromBody] GuardarProveedorRequest peticion, CancellationToken ct)
        => Creado(await _compras.CrearProveedorAsync(peticion, ct));

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ProveedorDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarProveedorRequest peticion, CancellationToken ct)
        => Exito(await _compras.ActualizarProveedorAsync(id, peticion, ct));

    /// <summary>
    /// Desactiva el proveedor. No se elimina: las compras históricas y los
    /// lotes lo referencian.
    /// </summary>
    [HttpPatch("{id:int}/desactivar")]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
        => Exito(await _compras.CambiarEstadoProveedorAsync(id, false, ct), "Proveedor desactivado");

    [HttpPatch("{id:int}/activar")]
    public async Task<IActionResult> Activar(int id, CancellationToken ct)
        => Exito(await _compras.CambiarEstadoProveedorAsync(id, true, ct), "Proveedor activado");
}