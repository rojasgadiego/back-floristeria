namespace Colibri.Api.Features.Usuarios;

public static class UsuariosExtensions
{
    public static IServiceCollection AgregarUsuarios(this IServiceCollection servicios)
    {
        servicios.AddScoped<IUsuariosService, UsuariosService>();
        return servicios;
    }
}
