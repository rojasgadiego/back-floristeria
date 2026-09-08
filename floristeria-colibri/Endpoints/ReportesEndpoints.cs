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
        var rep = app.MapGroup("/api/reportes")
            .WithTags("Reportes")
            .RequireAuthorization(Politicas.Admin)
            .AddEndpointFilter<RespuestaFilter>();

        // El panel se abre a todos: RequireAuthorization sin política
        // reemplaza a la del grupo.
        rep.MapGet("/panel", Panel)
            .WithName("PanelInicio")
            .WithSummary("Cómo va hoy, qué hay que atender y qué viene")
            .RequireAuthorization()
            .Produces<ResponseDto>(200);

        rep.MapGet("/inventario", Inventario)
            .WithName("ValorInventario")
            .WithSummary("La foto de hoy: cuánto hay y cuánto vale")
            .RequireAuthorization(Politicas.VerInventario)
            .Produces<ResponseDto>(200);

        rep.MapGet("/turno/{cajaId:int}", Turno)
            .WithName("DesgloseTurno")
            .RequireAuthorization(Politicas.Vender)
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

    public async Task<ResponseDto> Panel(ReportesBLL bll, CancellationToken ct)
        => await ConsultarUno(() => bll.ObtenerPanel(ct),
            "Panel", "No se pudo armar el panel.");

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
