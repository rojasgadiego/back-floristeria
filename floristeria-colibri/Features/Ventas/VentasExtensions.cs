using Colibri.Api.Common.Ajustes;

namespace Colibri.Api.Features.Ventas;

public static class VentasExtensions
{
    public static IServiceCollection AgregarVentas(this IServiceCollection servicios)
    {
        // La configuración vive en la base y la usan varios módulos
        servicios.AddMemoryCache();
        // IConsumidorLotes lo registra AgregarInventario: es compartido
        servicios.AddScoped<IAjustesService, AjustesService>();

        servicios.AddScoped<ICajaService, CajaService>();
        servicios.AddScoped<IVentasService, VentasService>();
        return servicios;
    }
}