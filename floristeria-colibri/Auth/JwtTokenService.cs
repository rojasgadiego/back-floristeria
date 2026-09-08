using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Colibri.Api.Models.Tablas;
using Microsoft.IdentityModel.Tokens;

namespace Colibri.Api.Auth;

public interface IJwtTokenService
{
    (string Token, DateTime ExpiraEn) Generar(Usuario usuario);
}

/// <summary>
/// Emite los tokens. Los nombres de los claims tienen que calzar EXACTAMENTE
/// con lo que espera el validador en JwtConfig y con lo que lee
/// EndpointsBase.UsuarioActual(): "sub" para el id y "role" para el rol.
///
/// Por eso ambos lados llevan MapInboundClaims = false. Ver el comentario largo
/// en JwtConfig.
/// </summary>
public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _cfg;

    public JwtTokenService(IConfiguration cfg) => _cfg = cfg;

    public (string Token, DateTime ExpiraEn) Generar(Usuario usuario)
    {
        var llave = _cfg["Jwt:Key"]!;
        var minutos = _cfg.GetValue<int?>("Jwt:ExpiraMinutos") ?? 480;
        var expira = DateTime.UtcNow.AddMinutes(minutos);

        var claims = new List<Claim>
        {
            new("sub",    usuario.Id.ToString()),
            new("nombre", usuario.Nombre),
            new("email",  usuario.Email),
            new("role",   usuario.Rol.ToString()),
            // jti: identifica este token en particular. Hoy no se usa, pero si
            // algún día hay lista de revocación, es lo que se revoca.
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(llave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _cfg["Jwt:Issuer"],
            audience: _cfg["Jwt:Audience"],
            claims: claims,
            expires: expira,
            signingCredentials: credenciales);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }
}
