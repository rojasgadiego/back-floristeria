using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de VENTAS.
///
/// EL FILTRO POR USUARIO NO ES NEGOCIABLE Y NO VIENE DEL CLIENTE. Un
/// vendedor no debe ver lo que vendió otro —cuánto, a quién, con qué
/// descuentos— y si el usuarioId viajara en el query string, cambiar el
/// número en la URL bastaría para cruzarlos.
/// </summary>
public class VentasEndpoints : EndpointsBase
{
    public VentasEndpoints(ILogger<VentasEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var ventas = app.MapGroup("/api/ventas")
            .WithTags("Ventas")
            .RequireAuthorization(Politicas.Vender)
            .AddEndpointFilter<RespuestaFilter>();

        // Literales antes que /{id:int}.
        ventas.MapPost("/promociones-aplicables", PromocionesAplicables)
            .WithName("PromocionesAplicables")
            .WithSummary("Las que aplican a este carrito, con el descuento estimado")
            .Produces<ResponseDto>(200);

        ventas.MapGet("/", Listar)
            .WithName("ListarVentas")
            .WithSummary("Historial. Un vendedor ve solo sus boletas.")
            .Produces<ResponseDto>(200);

        ventas.MapPost("/", Registrar)
            .WithName("RegistrarVenta")
            .WithSummary("Cobra la boleta. Necesita caja abierta.")
            .Produces<ResponseDto>(201).Produces(400);

        ventas.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerVenta")
            .WithSummary("La boleta con sus líneas y su plan de consumo")
            .Produces<ResponseDto>(200).Produces(404).Produces(403);

        ventas.MapGet("/{id:int}/ticket", Ticket)
            .WithName("TicketVenta")
            .WithSummary("La boleta más los datos del local, listos para imprimir")
            .Produces<ResponseDto>(200).Produces(404);

        ventas.MapPost("/{id:int}/anular", Anular)
            .WithName("AnularVenta")
            .WithSummary("Devuelve las varas al lote del que salieron. Solo admin.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);
    }

    #region Cobro

    /// <summary>
    /// El mensaje de éxito lleva el vuelto cuando corresponde: es lo único
    /// que la persona necesita leer con el cliente esperando.
    /// </summary>
    public async Task<ResponseDto> Registrar(
        [FromBody] RegistrarVentaRequest peticion, VentasBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            if (peticion is null)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, "Request inválido", null);

            var r = await bll.Registrar(peticion, UsuarioActual(http), ct);

            if (!r.Ok)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);

            var v = r.Datos!;
            var mensaje = v.Vuelto is > 0
                ? $"Boleta {v.Folio} · vuelto ${v.Vuelto:N0}"
                : $"Boleta {v.Folio} · ${v.Total:N0}";

            return CustomUtilz.CreateResponse(HttpStatusCodes.Created, mensaje, v);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al registrar la venta");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al cobrar: {ex.Message}", null);
        }
    }

    public async Task<ResponseDto> Anular(
        int id, [FromBody] AnularVentaRequest peticion, VentasBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            var r = await bll.Anular(id, peticion?.Motivo ?? "", UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok,
                    $"Boleta {r.Datos!.Folio} anulada · {r.Datos.Devuelto} unidad(es) devuelta(s)",
                    r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al anular la venta {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al anular: {ex.Message}", null);
        }
    }

    #endregion

    #region Consultas

    public async Task<ResponseDto> Listar(
        [AsParameters] VentaFiltro filtro, VentasBLL bll, HttpContext http, CancellationToken ct)
    {
        // El usuarioId que mande el cliente se IGNORA salvo que sea admin.
        filtro.UsuarioId = EsAdmin(http) ? filtro.UsuarioId : UsuarioActual(http);
        return await Consultar(() => bll.Listar(filtro, ct), "ventas");
    }

    /// <summary>
    /// 403 y no 404 cuando la boleta es de otro: existe, pero no le
    /// corresponde. Un 404 invitaría a probar ids hasta encontrar los suyos.
    /// </summary>
    public async Task<ResponseDto> Obtener(
        int id, VentasBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var venta = await bll.Obtener(id, ct);

            if (venta is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.NotFound, $"No existe la boleta {id}.", null);

            if (!EsAdmin(http) && venta.UsuarioId != UsuarioActual(http))
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.Forbidden, "Esa boleta la registró otra persona.", null);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok, $"Boleta {venta.Folio}", venta);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al obtener la venta {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al obtener la boleta.", null);
        }
    }

    public async Task<ResponseDto> Ticket(
        int id, VentasBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var ticket = await bll.Ticket(id, ct);

            if (ticket is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.NotFound, $"No existe la boleta {id}.", null);

            if (!EsAdmin(http) && ticket.Venta.UsuarioId != UsuarioActual(http))
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.Forbidden, "Esa boleta la registró otra persona.", null);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok, $"Ticket {ticket.Venta.Folio}", ticket);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al armar el ticket de {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al armar el ticket.", null);
        }
    }

    /// <summary>
    /// El cuerpo es la lista de líneas, sin envolver. Devuelve estimaciones:
    /// al cobrar se recalcula con los precios de ese momento.
    /// </summary>
    public async Task<ResponseDto> PromocionesAplicables(
        [FromBody] List<VentaLineaPrevia> items, VentasBLL bll, CancellationToken ct)
        => await Consultar(() => bll.PromocionesAplicables(items, ct), "promociones");

    #endregion

    /// <summary>
    /// El rol sale del token, no de un parámetro. Ver JwtConfig: los claims
    /// van sin mapear, así que el rol vive en "role" tal cual se emitió.
    /// </summary>
    private static bool EsAdmin(HttpContext http) => http.User.IsInRole("admin");
}
