using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Lotes.Dtos;

namespace Colibri.Api.Features.Lotes;

/// <summary>
/// Lotes: el paquete físico que se recibe, se etiqueta con QR y se consume.
/// Consultar y escanear lo puede hacer cualquier rol; mover un lote de
/// ubicación, solo Admin y Bodega.
/// </summary>
[Authorize(Policy = Politicas.VerInventario)]
public class LotesController : ControladorBase
{
    private readonly ILotesService _lotes;

    public LotesController(ILotesService lotes) => _lotes = lotes;

    /// <summary>
    /// Lotes con existencias.
    /// </summary>
    /// <remarks>
    /// `ordenFifo` indica la posición en la fila de consumo: el 1 es el que
    /// debería venderse ahora. `alerta` marca los vencidos, los que están por
    /// vencer y los restos que conviene liquidar.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<LoteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] LoteFiltro filtro, CancellationToken ct)
        => Exito(await _lotes.ListarActivosAsync(filtro, ct));

    /// <summary>Todos los lotes, incluidos los agotados y descartados.</summary>
    [HttpGet("historial")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<LoteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Historial(
        [FromQuery] HistorialLoteFiltro filtro, CancellationToken ct)
        => Exito(await _lotes.ListarHistorialAsync(filtro, ct));

    /// <summary>Ficha del lote con su procedencia y su libro de movimientos.</summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<LoteDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _lotes.ObtenerAsync(id, ct));

    /// <summary>
    /// Ficha por código. Es lo que abre la URL del QR, y también lo que sirve
    /// si alguien tipea el código a mano porque la etiqueta se borró.
    /// </summary>
    [HttpGet("codigo/{codigo}")]
    [ProducesResponseType(typeof(ApiResponse<LoteDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> PorCodigo(string codigo, CancellationToken ct)
        => Exito(await _lotes.ObtenerPorCodigoAsync(codigo, ct));

    /// <summary>
    /// Respuesta del escaneo del QR en el punto de venta.
    /// </summary>
    /// <remarks>
    /// Devuelve el producto, cuánto queda en ese lote y las advertencias:
    /// si está vencido, si no alcanza, y sobre todo si queda un lote más
    /// antiguo abierto —el caso de las varas que sobraron del paquete
    /// anterior y conviene vender primero.
    ///
    /// Es informativo. La validación que manda ocurre al cobrar: ahí las
    /// filas se bloquean, y si otra caja se llevó el stock entremedio, la
    /// venta falla en ese momento.
    /// </remarks>
    [HttpPost("validar")]
    [ProducesResponseType(typeof(ApiResponse<ValidacionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Validar(
        [FromBody] ValidarLoteRequest peticion, CancellationToken ct)
    {
        var validacion = await _lotes.ValidarAsync(peticion, ct);
        return Exito(validacion, validacion.Advertencia);
    }

    /// <summary>
    /// Restos rezagados: lotes viejos que quedaron atrás porque se empezó a
    /// vender de uno nuevo. Si no se liquidan, terminan en merma.
    /// </summary>
    [HttpGet("rezagados")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LoteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Rezagados(CancellationToken ct)
        => Exito(await _lotes.RezagadosAsync(ct));

    /// <summary>Lotes que vencen dentro de los próximos días.</summary>
    [HttpGet("por-vencer")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LoteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PorVencer([FromQuery] int dias = 3, CancellationToken ct = default)
        => Exito(await _lotes.PorVencerAsync(dias, ct));

    /// <summary>
    /// Flor recuperada disponible para vender.
    /// </summary>
    /// <remarks>
    /// El balde aparte: lo que volvió del desarme de un ramo o de un pedido
    /// que el cliente pagó y no usó. Cada lote trae su precio rebajado y la
    /// diferencia contra el de lista.
    ///
    /// Estos lotes no entran en el reparto automático: hay que escanearlos.
    /// </remarks>
    [HttpGet("recuperados")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<LoteRecuperadoDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Recuperados(CancellationToken ct)
        => Exito(await _lotes.RecuperadosAsync(ct));

    /// <summary>
    /// Costo promedio ponderado de lo que hay en cámara, por producto, y el
    /// dinero inmovilizado en cada uno.
    /// </summary>
    [HttpGet("costo-promedio")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CostoPromedioDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> CostoPromedio(CancellationToken ct)
        => Exito(await _lotes.CostoPromedioAsync(ct));

    /// <summary>
    /// Registra dónde está el paquete. Es lo único editable de un lote: las
    /// varas se mueven recibiendo, vendiendo o mermando.
    /// </summary>
    [HttpPatch("{id:int}/ubicacion")]
    [Authorize(Policy = Politicas.Inventario)]
    [ProducesResponseType(typeof(ApiResponse<LoteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Ubicacion(
        int id, [FromBody] ActualizarUbicacionRequest peticion, CancellationToken ct)
        => Exito(await _lotes.ActualizarUbicacionAsync(id, peticion, ct), "Ubicación registrada");

    /* ---------------- Etiquetas ---------------- */

    /// <summary>Datos para imprimir etiquetas de los lotes indicados.</summary>
    [HttpGet("etiquetas")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EtiquetaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Etiquetas([FromQuery] int[] ids, CancellationToken ct)
        => Exito(await _lotes.EtiquetasAsync(ids, ct));

    /// <summary>
    /// Etiquetas de todos los lotes de una compra. Es el flujo real: se
    /// imprimen al recibir y se pegan antes de meter los paquetes a la cámara.
    /// </summary>
    [HttpGet("etiquetas/compra/{compraId:int}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EtiquetaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EtiquetasDeCompra(int compraId, CancellationToken ct)
        => Exito(await _lotes.EtiquetasDeCompraAsync(compraId, ct));

    /// <summary>
    /// Imagen PNG del código QR.
    /// </summary>
    /// <remarks>
    /// Contiene la URL que abre la ficha del lote. En el papel no viaja
    /// ningún dato: si la etiqueta se pierde o se moja, se reimprime y listo.
    ///
    /// Devuelve la imagen directamente, no un ApiResponse: va en el src de
    /// un &lt;img&gt;.
    /// </remarks>
    [HttpGet("{codigo}/qr")]
    [Produces("image/png")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Qr(string codigo, CancellationToken ct)
    {
        var png = await _lotes.GenerarQrAsync(codigo, ct);
        return File(png, "image/png", $"{codigo}.png");
    }
}