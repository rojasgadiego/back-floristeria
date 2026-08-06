namespace Colibri.Api.Features.Reportes;

public static class ReportesExtensions
{
    public static IServiceCollection AgregarReportes(this IServiceCollection servicios)
    {
        servicios.AddScoped<IReportesService, ReportesService>();
        return servicios;
    }
}