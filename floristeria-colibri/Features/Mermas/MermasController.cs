using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Mermas.Dtos;

namespace Colibri.Api.Features.Mermas;

/// <summary>
/// Salidas de inventario. Registrar es cosa de Admin y Bodega; revertir,
/// solo de administración, porque deshace un registro de pérdida.
/// </summary>
[Authorize(Policy = Politicas.Inventario)]
public class MermasController : ControladorBase
{
    private readonly IMermasService _mermas;

    public MermasController(IMermasService mermas) => _mermas = mermas;

    [HttpGet]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<MermaDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] MermaFiltro filtro, CancellationToken ct)
        => Exito(await _mermas.ListarAsync(filtro, ct));

    [HttpGet("{id:int}")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<MermaDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _mermas.ObtenerAsync(id, ct));

    /// <summary>
    /// Registra una salida de inventario.
    /// </summary>
    /// <remarks>
    /// `destino` decide qué pasó con lo que salió:
    ///
    /// · **perdida** — se botó: es costo.
    /// · **reingreso** — vuelve al stock. Con `cantidadRecuperada` y
    ///   `calidad`. Óptima regresa a su lote original; buena y limitada van
    ///   a un lote de recuperación con `precioRecuperado` propio, que queda
    ///   fuera del reparto automático y solo se vende escaneándolo.
    /// · **devolucion_proveedor** — sale del stock pero NO es costo: se abona.
    ///
    /// Si el producto se controla por lote, `loteId` es obligatorio: una flor
    /// pertenece a un lote concreto, con su costo y su procedencia.
    ///
    /// El costo se congela al registrar: si el proveedor sube el precio la
    /// próxima semana, la pérdida de hoy sigue valiendo lo que valía hoy.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<MermaDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Registrar(
        [FromBody] RegistrarMermaRequest peticion, CancellationToken ct)
    {
        var merma = await _mermas.RegistrarAsync(peticion, ct);
        var mensaje = merma.CantidadRecuperada > 0
            ? $"{merma.CantidadRecuperada} unidad(es) recuperada(s) · " +
              $"pérdida real de {merma.CostoPerdido:N0}"
            : $"Registrado · pérdida de {merma.CostoPerdido:N0}";
        return Creado(merma, mensaje);
    }

    /// <summary>
    /// Da de baja el lote completo con lo que le quede.
    /// </summary>
    /// <remarks>
    /// Es el destino de los rezagados: el resto que no se alcanzó a liquidar
    /// y ya no sirve. El lote queda en estado descartado y sale del stock.
    ///
    /// Con `esDevolucionProveedor` en true, la mercadería sale pero no cuenta
    /// como costo: se abona.
    /// </remarks>
    [HttpPost("lote/{loteId:int}/descartar")]
    [ProducesResponseType(typeof(ApiResponse<MermaDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> DescartarLote(
        int loteId, [FromBody] DescartarLoteRequest peticion, CancellationToken ct)
    {
        var merma = await _mermas.DescartarLoteAsync(loteId, peticion, ct);
        return Creado(merma,
            $"Lote {merma.LoteCodigo} descartado · pérdida de {merma.CostoPerdido:N0}");
    }

    /// <summary>
    /// Revierte una merma registrada por error.
    /// </summary>
    /// <remarks>
    /// Las varas vuelven al lote del que salieron, conservando su costo y su
    /// vencimiento. Si el lote se había descartado, se reactiva.
    ///
    /// Si hubo reingreso y esas varas ya se vendieron, la reversa se rechaza:
    /// no hay forma de deshacerla sin inventar stock.
    /// </remarks>
    [HttpPost("{id:int}/revertir")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<MermaDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Revertir(
        int id, [FromBody] RevertirMermaRequest peticion, CancellationToken ct)
        => Exito(await _mermas.RevertirAsync(id, peticion, ct), "Merma revertida");

    /* ---------------- Desarme ---------------- */

    /// <summary>
    /// Plan sugerido para desarmar.
    /// </summary>
    /// <remarks>
    /// Devuelve cuántas varas de cada componente salen y de qué lote
    /// vinieron, rastreado desde los movimientos del armado. La interfaz lo
    /// muestra pre-llenado y la persona solo mueve lo que corresponda.
    ///
    /// `diasEnCamara` cuenta desde que la flor llegó al local, no desde que
    /// se armó el ramo: esa es su edad real.
    /// </remarks>
    [HttpGet("desarme/{productoId:int}/plan")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<PlanDesarmeDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PlanDesarme(
        int productoId, [FromQuery] int cantidad = 1, CancellationToken ct = default)
        => Exito(await _mermas.PlanDesarmeAsync(productoId, cantidad, ct));

    /// <summary>
    /// Desarma unidades armadas y clasifica sus varas.
    /// </summary>
    /// <remarks>
    /// La clasificación es por vara y no por conjunto: del mismo ramo pueden
    /// salir ocho rosas óptimas que vuelven a su balde y cuatro buenas que
    /// van a uno aparte con precio rebajado. Por eso se envían varias líneas
    /// por componente.
    ///
    /// Las cantidades de cada componente deben sumar exactamente lo que dice
    /// la receta: cada vara tiene que tener un destino.
    /// </remarks>
    [HttpPost("desarme/{productoId:int}")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoDesarmeDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Desarmar(
        int productoId, [FromBody] DesarmarRequest peticion, CancellationToken ct)
    {
        var resultado = await _mermas.DesarmarAsync(productoId, peticion, ct);
        return Exito(resultado,
            $"{resultado.Desarmadas} unidad(es) desarmada(s) · " +
            $"{resultado.VarasRecuperadas} varas recuperadas, {resultado.VarasPerdidas} perdidas");
    }

    /* ---------------- Reporte ---------------- */

    /// <summary>
    /// Cuánto se perdió en el período, por destino, producto y motivo.
    /// </summary>
    /// <remarks>
    /// Separa lo que se perdió de lo que volvió al inventario. Sin esa
    /// distinción, un pedido devuelto en perfecto estado aparecería como
    /// pérdida total y el porcentaje del mes dejaría de servir para decidir
    /// cuánto comprar.
    ///
    /// Por producto incluye el porcentaje sobre lo comprado en el mismo
    /// período: sin ese denominador, "se perdieron 40 rosas" no dice si es
    /// mucho o poco.
    ///
    /// Sin fechas, toma los últimos 30 días.
    /// </remarks>
    [HttpGet("resumen")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ResumenMermasDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Resumen(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
        => Exito(await _mermas.ResumenAsync(desde, hasta, ct));

    /// <summary>
    /// Motivos habituales, para que la interfaz los ofrezca en vez de dejar
    /// el campo libre: "marchita", "Marchita" y "se marchitó" serían tres
    /// categorías distintas en el reporte.
    /// </summary>
    [HttpGet("motivos")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<string>>), StatusCodes.Status200OK)]
    public IActionResult Motivos() => Exito(_mermas.MotivosSugeridos());
}