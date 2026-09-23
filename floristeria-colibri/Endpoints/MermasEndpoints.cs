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

        mer.MapPost("/", Registrar)
            .WithName("RegistrarMerma")
            .WithSummary("Saca varas del inventario. El costo lo pone el sistema, no el formulario.")
            .Produces<ResponseDto>(201).Produces(400);

        mer.MapPost("/lote/{loteId:int}/descartar", DescartarLote)
            .WithName("DescartarLote")
            .WithSummary("Da de baja el lote con lo que le quede. Es el destino de los rezagados.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400);

        // ═══ Con id ═══

        mer.MapGet("/{id:int}", Obtener)
            .WithName("ObtenerMerma")
            .Produces<ResponseDto>(200).Produces(404);

        mer.MapPost("/{id:int}/revertir", Revertir)
            .WithName("RevertirMerma")
            .WithSummary("Las varas vuelven a su lote. Solo si lo recuperado no se vendió.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400);
    }

    /// <summary>
    /// Null para el administrador (ve todas); el id del token para cualquier
    /// otro. Cada persona ve solo las mermas que registró, jamás las de otros.
    /// </summary>
    private static int? SoloDe(HttpContext http)
        => http.User.IsInRole("admin") ? null : UsuarioActual(http);

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

    /// <summary>
    /// El mensaje de éxito dice qué se recuperó, no solo qué se perdió: es lo
    /// que cambia la sensación de haber botado plata por la de haber salvado
    /// algo.
    /// </summary>
    public async Task<ResponseDto> Registrar(
        [FromBody] RegistrarMermaRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Registrar(peticion, UsuarioActual(http), SoloMostrador(http), ct),
            m => m.CantidadRecuperada > 0
                ? $"{m.Cantidad} de {m.Producto} · {m.CantidadRecuperada} recuperadas en {m.LoteRecuperacion}"
                : $"{m.Cantidad} de {m.Producto} · ${m.CostoPerdido:N0} de pérdida",
            HttpStatusCodes.Created, "registrar la merma");

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

    public async Task<ResponseDto> DescartarLote(
        int loteId, [FromBody] DescartarLoteRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.DescartarLote(loteId, peticion, UsuarioActual(http), ct),
            m => $"Lote {m.OrigenCodigo} dado de baja · {m.Cantidad} varas",
            HttpStatusCodes.Created, "descartar el lote");

    public async Task<ResponseDto> Revertir(
        int id, [FromBody] RevertirMermaRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Revertir(id, peticion?.Motivo ?? "", UsuarioActual(http), ct),
            m => m.DesarmeGrupo is not null
                ? "Desarme revertido · el armado volvió al stock y se anularon los lotes recuperados"
                : $"Merma revertida · {m.Cantidad} de {m.Producto} volvieron al inventario",
            HttpStatusCodes.Ok, "revertir la merma");

    public async Task<ResponseDto> Desarmar(
        int productoId, [FromBody] DesarmeRequest peticion, MermasBLL bll,
        HttpContext http, CancellationToken ct)
        => await Escribir(() => bll.Desarmar(productoId, peticion, UsuarioActual(http), ct),
            r => $"{r.Desarmados} × {r.Producto} desarmados · " +
                 $"{r.Recuperadas} varas recuperadas, {r.Perdidas} perdidas",
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
