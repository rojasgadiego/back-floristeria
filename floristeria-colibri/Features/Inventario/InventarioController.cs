using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Inventario.Dtos;

namespace Colibri.Api.Features.Inventario;

/// <summary>Consultas transversales del inventario: kardex, alertas y categorías.</summary>
[Authorize(Policy = Politicas.VerInventario)]
public class InventarioController : ControladorBase
{
    private readonly IInventarioService _inventario;

    public InventarioController(IInventarioService inventario) => _inventario = inventario;

    /// <summary>
    /// Libro mayor del inventario: toda entrada y salida con su motivo y su
    /// responsable. Es lo que permite reconstruir por qué faltan seis rosas.
    /// </summary>
    [HttpGet("movimientos")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<MovimientoDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Movimientos(
        [FromQuery] MovimientoFiltro filtro, CancellationToken ct)
        => Exito(await _inventario.ListarMovimientosAsync(filtro, ct));

    /// <summary>Productos que llegaron a su stock mínimo.</summary>
    [HttpGet("bajo-minimo")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ProductoDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> BajoMinimo(CancellationToken ct)
        => Exito(await _inventario.BajoMinimoAsync(ct));

    [HttpGet("categorias")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CategoriaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Categorias(CancellationToken ct)
        => Exito(await _inventario.ListarCategoriasAsync(ct));

    [HttpPost("categorias")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<CategoriaDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> CrearCategoria(
        [FromBody] CrearCategoriaRequest peticion, CancellationToken ct)
        => Creado(await _inventario.CrearCategoriaAsync(peticion, ct));
}