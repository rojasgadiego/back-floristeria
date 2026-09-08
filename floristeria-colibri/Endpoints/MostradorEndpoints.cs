using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints del MOSTRADOR: las partidas que hay adelante y el traspaso
/// desde bodega.
///
/// El traspaso pide un LOTE, no un producto: hay que escanear el balde. Es
/// un paso más, y es el que evita que la trazabilidad se corte a mitad de
/// camino — la partida recuerda de dónde vino, y cuando se venda,
/// venta_consumos podrá anotar a qué lote devolver si se anula.
/// </summary>
public class MostradorEndpoints : EndpointsBase
{
    public MostradorEndpoints(ILogger<MostradorEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var mos = app.MapGroup("/api/mostrador")
            .WithTags("Mostrador")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        mos.MapGet("/partidas", Listar)
            .WithName("ListarPartidas")
            .WithSummary("Lo que hay en el mesón, con su QR y su vencimiento")
            .Produces<ResponseDto>(200);

        mos.MapGet("/partidas/{codigo}", Escanear)
            .WithName("EscanearPartida")
            .WithSummary("Lo que devuelve el escaneo: qué es, a cuánto sale, si se puede vender")
            .Produces<ResponseDto>(200).Produces(404);

        mos.MapGet("/producto/{productoId:int}", DeProducto)
            .WithName("PartidasDeProducto")
            .WithSummary("En orden de consumo: lo que vence antes sale primero")
            .Produces<ResponseDto>(200);

        mos.MapPost("/traspasos", Traspasar)
            .WithName("TraspasarLote")
            .WithSummary("Baja un lote al mesón. Devuelve la partida con su QR.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        mos.MapPost("/traspasos/producto", TraspasarSimple)
            .WithName("TraspasarProducto")
            .WithSummary("Para lo que no controla lotes: jarrones, cintas, tarjetas")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        mos.MapPost("/retornos", Retornar)
            .WithName("RetornarPartida")
            .WithSummary("Devuelve al lote del que salió. Bajar de más pasa.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);
    }

    public async Task<ResponseDto> Listar(
        [AsParameters] PartidaFiltro filtro, MostradorBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Listar(filtro, ct), "partidas");

    /// <summary>
    /// 200 aunque no sea vendible: la partida EXISTE y el vendedor necesita
    /// ver por qué no puede venderla. Un 404 haría pensar que la etiqueta
    /// está mala.
    /// </summary>
    public async Task<ResponseDto> Escanear(string codigo, MostradorBLL bll, CancellationToken ct)
    {
        try
        {
            var p = await bll.Escanear(codigo, ct);

            if (p is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.NotFound,
                    $"No hay ninguna partida con el código {QRCodeHelper.ExtraerCodigo(codigo)}.",
                    null);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok,
                p.Vendible ? (p.Advertencia ?? p.Producto) : p.Motivo ?? "No se puede vender.",
                p);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al escanear la partida {Codigo}", codigo);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al leer la partida.", null);
        }
    }

    public async Task<ResponseDto> DeProducto(int productoId, MostradorBLL bll, CancellationToken ct)
        => await Consultar(() => bll.DeProducto(productoId, ct), "partidas del producto");

    public async Task<ResponseDto> Traspasar(
        [FromBody] TraspasoLoteRequest peticion, MostradorBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Traspasar(peticion, UsuarioActual(http), ct),
            r => $"{r.Cantidad} de {r.Producto} al mesón · partida {r.Codigo}",
            HttpStatusCodes.Created, "traspasar");

    public async Task<ResponseDto> TraspasarSimple(
        [FromBody] TraspasoSimpleRequest peticion, MostradorBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.TraspasarSimple(peticion, UsuarioActual(http), ct),
            r => $"{r.Cantidad} de {r.Producto} al mesón · partida {r.Codigo}",
            HttpStatusCodes.Created, "traspasar");

    public async Task<ResponseDto> Retornar(
        [FromBody] RetornoPartidaRequest peticion, MostradorBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Retornar(peticion, UsuarioActual(http), ct),
            r => $"{r.Devuelto} de {r.Producto} de vuelta a bodega",
            HttpStatusCodes.Ok, "retornar");

    private async Task<ResponseDto> Escribir<T>(
        Func<Task<ResultadoOp<T>>> operacion, Func<T, string> mensajeExito,
        int statusExito, string queSeHacia)
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
