using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Ventas.Dtos;

namespace Colibri.Api.Features.Ventas;

/// <summary>Turnos de caja. Solo puede haber uno abierto a la vez.</summary>
[Authorize(Policy = Politicas.Caja)]
public class CajaController : ControladorBase
{
    private readonly ICajaService _caja;

    public CajaController(ICajaService caja) => _caja = caja;

    /// <summary>
    /// Turno abierto con su resumen al momento, o null si no hay ninguno.
    /// El punto de venta lo consulta al cargar para saber si puede vender.
    /// </summary>
    [HttpGet("actual")]
    [ProducesResponseType(typeof(ApiResponse<ResumenCajaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actual(CancellationToken ct)
        => Exito(await _caja.ActualAsync(ct));

    [HttpPost("abrir")]
    [ProducesResponseType(typeof(ApiResponse<CajaDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Abrir(
        [FromBody] AbrirCajaRequest peticion, CancellationToken ct)
        => Creado(await _caja.AbrirAsync(peticion, ct), "Caja abierta");

    /// <summary>
    /// Cierra el turno y calcula la diferencia.
    /// </summary>
    /// <remarks>
    /// Solo se informa lo que se contó en el cajón: el efectivo esperado lo
    /// calcula el sistema desde las boletas. Si se pudiera dictar, la
    /// diferencia dejaría de significar algo.
    /// </remarks>
    [HttpPost("cerrar")]
    [ProducesResponseType(typeof(ApiResponse<ResumenCajaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cerrar(
        [FromBody] CerrarCajaRequest peticion, CancellationToken ct)
    {
        var resumen = await _caja.CerrarAsync(peticion, ct);
        var mensaje = resumen.Diferencia switch
        {
            0 => "Caja cerrada y cuadrada",
            > 0 => $"Caja cerrada con sobrante de {resumen.Diferencia:N0}",
            _ => $"Caja cerrada con faltante de {Math.Abs(resumen.Diferencia ?? 0):N0}"
        };
        return Exito(resumen, mensaje);
    }

    [HttpGet("{id:int}/resumen")]
    [ProducesResponseType(typeof(ApiResponse<ResumenCajaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resumen(int id, CancellationToken ct)
        => Exito(await _caja.ResumenAsync(id, ct));

    [HttpGet("historial")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<CajaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial([FromQuery] CajaFiltro filtro, CancellationToken ct)
        => Exito(await _caja.HistorialAsync(filtro, ct));
}