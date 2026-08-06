namespace Colibri.Api.Features.Configuracion;

public static class ConfiguracionExtensions
{
    public static IServiceCollection AgregarConfiguracion(this IServiceCollection servicios)
    {
        servicios.AddScoped<IConfiguracionService, ConfiguracionService>();
        return servicios;
    }
}