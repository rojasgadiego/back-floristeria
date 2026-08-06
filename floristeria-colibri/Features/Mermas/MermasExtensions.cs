namespace Colibri.Api.Features.Mermas;

public static class MermasExtensions
{
    public static IServiceCollection AgregarMermas(this IServiceCollection servicios)
    {
        servicios.AddScoped<IMermasService, MermasService>();
        return servicios;
    }
}