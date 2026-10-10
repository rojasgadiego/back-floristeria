using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de MERMAS.
///
/// Leer es de VerInventario. Registrar, de cualquiera: bodega y admin desde
/// cualquier origen, el vendedor solo desde una partida del mostrador (lo
/// decide la BLL). Descartar un lote y desarmar, de Admin y Bodega. Revertir,
/// ver los patrones y editar el catálogo de motivos, solo de Admin.
/// </summary>
public class MermasEndpoints : EndpointsBase
{
    public MermasEndpoints(ILogger<MermasEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        var mer = app.MapGroup("/api/mermas")
            .WithTags("Mermas")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        // ═══ Literales antes que /{id:int} ═══
        // Sin este orden, "escanear" o "patrones" no parsean como int y la
        // ruta específica nunca se alcanza.

        mer.MapGet("/", Listar)
            .WithName("ListarMermas")
            .Produces<ResponseDto>(200);

        mer.MapGet("/motivos", Motivos)
            .WithName("MotivosMerma")
            .WithSummary("El catálogo por categoría. ?todos=true incluye los apagados (solo admin).")
            .Produces<ResponseDto>(200);

        mer.MapPost("/motivos", CrearMotivo)
            .WithName("CrearMotivoMerma")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(201).Produces(400);

        mer.MapPut("/motivos/{id:int}", ActualizarMotivo)
            .WithName("ActualizarMotivoMerma")
            .WithSummary("Renombrar, recategorizar o apagar. Las mermas registradas no cambian.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        mer.MapGet("/resumen", Resumen)
            .WithName("ResumenMermas")
            .WithSummary("Separa lo perdido de lo recuperado. Sin fechas, últimos 30 días.")
            .Produces<ResponseDto>(200);

        mer.MapGet("/escanear/{codigo}", Escanear)
            .WithName("EscanearParaMerma")
            .WithSummary("Lee un lote o una partida y devuelve todo lo que el formulario necesita")
            .Produces<ResponseDto>(200).Produces(404);

        mer.MapGet("/umbral", Umbral)
            .WithName("UmbralMerma")
            .WithSummary("Desde cuánto hace falta autorización")
            .Produces<ResponseDto>(200);

        mer.MapPost("/solicitar-codigo", SolicitarCodigo)
            .WithName("SolicitarCodigoMerma")
            .WithSummary("Manda a la administración un código para autorizar una merma sobre el umbral")
            .Produces<ResponseDto>(200).Produces(400);

        mer.MapGet("/patrones", Patrones)
            .WithName("PatronesMerma")
            .WithSummary("Quién merma sin escanear, a qué hora, y las registradas a mano")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200);

        mer.MapGet("/desarme/{productoId:int}/plan", PlanDesarme)
            .WithName("PlanDesarme")
            .WithSummary("Qué varas salen al desarmar, según la receta")
            .Produces<ResponseDto>(200);

        mer.MapPost("/desarme/{productoId:int}", Desarmar)
            .WithName("Desarmar")
            .WithSummary("Desarma un armado y reparte cada componente entre perdido y recuperado")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        // ═══ Reportes con evidencia (sql/23) ═══
        // Reemplazan al registro suelto: toda merma lleva foto, y ya no hay
        // reversa de administración, solo "deshacer" en los 10 minutos.

        mer.MapPost("/reportes", RegistrarReporte)
            .WithName("RegistrarReporteMerma")
            .WithSummary("Una cosa o varias (incidente), con 1 a 3 fotos. El costo lo pone el sistema.")
            .Produces<ResponseDto>(201).Produces(400);

        mer.MapPost("/reportes/{id:int}/deshacer", DeshacerReporte)
            .WithName("DeshacerReporteMerma")
            .WithSummary("Solo quien lo registró, dentro de 10 minutos")
            .Produces<ResponseDto>(200).Produces(400);

        mer.MapGet("/revision", Pendientes)
            .WithName("MermasPorRevisar")
            .WithSummary("Reportes que nadie ha revisado, con lo necesario para mirarlos")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200);

