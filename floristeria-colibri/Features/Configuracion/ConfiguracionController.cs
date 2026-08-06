using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Configuracion.Dtos;

namespace Colibri.Api.Features.Configuracion;

/// <summary>
/// Ajustes del negocio: datos del local, ticket, IVA y club de puntos.
///
/// Escribir es exclusivo de administración: acá se define cuánto vale un
/// punto y desde qué monto un descuento necesita autorización.
/// </summary>
[Authorize(Policy = Politicas.Admin)]
public class ConfiguracionController : ControladorBase
{
    private readonly IConfiguracionService _configuracion;

    public ConfiguracionController(IConfiguracionService configuracion)
        => _configuracion = configuracion;

    /// <summary>
    /// Toda la configuración.
    /// </summary>
    /// <remarks>
    /// Lo puede leer cualquier rol: el punto de venta necesita los datos del
    /// local para el ticket y el valor del punto para mostrar el saldo del
    /// cliente en pesos.
    /// </remarks>
    [HttpGet]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ConfiguracionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(CancellationToken ct)
        => Exito(await _configuracion.ObtenerAsync(ct));

    /// <summary>
    /// Datos del local. Aparecen en el encabezado de cada ticket.
    /// </summary>
    /// <remarks>
    /// El RUT se valida con módulo 11: uno mal escrito se imprime cientos de
    /// veces antes de que alguien lo note.
    /// </remarks>
    [HttpPut("local")]
    [ProducesResponseType(typeof(ApiResponse<AjustesLocalDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GuardarLocal(
        [FromBody] AjustesLocalDto peticion, CancellationToken ct)
        => Exito(await _configuracion.GuardarLocalAsync(peticion, ct), "Datos del local guardados");

    /// <summary>
    /// Mensaje y leyenda del ticket.
    /// </summary>
    /// <remarks>
    /// La leyenda no puede quedar vacía: es lo que deja claro que el
    /// documento no es tributario. La boleta real se emite al SII con un
    /// emisor de DTE, que este sistema no tiene.
    /// </remarks>
    [HttpPut("ticket")]
    [ProducesResponseType(typeof(ApiResponse<AjustesTicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GuardarTicket(
        [FromBody] AjustesTicketDto peticion, CancellationToken ct)
        => Exito(await _configuracion.GuardarTicketAsync(peticion, ct), "Ticket actualizado");

    /// <summary>
    /// IVA y umbral de descuento sin autorización.
    /// </summary>
    /// <remarks>
    /// Cambiar el IVA **no** reescribe las boletas ya emitidas: cada venta
    /// guarda la tasa con que se calculó, así el desglose histórico se
    /// mantiene fiel.
    ///
    /// El umbral de descuento decide desde qué monto la venta exige
    /// credenciales de una administradora. Subirlo mucho es el atajo más
    /// rentable para vaciar un punto de venta, así que el cambio queda
    /// registrado.
    /// </remarks>
    [HttpPut("venta")]
    [ProducesResponseType(typeof(ApiResponse<AjustesVentaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GuardarVenta(
        [FromBody] AjustesVentaDto peticion, CancellationToken ct)
        => Exito(await _configuracion.GuardarVentaAsync(peticion, ct), "Ajustes de venta guardados");

    /// <summary>
    /// Club de puntos.
    /// </summary>
    /// <remarks>
    /// Cambiar el valor del punto **revalúa de inmediato todos los saldos
    /// vigentes**. No es ajustar una preferencia: los puntos son un
    /// compromiso con los clientes.
    ///
    /// Si hay puntos en circulación, la primera petición devuelve 400 con el
    /// detalle de cuánto se mueve. Para confirmar, reenvía con
    /// `confirmaRevaluacion: true`.
    /// </remarks>
    [HttpPut("club")]
    [ProducesResponseType(typeof(ApiResponse<AjustesClubDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GuardarClub(
        [FromBody] GuardarClubRequest peticion, CancellationToken ct)
        => Exito(await _configuracion.GuardarClubAsync(peticion, ct), "Club de puntos guardado");

    /// <summary>
    /// Qué mueve cambiar el valor del punto, antes de aplicarlo.
    /// </summary>
    /// <remarks>
    /// Devuelve cuántos puntos hay en circulación, cuánto valen hoy y cuánto
    /// valdrían con el valor propuesto. La interfaz lo muestra junto al campo
    /// para que la decisión se tome con el número a la vista.
    /// </remarks>
    [HttpGet("club/impacto")]
    [ProducesResponseType(typeof(ApiResponse<ImpactoClubDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> ImpactoClub(
        [FromQuery] int valorPunto, CancellationToken ct)
        => Exito(await _configuracion.ImpactoClubAsync(valorPunto, ct));
}