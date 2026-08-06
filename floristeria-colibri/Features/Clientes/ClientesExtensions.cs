namespace Colibri.Api.Features.Clientes;

public static class ClientesExtensions
{
    public static IServiceCollection AgregarClientes(this IServiceCollection servicios)
    {
        servicios.AddScoped<IClientesService, ClientesService>();
        return servicios;
    }
}