        mer.MapPost("/reportes/{id:int}/revisado", MarcarRevisado)
            .WithName("MarcarReporteRevisado")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);

        mer.MapGet("/reportes/{id:int}/evidencias", EvidenciasReporte)
            .WithName("EvidenciasReporteMerma")
            .WithSummary("Las fotos del reporte, sin los bytes. De quien lo registró y de la administración.")
            .Produces<ResponseDto>(200).Produces(404);

        // ═══ Con id ═══

        mer.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerMerma")
            .Produces<ResponseDto>(200).Produces(404);


        // Las fotos devuelven bytes, no el sobre JSON: van en un grupo sin
        // RespuestaFilter.
        app.MapGroup("/api/mermas/evidencias")
            .WithTags("Mermas")
            .RequireAuthorization(Politicas.VerInventario)
            .MapGet("/{id:int}", VerEvidencia)
            .WithName("VerEvidenciaMerma")
            .Produces(200, contentType: "image/jpeg").Produces(404);
    }

    /// <summary>
    /// Null para el administrador (ve todas); el id del token para cualquier
    /// otro. Cada persona ve solo las mermas que registró, jamás las de otros.
    /// </summary>
    private static int? SoloDe(HttpContext http)
        => http.User.IsInRole("admin") ? null : UsuarioActual(http);

    /// <summary>Una admin no pide código de autorización: es quien lo dictaría.</summary>
    private static bool EsAdmin(HttpContext http) => http.User.IsInRole("admin");

    /// <summary>El vendedor merma solo desde el mostrador.</summary>
    private static bool SoloMostrador(HttpContext http)
        => !http.User.IsInRole("admin") && !http.User.IsInRole("bodega");

    #region Consultas

    public async Task<ResponseDto> Listar(
        [AsParameters] MermaFiltro filtro, MermasBLL bll, HttpContext http, CancellationToken ct)
        => await Consultar(() => bll.Listar(filtro, SoloDe(http), ct), "mermas");

    public async Task<ResponseDto> Obtener(int id, MermasBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(() => bll.Obtener(id, SoloDe(http), ct), "Merma", $"No existe la merma {id}.");

    public async Task<ResponseDto> Motivos(
        [FromQuery] bool? todos, MermasBLL bll, HttpContext http, CancellationToken ct)
        => await Consultar(() => bll.Motivos(todos == true && http.User.IsInRole("admin"), ct), "motivos");

    public async Task<ResponseDto> Resumen(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        MermasBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(() => bll.Resumen(desde, hasta, SoloDe(http), ct),
            "Resumen", "No se pudo calcular el resumen.");

    public async Task<ResponseDto> PlanDesarme(
        int productoId, [FromQuery] int? cantidad, MermasBLL bll, CancellationToken ct)
        => await Consultar(() => bll.PlanDesarme(productoId, cantidad ?? 1, ct),
            "plan de desarme");

    /// <summary>
    /// 200 aunque no se pueda mermar: el lote EXISTE y quien registra necesita
    /// ver por qué no sirve. Un 404 haría pensar que la etiqueta está mala.
    /// </summary>
    public async Task<ResponseDto> Escanear(
        string codigo, MermasBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var o = await bll.Escanear(codigo, SoloMostrador(http), ct);

            if (o is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.NotFound,
                    $"No hay ningún lote ni partida con el código {QRCodeHelper.ExtraerCodigo(codigo)}.",
                    null);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.Ok,
                o.PuedeMermar
                    ? $"{o.Producto} · quedan {o.Disponible}"
                    : o.MotivoBloqueo ?? "De acá no se puede mermar.",
                o);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al escanear {Codigo} para merma", codigo);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al leer el código.", null);
        }
    }

    public async Task<ResponseDto> Umbral(MermasBLL bll, CancellationToken ct)
        => await ConsultarUno(
            async () => new { umbral = await bll.Umbral(ct) },
            "Umbral", "No se pudo leer el umbral.");

    /// <summary>
    /// Solo admin. No es un reporte más: muestra el comportamiento de cada
    /// persona, y eso no debería estar a la vista de todo el equipo.
    /// </summary>
    public async Task<ResponseDto> Patrones(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        MermasBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Patrones(desde, hasta, ct),
            "Patrones", "No se pudieron calcular los patrones.");

    #endregion

    #region Escritura

    public async Task<ResponseDto> RegistrarReporte(
        [FromBody] RegistrarReporteRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(
            () => bll.RegistrarReporte(peticion, UsuarioActual(http), SoloMostrador(http), EsAdmin(http), ct),
            r => r.Mermas == 1
                ? "Merma registrada · la puedes deshacer durante 10 minutos"
                : $"Incidente registrado · {r.Mermas} cosas · lo puedes deshacer durante 10 minutos",
            HttpStatusCodes.Created, "registrar la merma");

    public async Task<ResponseDto> DeshacerReporte(
        int id, MermasBLL bll, HttpContext http, CancellationToken ct)
    {
        try
        {
            var error = await bll.DeshacerReporte(id, UsuarioActual(http), ct);
            return string.IsNullOrEmpty(error)
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Merma deshecha · todo volvió a su lugar", null)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, error, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al deshacer el reporte {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al deshacer la merma.", null);
        }
    }

    public async Task<ResponseDto> EvidenciasReporte(
        int id, MermasBLL bll, HttpContext http, CancellationToken ct)
        => await ConsultarUno(
            () => bll.Evidencias(id, UsuarioActual(http), EsAdmin(http), ct),
            "Evidencias", $"No existe el reporte {id}.");

    /// <summary>
    /// La foto tal cual, con caché privada: es evidencia, no un recurso
    /// público, pero tampoco cambia nunca.
    /// </summary>
    public async Task<IResult> VerEvidencia(
        int id, MermasBLL bll, HttpContext http, CancellationToken ct)
    {
        var f = await bll.Evidencia(id, UsuarioActual(http), EsAdmin(http), ct);
        if (f is null) return Results.NotFound();

        http.Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        return Results.File(f.Foto, f.TipoMime);
    }

    public async Task<ResponseDto> SolicitarCodigo(
        [FromBody] SolicitarCodigoMermaRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.SolicitarCodigo(peticion?.Valor ?? 0, UsuarioActual(http), ct),
            mensaje => mensaje,
            HttpStatusCodes.Ok, "pedir el código");

    public async Task<ResponseDto> CrearMotivo(
        [FromBody] MotivoMermaRequest peticion, MermasBLL bll, CancellationToken ct)
        => await Escribir(() => bll.CrearMotivo(peticion, ct),
            m => $"Motivo \"{m.Motivo}\" agregado",
            HttpStatusCodes.Created, "crear el motivo");

    public async Task<ResponseDto> ActualizarMotivo(
        int id, [FromBody] MotivoMermaRequest peticion, MermasBLL bll, CancellationToken ct)
        => await Escribir(() => bll.ActualizarMotivo(id, peticion, ct),
            m => m.Activo ? $"Motivo \"{m.Motivo}\" guardado" : $"Motivo \"{m.Motivo}\" apagado",
            HttpStatusCodes.Ok, "guardar el motivo");

    public async Task<ResponseDto> Pendientes(MermasBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Pendientes(ct), "reportes por revisar");

    public async Task<ResponseDto> MarcarRevisado(
        int id, [FromBody] RevisarReporteRequest? peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
    {
        try
        {
            var error = await bll.MarcarRevisado(id, UsuarioActual(http), peticion?.Nota, ct);
            return string.IsNullOrEmpty(error)
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Reporte revisado", null)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, error, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al marcar revisado el reporte {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, "Error al marcar como revisado.", null);
        }
    }

    public async Task<ResponseDto> Desarmar(
        int productoId, [FromBody] DesarmeRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Desarmar(productoId, peticion, UsuarioActual(http), EsAdmin(http), ct),
            r => $"{r.Desarmados} desarmado(s) · {r.Recuperadas} recuperadas, {r.Perdidas} perdidas · " +
                 "lo puedes deshacer durante 10 minutos",
            HttpStatusCodes.Created, "desarmar");

    #endregion

    /// <summary>
    /// El patrón de escritura, una sola vez: si salió bien devuelve los datos
    /// con su mensaje; si no, el texto del RAISE tal cual, sin reformular.
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
}
