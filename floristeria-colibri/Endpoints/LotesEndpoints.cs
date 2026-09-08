using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de LOTES: la grilla, la ficha, las alertas, el escaneo y las
/// etiquetas.
///
/// TODO menos el PNG del QR devuelve ResponseDto. La imagen no: envolver un
/// binario en el sobre JSON obligaría al front a desempacar y reconstruir el
/// blob, y una etiqueta es una imagen.
/// </summary>
public class LotesEndpoints : EndpointsBase
{
    public LotesEndpoints(ILogger<LotesEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var lotes = app.MapGroup("/api/lotes")
            .WithTags("Lotes")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        // ═══ Rutas literales ANTES que /{id:int} ═══
        // Sin este orden, "historial" o "rezagados" no parsean como int y la
        // ruta específica nunca se alcanza.

        lotes.MapGet("/", Listar)
            .WithName("ListarLotes")
            .WithSummary("Lotes con existencias. `ordenFifo` marca cuál toca vender.")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/historial", Historial)
            .WithName("HistorialLotes")
            .WithSummary("Incluye agotados y descartados")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/rezagados", Rezagados)
            .WithName("LotesRezagados")
            .WithSummary("Quedaron atrás al abrir uno más nuevo. Si no se liquidan, son merma.")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/por-vencer", PorVencer)
            .WithName("LotesPorVencer")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/recuperados", Recuperados)
            .WithName("LotesRecuperados")
            .WithSummary("El balde aparte: fuera del reparto automático, se venden escaneando")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/costo-promedio", CostoPromedio)
            .WithName("CostoPromedioLotes")
            .WithSummary("El valor real de la cámara, ponderado por lote")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/etiquetas", Etiquetas)
            .WithName("EtiquetasLotes")
            .WithSummary("Datos para imprimir. La hoja la arma el front; el QR va aparte.")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/etiquetas/compra/{compraId:int}", EtiquetasDeCompra)
            .WithName("EtiquetasDeCompra")
            .WithSummary("Las de una recepción completa. Es el flujo real.")
            .Produces<ResponseDto>(200);

        lotes.MapGet("/codigo/{codigo}", PorCodigo)
            .WithName("LotePorCodigo")
            .WithSummary("Lo que abre el QR. Acepta el código pelado o el contenido completo.")
            .Produces<ResponseDto>(200).Produces(404);

        lotes.MapPost("/validar", Validar)
            .WithName("ValidarLote")
            .WithSummary("Escaneo en el punto de venta. Informativo: lo que manda es el cobro.")
            .Produces<ResponseDto>(200).Produces(404);

        // ═══ Rutas con id ═══

        lotes.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerLote")
            .WithSummary("La ficha con sus movimientos")
            .Produces<ResponseDto>(200).Produces(404);

        lotes.MapPatch("/{id:int}/ubicacion", ActualizarUbicacion)
            .WithName("UbicacionLote")
            .WithSummary("Lo único editable de un lote: las varas se mueven de otras formas.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);

        // ═══ El PNG: fuera del grupo, sin el filtro del sobre ═══
        //
        // El front lo pide por CÓDIGO y no por id: es lo que tiene a mano
        // después de escanear, y lo que aparece impreso en la etiqueta.
        app.MapGet("/api/lotes/{codigo}/qr", QrPng)
            .WithTags("Lotes")
            .WithName("QrLote")
            .WithSummary("El QR de un lote como PNG")
            .RequireAuthorization(Politicas.VerInventario)
            .Produces(200, contentType: "image/png").Produces(404);
    }

    #region Consultas

    public async Task<ResponseDto> Listar(
        [AsParameters] LoteFiltro filtro, LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Listar(filtro, ct), "lotes");

    public async Task<ResponseDto> Historial(
        [AsParameters] LoteFiltro filtro, LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Historial(filtro, ct), "historial de lotes");

    public async Task<ResponseDto> Obtener(int id, LotesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Obtener(id, ct), "Lote", $"No existe el lote {id}.");

    /// <summary>
    /// El 404 acá no es un fallo del sistema: es una etiqueta mal tipeada o
    /// borrosa, y la pantalla del front ya lo explica mejor que un error rojo.
    /// </summary>
    public async Task<ResponseDto> PorCodigo(string codigo, LotesBLL bll, CancellationToken ct)
        => await ConsultarUno(
            () => bll.ObtenerPorCodigo(codigo, ct),
            "Lote",
            $"No hay ningún lote con el código {QRCodeHelper.ExtraerCodigo(codigo)}.");

    public async Task<ResponseDto> Rezagados(LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Rezagados(ct), "restos rezagados");

    public async Task<ResponseDto> PorVencer(
        [FromQuery] int? dias, LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.PorVencer(dias ?? 3, ct), "lotes por vencer");

    public async Task<ResponseDto> Recuperados(LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Recuperados(ct), "flor recuperada");

    public async Task<ResponseDto> CostoPromedio(LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.CostoPromedio(ct), "costo promedio");

    #endregion

    #region Etiquetas

    /// <summary>
    /// El front manda `?ids=1&amp;ids=2`. Minimal API liga un int[] desde
    /// parámetros repetidos con ese mismo nombre; si el cliente mandara
    /// `ids[]=1`, el arreglo llegaría vacío sin ningún error.
    /// </summary>
    public async Task<ResponseDto> Etiquetas(
        [FromQuery] int[] ids, LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Etiquetas(ids, ct), "etiquetas");

    public async Task<ResponseDto> EtiquetasDeCompra(
        int compraId, LotesBLL bll, CancellationToken ct)
        => await Consultar(() => bll.EtiquetasDeCompra(compraId, ct), "etiquetas de la compra");

    /// <summary>
    /// Devuelve el PNG directo, sin el sobre JSON.
    ///
    /// El QR se arma desde los datos del lote —no se guarda en ninguna
    /// columna— así que hay que leerlo primero. Es una consulta por imagen,
    /// pero el contenido nunca cambia una vez creado el lote, así que el
    /// navegador puede cachearlo.
    /// </summary>
    public async Task<IResult> QrPng(
        string codigo, [FromQuery] int? px, LotesBLL bll, CancellationToken ct)
    {
        var lote = await bll.ObtenerPorCodigo(codigo, ct);
        if (lote is null) return Results.NotFound();

        var contenido = QRCodeHelper.Construir(
            lote.Id, lote.ProductoId, lote.FechaIngreso, lote.Codigo);

        var png = QRCodeHelper.GenerarPng(contenido, px ?? QRCodeHelper.PixelesPorModulo);

        return Results.File(png, "image/png", fileDownloadName: $"{lote.Codigo}.png");
    }

    #endregion

    #region Punto de venta

    /// <summary>
    /// 200 aunque no se pueda vender: el lote EXISTE y el vendedor necesita
    /// ver por qué no. Un 404 acá haría pensar que la etiqueta está mala.
    /// </summary>
    public async Task<ResponseDto> Validar(
        [FromBody] ValidarLoteRequest peticion, LotesBLL bll, CancellationToken ct)
    {
        try
        {
            var r = await bll.Validar(peticion, ct);

            if (r is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.NotFound,
                    $"No hay ningún lote con el código {QRCodeHelper.ExtraerCodigo(peticion.Codigo)}.",
                    null);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok,
                r.Puede ? (r.Advertencia ?? r.Producto) : r.Motivo ?? "No se puede vender este lote.",
                r);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al validar el lote {Codigo}", peticion.Codigo);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al validar el lote.", null);
        }
    }

    #endregion

    #region Escritura

    public async Task<ResponseDto> ActualizarUbicacion(
        int id, [FromBody] UbicacionRequest peticion, LotesBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            var r = await bll.ActualizarUbicacion(
                id, peticion?.Ubicacion ?? "", UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Ubicación registrada", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al actualizar la ubicación del lote {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al guardar la ubicación: {ex.Message}", null);
        }
    }

    #endregion
}
