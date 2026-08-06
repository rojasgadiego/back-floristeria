namespace Colibri.Api.Features.Cotizaciones;

public static class CotizacionesExtensions
{
    public static IServiceCollection AgregarCotizaciones(this IServiceCollection servicios)
    {
        servicios.AddScoped<ICotizacionesService, CotizacionesService>();
        return servicios;
    }
}