using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Features.Clientes.Dtos;

namespace Colibri.Api.Features.Clientes;

/// <summary>
/// Club de clientes. Quien atiende el mesón necesita buscarlos y crearlos
/// sobre la marcha, así que el módulo es de la política Caja.
/// </summary>
[Authorize(Policy = Politicas.Caja)]
public class ClientesController : ControladorBase
{
    private readonly IClientesService _clientes;

    public ClientesController(IClientesService clientes) => _clientes = clientes;

    /// <summary>
    /// Lista con su historial acumulado.
    /// </summary>
    /// <remarks>
    /// `buscar` recorre nombre, RUT, teléfono y correo. El RUT se normaliza:
    /// da lo mismo si se escribe con puntos, sin puntos o con guion.
    ///
    /// `sinComprarDias` filtra a quienes se alejaron, que es a quienes vale
    /// la pena llamar.
    /// </remarks>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<ClienteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] ClienteFiltro filtro, CancellationToken ct)
        => Exito(await _clientes.ListarAsync(filtro, ct));

    /// <summary>
    /// Ficha completa: últimas compras, libro de puntos y lo que más compra.
    /// </summary>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ClienteDetalleDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Obtener(int id, CancellationToken ct)
        => Exito(await _clientes.ObtenerAsync(id, ct));

    /// <summary>
    /// Búsqueda por RUT para el punto de venta.
    /// </summary>
    /// <remarks>
    /// Devuelve `datos: null` con 200 si no existe, en vez de 404: en el mesón
    /// lo normal es que el cliente no esté registrado, y eso no es un error
    /// que deba pintarse de rojo.
    /// </remarks>
    [HttpGet("rut/{rut}")]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> PorRut(string rut, CancellationToken ct)
    {
        var cliente = await _clientes.BuscarPorRutAsync(rut, ct);
        return Exito(cliente, cliente is null ? "No hay ficha con ese RUT" : null);
    }

    /// <summary>
    /// Crea la ficha.
    /// </summary>
    /// <remarks>
    /// El RUT se valida con módulo 11. Si el dígito está mal, la respuesta
    /// dice cuál debería ser: casi siempre el error es ese y no el número.
    ///
    /// Si el RUT ya existe, el mensaje trae el nombre de quien lo tiene, y si
    /// esa ficha está desactivada sugiere reactivarla en vez de duplicarla.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Crear(
        [FromBody] GuardarClienteRequest peticion, CancellationToken ct)
    {
        var cliente = await _clientes.CrearAsync(peticion, ct);
        return Creado(cliente, $"{cliente.Nombre} se sumó al club");
    }

    /// <summary>
    /// Actualiza la ficha. Los puntos no se tocan acá: se mueven con su
    /// propio endpoint, que exige motivo.
    /// </summary>
    [HttpPut("{id:int}")]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Actualizar(
        int id, [FromBody] GuardarClienteRequest peticion, CancellationToken ct)
        => Exito(await _clientes.ActualizarAsync(id, peticion, ct));

    /// <summary>
    /// Desactiva la ficha. No se elimina: sus boletas históricas la
    /// referencian.
    /// </summary>
    [HttpPatch("{id:int}/desactivar")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Desactivar(int id, CancellationToken ct)
        => Exito(await _clientes.CambiarEstadoAsync(id, false, ct), "Ficha desactivada");

    [HttpPatch("{id:int}/reactivar")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Reactivar(int id, CancellationToken ct)
        => Exito(await _clientes.CambiarEstadoAsync(id, true, ct), "Ficha reactivada");

    /// <summary>
    /// Regala o descuenta puntos a mano.
    /// </summary>
    /// <remarks>
    /// Exige motivo: los puntos son dinero, y un saldo que no cuadra tiene
    /// que poder explicarse. Cantidad positiva regala, negativa descuenta.
    ///
    /// Exclusivo de administración.
    /// </remarks>
    [HttpPost("{id:int}/ajustar-puntos")]
    [Authorize(Policy = Politicas.Admin)]
    [ProducesResponseType(typeof(ApiResponse<ClienteDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> AjustarPuntos(
        int id, [FromBody] AjustarPuntosRequest peticion, CancellationToken ct)
    {
        var cliente = await _clientes.AjustarPuntosAsync(id, peticion, ct);
        return Exito(cliente, $"Saldo actualizado: {cliente.Puntos} puntos");
    }

    /// <summary>Todas las compras del cliente, paginadas.</summary>
    [HttpGet("{id:int}/compras")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<CompraClienteDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Compras(
        int id, [FromQuery] ParametrosPagina parametros, CancellationToken ct)
        => Exito(await _clientes.HistorialComprasAsync(id, parametros, ct));

    /// <summary>
    /// Libro de puntos: cada acumulación y cada canje, con su motivo.
    /// </summary>
    [HttpGet("{id:int}/puntos")]
    [ProducesResponseType(typeof(ApiResponse<ResultadoPagina<MovimientoPuntosDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Puntos(
        int id, [FromQuery] ParametrosPagina parametros, CancellationToken ct)
        => Exito(await _clientes.MovimientosPuntosAsync(id, parametros, ct));

    /// <summary>
    /// Quiénes cumplen años este mes.
    /// </summary>
    /// <remarks>
    /// Es la campaña que más rinde en una florería. `diasFaltantes` viene
    /// negativo si la fecha ya pasó, para distinguir a quién ya se saludó.
    ///
    /// Sin parámetro toma el mes actual.
    /// </remarks>
    [HttpGet("cumpleanos")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CumpleanosDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Cumpleanos(
        [FromQuery] short? mes, CancellationToken ct)
        => Exito(await _clientes.CumpleanosDelMesAsync(mes, ct));
}