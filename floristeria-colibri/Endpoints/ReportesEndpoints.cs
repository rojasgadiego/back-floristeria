using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de REPORTES.
///
/// El panel lo ve cualquiera autenticado: es la primera pantalla y todos
/// necesitan saber si hay caja abierta y qué hay que atender.
///
/// Resultado, productos y equipo son solo de administración: son márgenes y
/// comportamiento de personas. El de inventario pide VerInventario, que es
/// lo mismo que ver la grilla de productos.
/// </summary>
public class ReportesEndpoints : EndpointsBase
{
    public ReportesEndpoints(ILogger<ReportesEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // Dos grupos sobre la misma ruta: las políticas del grupo y las del
        // endpoint se SUMAN —todas tienen que cumplirse—, así que un endpoint
        // no puede "abrirse" dentro de un grupo de administrador. Con un solo
        // grupo, el panel quedaba solo para admin.
        var abiertos = app.MapGroup("/api/reportes")
            .WithTags("Reportes")
            .AddEndpointFilter<RespuestaFilter>();

        var rep = app.MapGroup("/api/reportes")
            .WithTags("Reportes")
            .RequireAuthorization(Politicas.Admin)
            .AddEndpointFilter<RespuestaFilter>();

        // Lo ve todo el equipo, pero cada uno con sus números: ver Panel().
        abiertos.MapGet("/panel", Panel)
            .WithName("PanelInicio")
            .WithSummary("Cómo va hoy, qué hay que atender y qué viene")
            .RequireAuthorization()
            .Produces<ResponseDto>(200);

        abiertos.MapGet("/inventario", Inventario)
            .WithName("ValorInventario")
            .WithSummary("La foto de hoy: cuánto hay y cuánto vale")
            .RequireAuthorization(Politicas.VerInventario)
            .Produces<ResponseDto>(200);

        // Solo admin: trae costo y utilidad del turno completo.
        rep.MapGet("/turno/{cajaId:int}", Turno)
            .WithName("DesgloseTurno")
            .Produces<ResponseDto>(200).Produces(404);

        rep.MapGet("/resultado", Resultado)
            .WithName("ResultadoPeriodo")
            .WithSummary("Ingresos, costo real y margen. Sin fechas, últimos 30 días.")
            .Produces<ResponseDto>(200);

        rep.MapGet("/productos", Productos)
            .WithName("RendimientoProductos")
            .WithSummary("Qué deja plata. El orden es por utilidad, no por ingresos.")
            .Produces<ResponseDto>(200);

        rep.MapGet("/equipo", Equipo)
            .WithName("RendimientoEquipo")
            .WithSummary("Ventas y diferencias de caja por persona")
            .Produces<ResponseDto>(200);
    }

    /// <summary>
    /// El administrador ve el local; un vendedor, solo sus boletas; quien no
    /// vende (bodega), ni ventas ni caja. Lo decide el token, nunca el cliente.
    /// </summary>
    public async Task<ResponseDto> Panel(ReportesBLL bll, HttpContext http, CancellationToken ct)
    {
        var u = http.User;
        var (soloDe, alcance) =
            u.IsInRole("admin")    ? ((int?)null, "local")
          : u.IsInRole("vendedor") ? (UsuarioActual(http), "personal")
          :                          (UsuarioActual(http), "ninguno");

        return await ConsultarUno(() => bll.ObtenerPanel(soloDe, alcance, ct),
            "Panel", "No se pudo armar el panel.");
    }

    public async Task<ResponseDto> Resultado(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Resultado(desde, hasta, ct),
            "Resultado", "No se pudo calcular el resultado.");

    public async Task<ResponseDto> Productos(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, [FromQuery] int? limite,
        ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Productos(desde, hasta, limite, ct),
            "Rendimiento", "No se pudo calcular el rendimiento.");

    public async Task<ResponseDto> Inventario(ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Inventario(ct),
            "Inventario", "No se pudo valorizar el inventario.");

    public async Task<ResponseDto> Equipo(
        [FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta,
        ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Equipo(desde, hasta, ct),
            "Equipo", "No se pudo calcular el rendimiento del equipo.");

    public async Task<ResponseDto> Turno(int cajaId, ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.Turno(cajaId, ct),
            "Turno", $"No existe la caja {cajaId}.");
}
