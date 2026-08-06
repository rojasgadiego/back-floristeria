using Colibri.Api.Common.Seguridad;

namespace Colibri.Api.Features.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AgregarAuth(
        this IServiceCollection servicios, IConfiguration config)
    {
        servicios.Configure<JwtOpciones>(config.GetSection(JwtOpciones.Seccion));
        servicios.AddScoped<IAuthService, AuthService>();
        return servicios;
    }
}
