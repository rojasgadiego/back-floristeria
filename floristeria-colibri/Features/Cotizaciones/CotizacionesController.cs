using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Cotizaciones.Dtos;

namespace Colibri.Api.Features.Cotizaciones;

/// <summary>
/// Presupuestos de eventos, sus abonos y su plan de pago.
///
/// Cada abono registra una venta real: entra por caja, tiene folio y medio de
/// pago. Así el arqueo del día nunca muestra plata que el sistema no explica.
/// </summary>
[Authorize(Policy = Politicas.Caja)]
public class CotizacionesController : ControladorBase
{
    private readonly ICotizacionesService _cotizaciones;

    public CotizacionesController(ICotizacionesService cotizaciones)
        => _cotizaciones = cotizaciones;

    /// <summary>
    /// Lista con su estado de cobro.
    /// </summary>
    /// <remarks>
    /// `estadoPago` resume la situación en una frase —"al día", "debe
    /// $100.000 desde el 15-08"— que es lo que se lee de un vistazo para
    /// decidir a quién llamar.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<CotizacionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] CotizacionFiltro filtro, CancellationToken ct)
        => Exito(await _cotizaciones.ListarAsync(filtro, ct));

    /// <summary>
    /// Ficha completa: líneas, pagos, plan de cuotas y flor faltante.
    /// </summary>
    /// <remarks>
    /// Si el evento ya se cobró, incluye el resultado juntando las dos
    /// boletas —los abonos y el cobro final—, porque ninguna de las dos,
    /// mirada sola, dice la verdad.
    /// </remarks>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CotizacionDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _cotizaciones.ObtenerAsync(id, ct));

