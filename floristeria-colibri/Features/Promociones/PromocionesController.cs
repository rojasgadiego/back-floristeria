using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Promociones.Dtos;

namespace Colibri.Api.Features.Promociones;

/// <summary>
/// Promociones. Consultarlas es de la política Caja —el punto de venta las
/// ofrece—; crearlas y editarlas, exclusivo de administración: definen
/// cuánto dinero se regala.
/// </summary>
[Authorize(Policy = Politicas.Admin)]
public class PromocionesController : ControladorBase
{
    private readonly IPromocionesService _promociones;

    public PromocionesController(IPromocionesService promociones)
        => _promociones = promociones;

    /// <summary>
    /// Lista con su uso acumulado.
    /// </summary>
    /// <remarks>
    /// `vigenteHoy` dice si corre en este momento, y `motivoNoVigente`
    /// explica por qué no: sin eso, una promoción "activa" que el punto de
    /// venta no ofrece deja a todos adivinando.
    /// </remarks>
    [HttpGet]
    [Authorize(Policy = Politicas.Caja)]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<PromocionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] PromocionFiltro filtro, CancellationToken ct)
        => Exito(await _promociones.ListarAsync(filtro, ct));

    /// <summary>
    /// Las que corren hoy. Es lo que el punto de venta puede ofrecer.
    /// </summary>
    [HttpGet("vigentes")]
    [Authorize(Policy = Politicas.Caja)]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<PromocionDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Vigentes(CancellationToken ct)
        => Exito(await _promociones.VigentesAsync(ct));

    /// <summary>
    /// Detalle con su rendimiento y las promociones con las que se pisa.
    /// </summary>
    [HttpGet("{id:int}")]
    [Authorize(Policy = Politicas.Caja)]
    [ProducesResponseType(typeof(ApiResponse<PromocionDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _promociones.ObtenerAsync(id, ct));

    /// <summary>
    /// Crea la promoción.
    /// </summary>
    /// <remarks>
    /// El alcance decide qué campo es obligatorio: `categoria` necesita
    /// `categoriaId`, `producto` necesita `productoId`, y `boleta` ninguno.
    ///
    /// `dias` usa 0 para domingo y 6 para sábado. Lista vacía significa todos
    /// los días.
    ///
    /// El mínimo siempre se mide sobre el total de la boleta, aunque el
    /// descuento se aplique solo a una categoría: es lo que entiende el
    /// cliente cuando lee "en compras sobre $20.000".
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<PromocionDetalleDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Crear(
        [FromBody] GuardarPromocionRequest peticion, CancellationToken ct)
    {
        var promo = await _promociones.CrearAsync(peticion, ct);

        var mensaje = promo.Conflictos.Count > 0
            ? $"«{promo.Nombre}» creada. Se pisa con {promo.Conflictos.Count} promoción(es): " +
              "revisa cuál conviene mantener."
            : $"«{promo.Nombre}» creada";

        return Creado(promo, mensaje);
    }

    /// <summary>
    /// Edita la promoción.
    /// </summary>
    /// <remarks>
    /// No reescribe las boletas ya emitidas: cada venta guardó el descuento
    /// con que se calculó. El cambio rige de aquí en adelante.
    /// </remarks>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<PromocionDetalleDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarPromocionRequest peticion, CancellationToken ct)
        => Exito(await _promociones.ActualizarAsync(id, peticion, ct));

    [HttpPatch("{id:int}/activar")]
    [ProducesResponseType(typeof(ApiResponse<PromocionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activar(int id, CancellationToken ct)
        => Exito(await _promociones.CambiarEstadoAsync(id, true, ct), "Promoción activada");

    [HttpPatch("{id:int}/desactivar")]
    [ProducesResponseType(typeof(ApiResponse<PromocionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
        => Exito(await _promociones.CambiarEstadoAsync(id, false, ct), "Promoción desactivada");

    /// <summary>
    /// Borrado definitivo. Solo para promociones creadas por error: si ya se
    /// aplicó en alguna boleta, se rechaza.
    /// </summary>
    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        await _promociones.EliminarAsync(id, ct);
        return SinContenido();
    }

    /// <summary>
    /// Simula la promoción contra las ventas reales del período.
    /// </summary>
    /// <remarks>
    /// Devuelve en cuántas boletas habría aplicado, cuánto habría descontado
    /// y qué porcentaje de la venta del período se habría ido en descuento.
    ///
    /// Usa exactamente la misma regla que el cobro, así que lo que muestra es
    /// lo que va a pasar. Sin fechas toma los últimos 30 días; el máximo es
    /// 180.
    ///
    /// No guarda nada: se puede simular una promoción que todavía no existe.
    /// </remarks>
    [HttpPost("simular")]
    [ProducesResponseType(typeof(ApiResponse<SimulacionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Simular(
        [FromBody] SimularRequest peticion, CancellationToken ct)
    {
        var simulacion = await _promociones.SimularAsync(peticion, ct);

        var mensaje = simulacion.BoletasQueAplican == 0
            ? "En el período evaluado no habría aplicado a ninguna boleta"
            : $"Habría aplicado a {simulacion.BoletasQueAplican} de " +
              $"{simulacion.BoletasEvaluadas} boletas · " +
              $"{simulacion.DescuentoTotal:N0} en descuentos " +
              $"({simulacion.ImpactoSobreVentas}% de la venta)";

        return Exito(simulacion, mensaje);
    }
}