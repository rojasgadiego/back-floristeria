using Colibri.Api.Models.Tablas;

namespace Colibri.Api.Dto;

/// <summary>
/// Lo que recibe el front al entrar. Va el usuario completo para que pueda
/// pintar el nombre y decidir qué menús mostrar sin decodificar el token.
///
/// Ojo: el front decide qué MUESTRA según el rol; qué PUEDE hacer lo decide la
/// API. Esconder un botón no es seguridad.
/// </summary>
public class LoginResponse
{
    public string Token { get; init; } = string.Empty;
    public DateTime ExpiraEn { get; init; }
    public Usuario Usuario { get; init; } = new();
    public string[] Permisos { get; init; } = [];
}
