using System.Text;
using Colibri.Api.Endpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Colibri.Api.Auth;

public static class JwtConfig
{
    public static IServiceCollection AgregarSeguridad(
        this IServiceCollection services, IConfiguration cfg)
    {
        var llave = cfg["Jwt:Key"];

        // "" no es null: sin este chequeo la llave vacía pasa el ?? throw y
        // revienta recién en el primer request, con un IDX10703 que no menciona
        // la configuración.
        if (string.IsNullOrWhiteSpace(llave))
            throw new InvalidOperationException(
                "Falta Jwt:Key. En local va en appsettings.Development.json; en el VPS, como Jwt__Key.");

        if (Encoding.UTF8.GetByteCount(llave) < 32)
            throw new InvalidOperationException("Jwt:Key debe tener al menos 32 caracteres para HS256.");

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(o =>
            {
                // ─────────────────────────────────────────────────────────
                // ESTAS TRES LÍNEAS SON EL PUNTO IMPORTANTE.
                //
                // Por defecto .NET "traduce" los claims entrantes a URIs de
                // schemas.xmlsoap.org: "sub" pasa a ser nameidentifier, "role"
                // pasa a ser .../claims/role. El resultado es que
                // UsuarioActual() busca "sub", no lo encuentra, y devuelve 0
                // —todos los movimientos firmados por nadie—; y RequireRole
                // busca en un claim que tampoco existe con ese nombre, así que
                // todo responde 403 sin explicar por qué.
                //
                // MapInboundClaims = false deja los nombres tal cual vienen, y
                // las dos líneas siguientes le dicen al validador dónde mirar.
                // ─────────────────────────────────────────────────────────
                o.MapInboundClaims = false;

                o.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = cfg["Jwt:Issuer"],
                    ValidAudience = cfg["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(llave)),

                    NameClaimType = "nombre",
                    RoleClaimType = "role",

                    // Por defecto .NET regala 5 minutos de gracia a los vencidos.
                    ClockSkew = TimeSpan.FromSeconds(30)
                };
            });

        // Las etiquetas coinciden con el enum rol_usuario: admin, vendedor, bodega.
        services.AddAuthorizationBuilder()
            .AddPolicy(Politicas.VerInventario, p => p.RequireAuthenticatedUser())
            .AddPolicy(Politicas.Inventario, p => p.RequireRole("admin", "bodega"))
            .AddPolicy(Politicas.Vender, p => p.RequireRole("admin", "vendedor"))
            .AddPolicy(Politicas.Admin, p => p.RequireRole("admin"));

        services.AddSingleton<IJwtTokenService, JwtTokenService>();

        return services;
    }
}
