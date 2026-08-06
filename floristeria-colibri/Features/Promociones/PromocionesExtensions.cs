namespace Colibri.Api.Features.Promociones;

public static class PromocionesExtensions
{
    public static IServiceCollection AgregarPromociones(this IServiceCollection servicios)
    {
        servicios.AddScoped<IPromocionesService, PromocionesService>();
        return servicios;
    }
}