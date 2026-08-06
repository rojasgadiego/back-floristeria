using Colibri.Api.Common.Inventario;

namespace Colibri.Api.Features.Inventario;

public static class InventarioExtensions
{
    public static IServiceCollection AgregarInventario(this IServiceCollection servicios)
    {
        // Compartido con Ventas: la regla de consumo tiene que ser una sola
        servicios.AddScoped<IConsumidorLotes, ConsumidorLotes>();
        servicios.AddScoped<IInventarioService, InventarioService>();
        return servicios;
    }
}