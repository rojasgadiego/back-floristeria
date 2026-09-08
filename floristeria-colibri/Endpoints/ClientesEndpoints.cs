using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de CLIENTES.
///
/// El grupo es de la política Vender: quien atiende el mesón necesita buscar
/// y crear fichas sobre la marcha, con el cliente esperando. Desactivar y
/// ajustar puntos son de administración: mueven plata.
/// </summary>
public class ClientesEndpoints : EndpointsBase
{
    public ClientesEndpoints(ILogger<ClientesEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var cli = app.MapGroup("/api/clientes")
            .WithTags("Clientes")
            .RequireAuthorization(Politicas.Vender)
            .AddEndpointFilter<RespuestaFilter>();

        // ═══ Literales antes que /{id:int} ═══

        cli.MapGet("/", Listar)
            .WithName("ListarClientes")
            .Produces<ResponseDto>(200);

        cli.MapGet("/cumpleanos", Cumpleanos)
            .WithName("CumpleanosClientes")
            .WithSummary("Quiénes cumplen este mes. Sin mes toma el actual.")
            .Produces<ResponseDto>(200);

        cli.MapGet("/rut/{rut}", PorRut)
            .WithName("ClientePorRut")
            .WithSummary("Para el mesón. Devuelve null con 200 si no hay ficha.")
            .Produces<ResponseDto>(200);

        cli.MapPost("/", Crear)
            .WithName("CrearCliente")
            .Produces<ResponseDto>(201).Produces(400);

        // ═══ Con id ═══

        cli.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerCliente")
            .WithSummary("Ficha, últimas compras, libro de puntos y lo que más se lleva")
            .Produces<ResponseDto>(200).Produces(404);

        cli.MapGet("/{id:int}/compras", Compras)
            .WithName("ComprasCliente")
            .Produces<ResponseDto>(200);

        cli.MapGet("/{id:int}/puntos", Puntos)
            .WithName("PuntosCliente")
            .WithSummary("Cada movimiento con su saldo resultante")
            .Produces<ResponseDto>(200);

        cli.MapPut("/{id:int}", Actualizar)
            .WithName("ActualizarCliente")
            .Produces<ResponseDto>(200).Produces(400);

        cli.MapPatch("/{id:int}/desactivar",
                (int id, ClientesBLL bll, CancellationToken ct) => CambiarEstado(id, false, bll, ct))
            .WithName("DesactivarCliente")
            .WithSummary("Lo saca del buscador. No borra sus puntos ni sus compras.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cli.MapPatch("/{id:int}/reactivar",
                (int id, ClientesBLL bll, CancellationToken ct) => CambiarEstado(id, true, bll, ct))
            .WithName("ReactivarCliente")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        cli.MapPost("/{id:int}/ajustar-puntos", AjustarPuntos)
            .WithName("AjustarPuntos")
            .WithSummary("Con signo: positiva regala, negativa descuenta. Motivo obligatorio.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);
    }

    #region Consultas

    public async Task<ResponseDto> Listar(
        [AsParameters] ClienteFiltro filtro, ClientesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Listar(filtro, ct), "clientes");

    public async Task<ResponseDto> Obtener(int id, ClientesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Obtener(id, ct), "Cliente", $"No existe el cliente {id}.");

    /// <summary>
    /// 200 con datos en null cuando no hay ficha. Un 404 haría que el
    /// vendedor viera un error rojo por atender a alguien nuevo, que es la
    /// situación más común del mesón.
    /// </summary>
    public async Task<ResponseDto> PorRut(string rut, ClientesBLL bll, CancellationToken ct)
    {
        try
        {
            var c = await bll.PorRut(rut, ct);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok,
                c is null ? "Sin ficha con ese RUT." : c.Nombre,
                c);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al buscar el RUT {Rut}", rut);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al buscar el cliente.", null);
        }
    }

    public async Task<ResponseDto> Compras(
        int id, [AsParameters] PaginaFiltro pagina, ClientesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Compras(id, pagina, ct), "compras del cliente");

    public async Task<ResponseDto> Puntos(
        int id, [AsParameters] PaginaFiltro pagina, ClientesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Puntos(id, pagina, ct), "movimientos de puntos");

    public async Task<ResponseDto> Cumpleanos(
        [FromQuery] int? mes, ClientesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Cumpleanos(mes, ct), "cumpleaños");

    #endregion

    #region Escritura

    public async Task<ResponseDto> Crear(
        [FromBody] ClienteRequest peticion, ClientesBLL bll, CancellationToken ct)
        => await Escribir(() => bll.Crear(peticion, ct),
            c => $"{c.Nombre} agregado", HttpStatusCodes.Created, "crear el cliente");

    public async Task<ResponseDto> Actualizar(
        int id, [FromBody] ClienteRequest peticion, ClientesBLL bll, CancellationToken ct)
        => await Escribir(() => bll.Actualizar(id, peticion, ct),
            _ => "Ficha actualizada", HttpStatusCodes.Ok, "actualizar el cliente");

    public async Task<ResponseDto> CambiarEstado(
        int id, bool activo, ClientesBLL bll, CancellationToken ct)
        => await Escribir(() => bll.CambiarEstado(id, activo, ct),
            c => $"{c.Nombre} {(activo ? "reactivado" : "desactivado")}",
            HttpStatusCodes.Ok, "cambiar el estado");

    /// <summary>
    /// El mensaje de éxito dice el saldo nuevo: es lo que la persona necesita
    /// confirmar después de regalar o descontar puntos.
    /// </summary>
    public async Task<ResponseDto> AjustarPuntos(
        int id, [FromBody] AjustePuntosRequest peticion, ClientesBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.AjustarPuntos(id, peticion, UsuarioActual(http), ct),
            c => $"{(peticion.Cantidad > 0 ? "+" : "")}{peticion.Cantidad} puntos · " +
                 $"{c.Nombre} queda con {c.Puntos}",
            HttpStatusCodes.Ok, "ajustar los puntos");

    #endregion

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
}