    /// <summary>
    /// Crea el presupuesto en borrador.
    /// </summary>
    /// <remarks>
    /// El precio de un producto de catálogo sale de la base, no de la
    /// pantalla: un presupuesto con precios inventados es una promesa que
    /// después no se puede cumplir.
    ///
    /// Las líneas `aMedida` —un arco floral, por ejemplo— llevan nombre y
    /// precio propios y no tocan inventario.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CotizacionDetalleDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(
        [FromBody] GuardarCotizacionRequest peticion, CancellationToken ct)
    {
        var cotizacion = await _cotizaciones.CrearAsync(peticion, ct);
        return Creado(cotizacion, $"{cotizacion.Folio} creada · {cotizacion.Total:N0}");
    }

    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CotizacionDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarCotizacionRequest peticion, CancellationToken ct)
        => Exito(await _cotizaciones.ActualizarAsync(id, peticion, ct));

    /// <summary>
    /// Aprueba el presupuesto.
    /// </summary>
    /// <remarks>
    /// Aprobar NO reserva inventario: la flor de un matrimonio en tres
    /// semanas todavía no se compra. Lo que sí hace es que el evento aparezca
    /// en la agenda y en el compromiso de stock.
    /// </remarks>
    [HttpPatch("{id:int}/aprobar")]
    [ProducesResponseType(typeof(ApiResponse<CotizacionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Aprobar(int id, CancellationToken ct)
        => Exito(await _cotizaciones.AprobarAsync(id, ct), "Presupuesto aprobado");

    /// <summary>
    /// Anula el presupuesto.
    /// </summary>
    /// <remarks>
    /// No toca los abonos ya recibidos: esa plata entró en turnos que ya se
    /// cerraron, y borrarla descuadraría arqueos firmados. Queda como saldo a
    /// favor del cliente. Si hay que devolverla, se anulan esas boletas una
    /// por una desde Ventas.
    /// </remarks>
    [HttpPost("{id:int}/anular")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<CotizacionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Anular(
        int id, [FromBody] AnularCotizacionRequest peticion, CancellationToken ct)
        => Exito(await _cotizaciones.AnularAsync(id, peticion, ct), "Presupuesto anulado");

    /* ---------------- Pagos ---------------- */

    /// <summary>
    /// Registra un abono.
    /// </summary>
    /// <remarks>
    /// Genera una venta con una línea de servicio, que entra a la caja
    /// abierta con su folio y su medio de pago. Un mismo evento puede tener
    /// el abono en efectivo y las cuotas con crédito: el medio va en cada
    /// pago, no en la cotización.
    ///
    /// Necesita una caja abierta, igual que cualquier venta.
    /// </remarks>
    [HttpPost("{id:int}/pagos")]
    [ProducesResponseType(typeof(ApiResponse<PagoDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> RegistrarPago(
        int id, [FromBody] RegistrarPagoRequest peticion, CancellationToken ct)
    {
        var pago = await _cotizaciones.RegistrarPagoAsync(id, peticion, ct);
        return Creado(pago, $"Abono de {pago.Monto:N0} · boleta {pago.VentaFolio}");
    }

    /// <summary>
    /// Anula un abono y su boleta.
    /// </summary>
    /// <remarks>
    /// Las dos cosas van juntas: si la plata se devuelve, la venta deja de
    /// existir y el arqueo de ese día cambia. Anular solo el registro dejaría
    /// dinero en la caja sin dueño.
    /// </remarks>
    [HttpPost("{id:int}/pagos/{pagoId:int}/anular")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<PagoDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> AnularPago(
        int id, int pagoId, [FromBody] AnularPagoRequest peticion, CancellationToken ct)
        => Exito(await _cotizaciones.AnularPagoAsync(id, pagoId, peticion, ct), "Abono anulado");

    /* ---------------- Cuotas ---------------- */

    /// <summary>
    /// Define el plan de pago.
    /// </summary>
    /// <remarks>
    /// Las cuotas deben sumar exactamente el saldo pendiente: un plan
    /// incompleto significa que el último pago va a ser una sorpresa.
    ///
    /// No se marcan como pagadas una por una a propósito. En un acuerdo de
    /// palabra el cliente paga 120 en vez de 100, junta dos o se atrasa. Lo
    /// que el sistema compara es el total abonado contra lo que ya venció.
    ///
    /// Una lista vacía borra el plan: sin cuotas, el saldo completo vence el
    /// día del evento.
    /// </remarks>
    [HttpPut("{id:int}/cuotas")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CuotaDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GuardarCuotas(
        int id, [FromBody] GuardarCuotasRequest peticion, CancellationToken ct)
        => Exito(await _cotizaciones.GuardarCuotasAsync(id, peticion, ct), "Plan de pago guardado");

    /// <summary>
    /// Genera el plan repartiendo el saldo en cuotas iguales.
    /// </summary>
    /// <remarks>
    /// La diferencia por redondeo va en la primera cuota: es mejor cobrar el
    /// peso de más al principio que descubrirlo al final.
    /// </remarks>
    [HttpPost("{id:int}/cuotas/generar")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CuotaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GenerarCuotas(
        int id, [FromBody] GenerarCuotasRequest peticion, CancellationToken ct)
    {
        var cuotas = await _cotizaciones.GenerarCuotasAsync(id, peticion, ct);
        return Exito(cuotas, $"{cuotas.Count} cuota(s) generada(s)");
    }

    /* ---------------- Cobro ---------------- */

    /// <summary>
    /// Prepara la boleta final.
    /// </summary>
    /// <remarks>
    /// Devuelve las líneas sugeridas con su disponibilidad, el abono ya
    /// recibido y el saldo a cobrar. **No cobra**: para cerrar el evento hay
    /// que enviar esas líneas —ajustadas si hace falta— a `POST /api/ventas`
    /// con `cotizacionId`.
    ///
    /// La sugerencia es editable a propósito: si el arco quedó chico y se
    /// usaron 72 rosas en vez de 60, hay que cobrar y descontar lo que
    /// realmente salió. Cobrar a ciegas lo cotizado dejaría el inventario
    /// diciendo que salieron 60.
    /// </remarks>
    [HttpGet("{id:int}/preparar-cobro")]
    [ProducesResponseType(typeof(ApiResponse<PreparacionCobroDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> PrepararCobro(int id, CancellationToken ct)
    {
        var preparacion = await _cotizaciones.PrepararCobroAsync(id, ct);

        var mensaje = preparacion.Advertencias.Count > 0
            ? $"A cobrar {preparacion.SaldoACobrar:N0} · " +
              $"{preparacion.Advertencias.Count} punto(s) a revisar"
            : $"A cobrar {preparacion.SaldoACobrar:N0}";

        return Exito(preparacion, mensaje);
    }

    /* ---------------- Seguimiento ---------------- */

    /// <summary>
    /// Eventos con saldo vencido, del más atrasado al menos.
    /// Es la lista de a quiénes hay que llamar.
    /// </summary>
    [HttpGet("por-cobrar")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CotizacionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PorCobrar(CancellationToken ct)
        => Exito(await _cotizaciones.PorCobrarAsync(ct));

    /// <summary>
    /// Eventos aprobados de los próximos días.
    /// </summary>
    /// <remarks>
    /// Es lo que se mira para decidir cuánta flor comprar esta semana.
    /// </remarks>
    [HttpGet("agenda")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CotizacionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Agenda(
        [FromQuery] int dias = 30, CancellationToken ct = default)
        => Exito(await _cotizaciones.AgendaAsync(dias, ct));
}