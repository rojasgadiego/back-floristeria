using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Compras.Dtos;

namespace Colibri.Api.Features.Compras;

/// <summary>
/// Órdenes de compra y su recepción. Es la única puerta por la que entra
/// stock de un producto controlado por lote.
/// </summary>
[Authorize(Policy = Politicas.Inventario)]
public class ComprasController : ControladorBase
{
    private readonly IComprasService _compras;

    public ComprasController(IComprasService compras) => _compras = compras;

    [HttpGet]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<CompraDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] CompraFiltro filtro, CancellationToken ct)
        => Exito(await _compras.ListarAsync(filtro, ct));

    /// <summary>
    /// Detalle con sus líneas y, si ya se recibió, los lotes que generó.
    /// Cada línea trae el costo por vara de la compra anterior del mismo
    /// producto, para ver de inmediato si el proveedor subió el precio.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<CompraDetalleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _compras.ObtenerAsync(id, ct));

    /// <summary>
    /// Crea la orden en borrador.
    /// </summary>
    /// <remarks>
    /// El costo se indica por presentación —lo que cuesta UNA caja o UN
    /// paquete—, no por vara. El sistema calcula las varas y reparte el costo
    /// entre ellas, y ese costo por vara es el que después valoriza cada
    /// tallo consumido al armar o al vender.
    ///
    /// Crear no mueve stock: eso ocurre al recibir.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CompraDetalleDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Crear(
        [FromBody] GuardarCompraRequest peticion, CancellationToken ct)
    {
        var compra = await _compras.CrearAsync(peticion, ct);
        return Creado(compra, $"Compra {compra.Folio} creada en borrador");
    }

    /// <summary>Edita el borrador. Una compra recibida ya no se toca.</summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<CompraDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarCompraRequest peticion, CancellationToken ct)
        => Exito(await _compras.ActualizarAsync(id, peticion, ct));

    /// <summary>
    /// Recibe la mercadería y genera los lotes.
    /// </summary>
    /// <remarks>
    /// Cada línea se convierte en un lote con su código QR, su vencimiento
    /// —calculado desde los días de vida del producto— y su costo por vara.
    /// El stock del producto se sincroniza solo: lo mantiene un trigger
    /// sumando los lotes activos.
    ///
    /// La respuesta trae los códigos para imprimir las etiquetas.
    /// </remarks>
    [HttpPost("{id:int}/recibir")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoRecepcionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Recibir(int id, CancellationToken ct)
    {
        var resultado = await _compras.RecibirAsync(id, ct);
        return Exito(resultado,
            $"{resultado.LotesGenerados} lote(s) ingresado(s) · {resultado.VarasIngresadas} varas");
    }

    /// <summary>
    /// Anula el borrador. Una compra ya recibida no se anula: para revertirla
    /// hay que registrar la merma de sus lotes indicando la devolución.
    /// </summary>
    [HttpPost("{id:int}/anular")]
    [ProducesResponseType(typeof(ApiResponse<CompraDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Anular(int id, CancellationToken ct)
        => Exito(await _compras.AnularAsync(id, ct), "Compra anulada");

    /// <summary>Cómo se movió el costo por vara de un producto entre compras.</summary>
    [HttpGet("evolucion-costo/{productoId:int}")]
    [Authorize(Policy = Politicas.VerInventario)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<EvolucionCostoDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> EvolucionCosto(int productoId, CancellationToken ct)
        => Exito(await _compras.EvolucionCostoAsync(productoId, ct));
}