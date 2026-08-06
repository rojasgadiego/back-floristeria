namespace Colibri.Api.Features.Lotes;

public static class LotesExtensions
{
    public static IServiceCollection AgregarLotes(
        this IServiceCollection servicios, IConfiguration config)
    {
        servicios.Configure<LotesOpciones>(config.GetSection(LotesOpciones.Seccion));
        servicios.AddScoped<ILotesService, LotesService>();
        return servicios;
    }
}