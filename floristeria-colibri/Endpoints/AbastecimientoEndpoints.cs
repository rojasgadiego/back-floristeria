using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de ABASTECIMIENTO: proveedores, presentaciones y compras.
/// Los de lotes y etiquetas viven en LotesEndpoints.
///
/// Tres grupos porque el front los pide en tres raíces distintas.
/// </summary>
public class AbastecimientoEndpoints : EndpointsBase
{
    public AbastecimientoEndpoints(ILogger<AbastecimientoEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // ═══════════════ /api/proveedores ═══════════════
        var prov = app.MapGroup("/api/proveedores")
            .WithTags("Proveedores")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        prov.MapGet("/", ListarProveedores).WithName("ListarProveedores")
            .Produces<ResponseDto>(200);

        prov.MapGet("/{id:int}", ObtenerProveedor).WithName("ObtenerProveedor")
            .Produces<ResponseDto>(200).Produces(404);

        prov.MapPost("/", CrearProveedor).WithName("CrearProveedor")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        prov.MapPut("/{id:int}", ActualizarProveedor).WithName("ActualizarProveedor")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        prov.MapPatch("/{id:int}/activar",
                (int id, AbastecimientoBLL bll, CancellationToken ct)
                    => CambiarEstadoProveedor(id, true, bll, ct))
            .WithName("ActivarProveedor")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        prov.MapPatch("/{id:int}/desactivar",
                (int id, AbastecimientoBLL bll, CancellationToken ct)
                    => CambiarEstadoProveedor(id, false, bll, ct))
            .WithName("DesactivarProveedor")
            .WithSummary("Lo saca del selector de compra nueva conservando el historial")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        // ═══════════════ /api/presentaciones ═══════════════
        //
        // Cuelgan del producto para listar y crear: una presentación sin
        // producto no existe, y el front siempre las pide de a uno.
        var pres = app.MapGroup("/api/presentaciones")
            .WithTags("Presentaciones")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        pres.MapGet("/producto/{productoId:int}", ListarPresentaciones)
            .WithName("ListarPresentaciones")
            .WithSummary("Cómo viene ese producto del proveedor. `paquetes` define cuántos QR salen.")
            .Produces<ResponseDto>(200);

        pres.MapPost("/producto/{productoId:int}", CrearPresentacion)
            .WithName("CrearPresentacion")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        pres.MapPut("/{id:int}", ActualizarPresentacion)
            .WithName("ActualizarPresentacion")
            .WithSummary("Si ya se usó en una compra, su contenido queda congelado.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        pres.MapDelete("/{id:int}", EliminarPresentacion)
            .WithName("EliminarPresentacion")
            .WithSummary("Desactiva. No hay borrado: las compras viejas la referencian.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200);

        // ═══════════════ /api/compras ═══════════════
        var compras = app.MapGroup("/api/compras")
            .WithTags("Compras")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        compras.MapGet("/", ListarCompras).WithName("ListarCompras")
            .Produces<ResponseDto>(200);

        // ANTES que /{id:int}: sin este orden, "evolucion-costo" no parsea
        // como int y la ruta nunca calza.
        compras.MapGet("/evolucion-costo/{productoId:int}", EvolucionCosto)
            .WithName("EvolucionCosto")
            .WithSummary("Cómo se movió el costo por vara de un producto entre compras")
            .Produces<ResponseDto>(200);

        compras.MapGet("/{id:int}", ObtenerCompra)
            .WithName("ObtenerCompra")
            .WithSummary("Cabecera, líneas y —si ya se recibió— los lotes que generó")
            .Produces<ResponseDto>(200).Produces(404);

        compras.MapPost("/", CrearCompra)
            .WithName("CrearCompra")
            .WithSummary("Guarda el borrador completo, con sus líneas. No toca el inventario.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        compras.MapPut("/{id:int}", ActualizarCompra)
            .WithName("ActualizarCompra")
            .WithSummary("Reemplaza la compra entera. Solo borradores.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        compras.MapPost("/{id:int}/recibir", Recibir)
            .WithName("RecibirCompra")
            .WithSummary("LA PUERTA DE ENTRADA: genera los lotes con su QR y sube el stock")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        compras.MapPost("/{id:int}/anular", Anular)
            .WithName("AnularCompra")
            .WithSummary("Solo desde borrador. Una recibida ya generó lotes.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);
    }

    #region Proveedores

    public async Task<ResponseDto> ListarProveedores(
        [AsParameters] ProveedorFiltro filtro, AbastecimientoBLL bll, CancellationToken ct)
        => await Consultar(() => bll.ListarProveedores(filtro, ct), "proveedores");

    public async Task<ResponseDto> ObtenerProveedor(int id, AbastecimientoBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.ObtenerProveedor(id, ct),
            "Proveedor", $"No existe el proveedor {id}.");

    public async Task<ResponseDto> CrearProveedor(
        [FromBody] ProveedorRequest peticion, AbastecimientoBLL bll, CancellationToken ct)
        => await Escribir(() => bll.CrearProveedor(peticion, ct),
            p => $"{p.Nombre} agregado", HttpStatusCodes.Created, "crear el proveedor");

    public async Task<ResponseDto> ActualizarProveedor(
        int id, [FromBody] ProveedorRequest peticion, AbastecimientoBLL bll, CancellationToken ct)
        => await Escribir(() => bll.ActualizarProveedor(id, peticion, ct),
            _ => "Proveedor actualizado", HttpStatusCodes.Ok, "actualizar el proveedor");

    public async Task<ResponseDto> CambiarEstadoProveedor(
        int id, bool activo, AbastecimientoBLL bll, CancellationToken ct)
        => await Escribir(() => bll.CambiarEstadoProveedor(id, activo, ct),
            _ => activo ? "Proveedor activado" : "Proveedor desactivado",
            HttpStatusCodes.Ok, "cambiar el estado");

    #endregion

    #region Presentaciones

    public async Task<ResponseDto> ListarPresentaciones(
        int productoId, AbastecimientoBLL bll, CancellationToken ct)
        => await Consultar(() => bll.ListarPresentaciones(productoId, null, ct), "presentaciones");

    public async Task<ResponseDto> CrearPresentacion(
        int productoId, [FromBody] PresentacionRequest peticion,
        AbastecimientoBLL bll, CancellationToken ct)
        => await Escribir(() => bll.CrearPresentacion(productoId, peticion, ct),
            p => $"{p.Nombre} · {p.Paquetes} × {p.VarasPorPaquete} varas",
            HttpStatusCodes.Created, "crear la presentación");

    public async Task<ResponseDto> ActualizarPresentacion(
        int id, [FromBody] PresentacionRequest peticion, AbastecimientoBLL bll, CancellationToken ct)
        => Texto(await bll.ActualizarPresentacion(id, peticion, ct), "Presentación actualizada");

    /// <summary>
    /// DELETE que desactiva. El verbo dice "borrar" porque es lo que la
    /// persona cree que hace, pero las compras históricas referencian la
    /// presentación y borrarla partiría el historial de costos.
    /// </summary>
    public async Task<ResponseDto> EliminarPresentacion(
        int id, AbastecimientoBLL bll, CancellationToken ct)
        => Texto(await bll.CambiarEstadoPresentacion(id, false, ct), "Presentación desactivada");

    #endregion

    #region Compras

    public async Task<ResponseDto> ListarCompras(
        [AsParameters] CompraFiltro filtro, AbastecimientoBLL bll, CancellationToken ct)
        => await Consultar(() => bll.ListarCompras(filtro, ct), "compras");

    public async Task<ResponseDto> ObtenerCompra(int id, AbastecimientoBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.ObtenerDetalle(id, ct), "Compra", $"No existe la compra {id}.");

    public async Task<ResponseDto> CrearCompra(
        [FromBody] GuardarCompraRequest peticion, AbastecimientoBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.CrearCompra(peticion, UsuarioActual(http), ct),
            c => $"Compra {c.Folio} guardada en borrador",
            HttpStatusCodes.Created, "crear la compra");

    public async Task<ResponseDto> ActualizarCompra(
        int id, [FromBody] GuardarCompraRequest peticion, AbastecimientoBLL bll, CancellationToken ct)
        => await Escribir(() => bll.ActualizarCompra(id, peticion, ct),
            c => $"Compra {c.Folio} actualizada", HttpStatusCodes.Ok, "actualizar la compra");

    /// <summary>
    /// El mensaje de éxito dice cuántos lotes salieron, porque imprimir sus
    /// etiquetas es literalmente lo siguiente que hay que hacer.
    /// </summary>
    public async Task<ResponseDto> Recibir(
        int id, [FromBody] RecibirCompraRequest? peticion, AbastecimientoBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(
            () => bll.RecibirCompra(id, peticion ?? new RecibirCompraRequest(), UsuarioActual(http), ct),
            r => $"{r.Folio} recibida · {r.VarasIngresadas} varas en {r.LotesGenerados} lote(s)",
            HttpStatusCodes.Ok, "recibir la compra");

    public async Task<ResponseDto> Anular(
        int id, [FromBody] AnularCompraRequest? peticion, AbastecimientoBLL bll,
        HttpContext http, CancellationToken ct)
        => Texto(await bll.AnularCompra(id, peticion?.Motivo, UsuarioActual(http), ct),
            "Compra anulada");

    public async Task<ResponseDto> EvolucionCosto(
        int productoId, [FromQuery] int? limite, AbastecimientoBLL bll, CancellationToken ct)
        => await Consultar(() => bll.EvolucionCosto(productoId, limite ?? 12, ct),
            "historial de costos");

    #endregion

    #region Helpers

    /// <summary>
    /// El patrón de escritura, una sola vez: si salió bien devuelve los datos
    /// con su mensaje; si no, el texto del RAISE tal cual, sin reformular.
    /// Estaba copiado en catorce handlers casi idénticos.
    /// </summary>
    private async Task<ResponseDto> Escribir<T>(
        Func<Task<ResultadoOp<T>>> operacion,
        Func<T, string> mensajeExito,
        int statusExito,
        string queSeHacia)
    {
        try
        {
            var r = await operacion();

            return r.Ok
                ? CustomUtilz.CreateResponse(statusExito, mensajeExito(r.Datos!), r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al {Que}", queSeHacia);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al {queSeHacia}: {ex.Message}", null);
        }
    }

    /// <summary>Para las operaciones que devuelven string: "" = salió bien.</summary>
    private static ResponseDto Texto(string error, string mensajeExito)
        => string.IsNullOrWhiteSpace(error)
            ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, mensajeExito, new { Ok = true })
            : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, error, new { Ok = false });

    #endregion
}