namespace Colibri.Api.Features.Compras;

public static class ComprasExtensions
{
    public static IServiceCollection AgregarCompras(this IServiceCollection servicios)
    {
        servicios.AddScoped<IComprasService, ComprasService>();
        return servicios;
    }
}