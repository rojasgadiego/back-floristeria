using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Ventas.Dtos;

namespace Colibri.Api.Features.Ventas;

[Authorize(Policy = Politicas.Caja)]
public class VentasController : ControladorBase
{
    private readonly IVentasService _ventas;

    public VentasController(IVentasService ventas) => _ventas = ventas;

    /// <summary>
    /// Cobra la boleta.
    /// </summary>
    /// <remarks>
    /// El servidor no acepta montos del cliente: los precios se leen de la
    /// base, la promoción se recalcula con sus reglas y el total se arma de
    /// cero. Lo que la pantalla envía son productos, cantidades y el lote
    /// escaneado, nada más.
    ///
    /// Si una línea trae `loteId`, ese lote se consume primero y el resto se
    /// completa por antigüedad.
    ///
    /// Un descuento manual sobre el umbral configurado exige las credenciales
    /// de una administradora en `autorizacion`.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<VentaDetalleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Registrar(
        [FromBody] RegistrarVentaRequest peticion, CancellationToken ct)
    {
        var venta = await _ventas.RegistrarAsync(peticion, ct);
        return Creado(venta, $"Boleta {venta.Folio} · {venta.Total:N0}");
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<VentaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] VentaFiltro filtro, CancellationToken ct)
        => Exito(await _ventas.ListarAsync(filtro, ct));

    /// <summary>
    /// Detalle con el ticket y el plan de consumo: qué salió del inventario,
    /// de qué lote y a qué costo real.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<VentaDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _ventas.ObtenerAsync(id, ct));

    /// <summary>Todo lo que necesita la impresora del mesón.</summary>
    [HttpGet("{id:int}/ticket")]
    [ProducesResponseType(typeof(ApiResponse<TicketDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ticket(int id, CancellationToken ct)
        => Exito(await _ventas.TicketAsync(id, ct));

    /// <summary>
    /// Promociones que aplican al carrito, ordenadas por conveniencia.
    /// </summary>
    /// <remarks>
    /// Devuelve el descuento ya calculado para ESTE carrito, no la regla en
    /// abstracto. Al cobrar se vuelve a calcular: lo que se muestre acá no
    /// compromete el monto final.
    /// </remarks>
    [HttpPost("promociones-aplicables")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PromocionAplicableDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PromocionesAplicables(
        [FromBody] List<LineaVentaRequest> items, CancellationToken ct)
        => Exito(await _ventas.PromocionesAplicablesAsync(items, ct));

    /// <summary>
    /// Anula la boleta y devuelve al inventario exactamente lo que sacó.
    /// </summary>
    /// <remarks>
    /// Es posible porque la venta guardó su plan de consumo. Las varas
    /// vuelven al lote del que salieron, conservando su costo y su
    /// vencimiento.
    ///
    /// Exclusivo de administración: anular una boleta mueve dinero y stock.
    /// </remarks>
    [HttpPost("{id:int}/anular")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<VentaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Anular(
        int id, [FromBody] AnularVentaRequest peticion, CancellationToken ct)
        => Exito(await _ventas.AnularAsync(id, peticion, ct), "Boleta anulada");
}