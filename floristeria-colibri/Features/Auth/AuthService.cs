using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

using Colibri.Api.Common;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Auth.Dtos;
using Colibri.Api.Context;

namespace Colibri.Api.Features.Auth;

public class AuthService : IAuthService
{
    // Hash de una contraseña cualquiera. Se verifica contra este cuando el
    // correo no existe, para que el tiempo de respuesta sea el mismo que con
    // un usuario real: si no, medir la demora revela qué correos están
    // registrados.
    private const string HashSenuelo = "$2b$10$jXHNzJMrSMTm2RxUAiYeP.OxRm4lzOBy6KtnV2IhxxCHh7aFpuQ8K";

    private const string CredencialesInvalidas = "Correo o contraseña incorrectos.";

    private readonly ColibriDbContext _db;
    private readonly JwtOpciones _jwt;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<AuthService> _log;

    public AuthService(
        ColibriDbContext db,
        IOptions<JwtOpciones> jwt,
        IUsuarioActual usuarioActual,
        ILogger<AuthService> log)
    {
        _db = db;
        _jwt = jwt.Value;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest peticion, CancellationToken ct = default)
    {
        var email = peticion.Email.Trim().ToLowerInvariant();

        var usuario = await _db.Usuarios
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        if (usuario is null)
        {
            // Se verifica igual para no acortar la respuesta
            BCrypt.Net.BCrypt.Verify(peticion.Password, HashSenuelo);
            _log.LogWarning("Intento de acceso con correo no registrado: {Email}", email);
            throw new ExcepcionNegocio(CredencialesInvalidas, StatusCodes.Status401Unauthorized);
        }

        if (!BCrypt.Net.BCrypt.Verify(peticion.Password, usuario.PasswordHash))
        {
            _log.LogWarning("Contraseña incorrecta para {Email}", email);
            throw new ExcepcionNegocio(CredencialesInvalidas, StatusCodes.Status401Unauthorized);
        }

        // Una cuenta bloqueada no entra aunque la clave sea correcta. Este
        // mensaje sí es distinto: quien ya tiene cuenta merece saber por qué
        // no puede entrar.
        if (!usuario.Activo)
        {
            throw new ExcepcionNegocio(
                "Esta cuenta está bloqueada. Habla con la administradora.",
                StatusCodes.Status403Forbidden);
        }

        usuario.UltimoAcceso = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var (token, expira) = GenerarToken(usuario);
        _log.LogInformation("Sesión iniciada: {Email} ({Rol})", usuario.Email, usuario.Rol);

        return new LoginResponse
        {
            Token = token,
            ExpiraEn = expira,
            Usuario = ADto(usuario)
        };
    }

    public async Task<SesionDto> SesionActualAsync(CancellationToken ct = default)
    {
        var id = _usuarioActual.IdRequerido();

        var usuario = await _db.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new ExcepcionNegocio(
                "La cuenta de esta sesión ya no existe.", StatusCodes.Status401Unauthorized);

        // El token sigue siendo válido aunque bloqueen la cuenta: acá se
        // detecta y se corta. Es el punto donde el front descubre el bloqueo.
        if (!usuario.Activo)
            throw new ExcepcionNegocio("Esta cuenta fue bloqueada.", StatusCodes.Status403Forbidden);

        return ADto(usuario);
    }

    public async Task CambiarPasswordPropiaAsync(
        CambiarPasswordRequest peticion, CancellationToken ct = default)
    {
        var id = _usuarioActual.IdRequerido();

        var usuario = await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct)
            ?? throw new NoEncontradoException("La cuenta");

        // Pedir la actual evita que alguien con la sesión abierta ajena
        // —un computador desatendido— se apropie de la cuenta.
        if (!BCrypt.Net.BCrypt.Verify(peticion.PasswordActual, usuario.PasswordHash))
            throw new ExcepcionNegocio("La contraseña actual no coincide.");

        if (peticion.PasswordActual == peticion.PasswordNueva)
            throw new ExcepcionNegocio("La contraseña nueva debe ser distinta de la actual.");

        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(peticion.PasswordNueva, workFactor: 12);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Contraseña cambiada por el propio usuario {Email}", usuario.Email);
    }

    /* ------------------------------------------------------------------ */

    private (string token, DateTimeOffset expira) GenerarToken(Usuario usuario)
    {
        var expira = DateTimeOffset.UtcNow.AddMinutes(_jwt.MinutosVigencia);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.Nombre),
            new(ClaimTypes.Email, usuario.Email),
            new(ClaimTypes.Role, usuario.Rol.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())
        };

        var credenciales = new SigningCredentials(
            new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.Clave)),
            SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer: _jwt.Emisor,
            audience: _jwt.Audiencia,
            claims: claims,
            notBefore: DateTime.UtcNow,
            expires: expira.UtcDateTime,
            signingCredentials: credenciales);

        return (new JwtSecurityTokenHandler().WriteToken(token), expira);
    }

    private static SesionDto ADto(Usuario u) => new()
    {
        Id = u.Id,
        Nombre = u.Nombre,
        Email = u.Email,
        Rol = u.Rol.ToString(),
        Permisos = PermisosDe(u.Rol)
    };

    /// <summary>
    /// Módulos visibles por rol. Es el equivalente de menuColibri.js del
    /// front: sirve para armar el menú, no para autorizar. La autorización
    /// real la hacen las políticas de cada endpoint.
    /// </summary>
    private static string[] PermisosDe(RolUsuario rol) => rol switch
    {
        RolUsuario.admin => new[]
        {
            "dashboard", "pos", "cotizaciones", "ventas", "inventario", "lotes",
            "compras", "mermas", "clientes", "promociones", "usuarios", "reportes",
            "configuracion"
        },
        RolUsuario.vendedor => new[]
        {
            "dashboard", "pos", "cotizaciones", "ventas", "inventario", "lotes", "clientes"
        },
        RolUsuario.bodega => new[]
        {
            "dashboard", "inventario", "lotes", "compras", "mermas", "reportes"
        },
        _ => Array.Empty<string>()
    };
}
