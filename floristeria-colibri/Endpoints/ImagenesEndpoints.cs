using Colibri.Api.Auth;
using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Utils;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Fotos de productos y el catálogo público de la landing.
///
///   PUT    /api/productos/{id}/imagen   el cuerpo ES la foto (image/jpeg,
///                                       png o webp), sin multipart
///   DELETE /api/productos/{id}/imagen
///   GET    /api/productos/imagenes      mapa producto → versión
///   GET    /api/publico/productos/{id}/imagen?v=   la foto, sin sesión
///   GET    /api/publico/catalogo        lo activo y con foto, sin sesión
///
/// Lo público no pasa por RespuestaFilter porque la foto es un archivo; el
/// catálogo sí devuelve el sobre de siempre.
/// </summary>
public class ImagenesEndpoints : EndpointsBase
{
    public ImagenesEndpoints(ILogger<ImagenesEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var productos = app.MapGroup("/api/productos")
            .WithTags("Productos")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        productos.MapGet("/imagenes", Versiones)
            .WithName("VersionesImagenes")
            .WithSummary("Qué productos tienen foto y en qué versión")
            .Produces<ResponseDto>(200).Produces(401);

        productos.MapPut("/{id:int}/imagen", Subir)
            .WithName("SubirImagenProducto")
            .WithSummary("Sube o reemplaza la foto. El cuerpo es la imagen.")
            .RequireAuthorization(Politicas.Inventario)
            .Accepts<byte[]>("image/jpeg", "image/png", "image/webp")
            .Produces<ResponseDto>(200).Produces(400).Produces(401);

        productos.MapDelete("/{id:int}/imagen", Quitar)
            .WithName("QuitarImagenProducto")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(401);

        var publico = app.MapGroup("/api/publico")
            .WithTags("Público")
            .AllowAnonymous();

        publico.MapGet("/productos/{id:int}/imagen", Servir)
            .WithName("VerImagenProducto")
            .Produces(200, contentType: "image/jpeg").Produces(404);

        publico.MapGet("/catalogo", Catalogo)
            .WithName("CatalogoPublico")
            .WithSummary("Productos activos con foto, para la landing")
            .AddEndpointFilter<RespuestaFilter>()
            .Produces<ResponseDto>(200);
    }

    public Task<ResponseDto> Versiones(ImagenesBLL bll, CancellationToken ct)
        => Consultar(() => bll.Versiones(ct), "fotos");

    public Task<ResponseDto> Catalogo(ImagenesBLL bll, CancellationToken ct)
        => Consultar(() => bll.Catalogo(ct), "productos del catálogo");

    public async Task<ResponseDto> Subir(int id, HttpRequest req, ImagenesBLL bll, CancellationToken ct)
    {
        try
        {
            if (req.ContentLength > ImagenesBLL.PesoMaximo)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, "La foto pesa demasiado. El máximo es 5 MB.", null);

            var r = await bll.Guardar(id, req.Body, ct);
            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Foto guardada", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al subir la foto del producto {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "No se pudo guardar la foto.", null);
        }
    }

    public async Task<ResponseDto> Quitar(int id, ImagenesBLL bll, CancellationToken ct)
    {
        try
        {
            var r = await bll.Quitar(id, ct);
            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Foto quitada", null)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al quitar la foto del producto {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "No se pudo quitar la foto.", null);
        }
    }

    /// <summary>
    /// La URL lleva ?v=version y cambia con cada foto nueva, así que se puede
    /// cachear un año: el POS no vuelve a descargar las fotos en cada venta.
    /// </summary>
    public async Task<IResult> Servir(int id, HttpContext http, ImagenesBLL bll, CancellationToken ct)
    {
        var foto = await bll.Abrir(id, ct);
        if (foto is null) return Results.NotFound();

        http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
        return Results.File(foto.Value.Ruta, foto.Value.Tipo);
    }
}
