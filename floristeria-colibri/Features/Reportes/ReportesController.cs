using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Reportes.Dtos;

namespace Colibri.Api.Features.Reportes;

/// <summary>
/// Reportes del negocio.
///
/// El resultado y el rendimiento del equipo son de administración: muestran
/// márgenes y diferencias de caja por persona. El panel y el inventario los
/// ve todo el equipo, porque son los que sirven para trabajar.
/// </summary>
[Authorize(Policy = Politicas.Admin)]
public class ReportesController : ControladorBase
{
    private readonly IReportesService _reportes;

    public ReportesController(IReportesService reportes) => _reportes = reportes;

    /// <summary>
    /// Lo que se mira al llegar y antes de cerrar.
    /// </summary>
    /// <remarks>
    /// Trae el resultado del día contra el mismo día de la semana pasada, el
    /// estado de la caja, y las alertas que exigen atención hoy ordenadas por
    /// urgencia.
    ///
    /// Las alertas se limitan a lo accionable: una que nadie puede resolver
    /// hoy solo entrena a la gente a ignorar el panel.
    /// </remarks>
    [HttpGet("panel")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<PanelDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Panel(CancellationToken ct)
        => Exito(await _reportes.PanelAsync(ct));

    /// <summary>
    /// Resultado del período: la cascada desde el bruto hasta la utilidad.
    /// </summary>
    /// <remarks>
    /// El costo es el real de los lotes que salieron, no un promedio teórico.
    /// `resultado` ya descuenta las mermas: es el número del período.
    ///
    /// Incluye la serie diaria para graficar y el rendimiento por día de la
    /// semana, que sirve para decidir turnos.
    ///
    /// Sin fechas toma los últimos 30 días; el máximo es un año.
    /// </remarks>
    [HttpGet("resultado")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPeriodoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resultado(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Exito(await _reportes.ResultadoAsync(desde, hasta, ct));

    /// <summary>
    /// Qué productos rinden y cuáles solo ocupan espacio.
    /// </summary>
    /// <remarks>
    /// El top va ordenado por **utilidad, no por ingresos**: lo que más se
    /// vende no siempre es lo que más deja.
    ///
    /// `sinMovimiento` lista lo que tiene stock y no se ha vendido en el
    /// período, con el dinero inmovilizado en cada uno. Es la pregunta que
    /// nadie se hace hasta que la flor ya se perdió.
    /// </remarks>
    [HttpGet("productos")]
    [ProducesResponseType(typeof(ApiResponse<RendimientoProductosDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Productos(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Exito(await _reportes.ProductosAsync(desde, hasta, ct));

    /// <summary>
    /// Cuánta plata hay dormida en la cámara ahora mismo.
    /// </summary>
    /// <remarks>
    /// Separa el valor sano del que está por vencer y del ya vencido. Ese
    /// segundo número es el que hay que mover esta semana, y el tercero es
    /// pérdida casi segura.
    /// </remarks>
    [HttpGet("inventario")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ValorInventarioDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Inventario(CancellationToken ct)
        => Exito(await _reportes.InventarioAsync(ct));

    /// <summary>
    /// Cómo le va a cada persona en el mesón.
    /// </summary>
    /// <remarks>
    /// Además de lo vendido, trae las **diferencias de caja acumuladas**. Ese
    /// es el dato que más rinde: un turno descuadrado es un error de conteo,
    /// un patrón de faltantes es otra cosa.
    ///
    /// También muestra los descuentos manuales otorgados y las boletas
    /// anuladas.
    /// </remarks>
    [HttpGet("equipo")]
    [ProducesResponseType(typeof(ApiResponse<RendimientoEquipoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Equipo(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Exito(await _reportes.EquipoAsync(desde, hasta, ct));

    /// <summary>
    /// Desglose de un turno de caja.
    /// </summary>
    /// <remarks>
    /// Separa la venta de mostrador de los abonos de eventos, y lista qué
    /// cotizaciones recibieron pagos en el turno.
    ///
    /// La distinción importa: la venta de mostrador ya entregó flor, el abono
    /// es plata recibida por flor que todavía no sale. Al cerrar conviene
    /// saber cuánto de cada cosa entró.
    /// </remarks>
    [HttpGet("turno/{cajaId:int}")]
    [Authorize(Policy = Politicas.Caja)]
    [ProducesResponseType(typeof(ApiResponse<DesgloseTurnoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Turno(int cajaId, CancellationToken ct)
        => Exito(await _reportes.TurnoAsync(cajaId, ct));
}