using Colibri.Api.Common;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Npgsql;
using System.Collections.Concurrent;
using System.Text;
using System.Threading.RateLimiting;

namespace Colibri.Api.Startup;

/// <summary>
/// Registro de servicios, separado por tema. Program.cs se lee de un vistazo
/// y cada bloque se puede tocar sin leer el resto.
/// </summary>
public static class ServiciosExtensions
{
    public const string PoliticaCors = "front";
    public const string LimiteLogin = "login";

    public static IServiceCollection AgregarBaseDeDatos(
        this IServiceCollection servicios, IConfiguration config, IWebHostEnvironment entorno)
    {
        var cadena = config.GetConnectionString("Colibri")
            ?? throw new InvalidOperationException(
                "Falta la cadena de conexión 'Colibri' en appsettings.json.");

        // Los enum de PostgreSQL se registran en el origen de datos. Sin esto
        // Npgsql no sabe convertir rol_usuario ni tipo_producto y falla al consultar.
        var constructor = new NpgsqlDataSourceBuilder(cadena);
        constructor.MapEnum<RolUsuario>("rol_usuario");
        constructor.MapEnum<TipoProducto>("tipo_producto");
        constructor.MapEnum<TipoMovimiento>("tipo_movimiento");
        constructor.MapEnum<MedioPago>("medio_pago");
        constructor.MapEnum<EstadoCaja>("estado_caja");
        constructor.MapEnum<TipoPromocion>("tipo_promocion");
        constructor.MapEnum<AlcancePromocion>("alcance_promocion");
        constructor.MapEnum<EstadoCotizacion>("estado_cotizacion");
        constructor.MapEnum<TipoConsumo>("tipo_consumo");
        constructor.MapEnum<TipoPresentacion>("tipo_presentacion");
        constructor.MapEnum<EstadoLote>("estado_lote");
        constructor.MapEnum<EstadoCompra>("estado_compra");

        var fuente = constructor.Build();
        servicios.AddSingleton(fuente);

        servicios.AddDbContext<ColibriDbContext>(opciones =>
        {
            opciones.UseNpgsql(fuente);
            if (entorno.IsDevelopment())
            {
                opciones.EnableDetailedErrors();
                opciones.EnableSensitiveDataLogging();
            }
        });

        //servicios.AddHealthChecks().AddDbContextCheck<ColibriDbContext>();
        return servicios;
    }

    public static IServiceCollection AgregarAutenticacion(
        this IServiceCollection servicios, IConfiguration config)
    {
        var jwt = config.GetSection("Jwt");
        var clave = jwt["Clave"]
            ?? throw new InvalidOperationException("Falta Jwt:Clave en la configuración.");

        // Una clave corta hace que el token sea trivial de falsificar.
        // Mejor fallar al arrancar que descubrirlo en producción.
        if (clave.Length < 32)
            throw new InvalidOperationException("Jwt:Clave debe tener al menos 32 caracteres.");

        servicios.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(opciones =>
            {
                opciones.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt["Emisor"],
                    ValidAudience = jwt["Audiencia"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(clave)),
                    ClockSkew = TimeSpan.FromMinutes(1)
                };
            });

        // Estas políticas replican los permisos de menuColibri.js del front.
        // Con una diferencia: en el navegador solo ordenaban la interfaz;
        // acá sí son una barrera.
        servicios.AddAuthorization(o =>
        {
            o.AddPolicy(Politicas.Admin,
                p => p.RequireRole(Roles.Admin));
            o.AddPolicy(Politicas.Caja,
                p => p.RequireRole(Roles.Admin, Roles.Vendedor));
            o.AddPolicy(Politicas.Inventario,
                p => p.RequireRole(Roles.Admin, Roles.Bodega));
            o.AddPolicy(Politicas.VerInventario,
                p => p.RequireRole(Roles.Admin, Roles.Vendedor, Roles.Bodega));
        });

        servicios.AddHttpContextAccessor();
        servicios.AddScoped<IUsuarioActual, UsuarioActual>();
        return servicios;
    }

    public static IServiceCollection AgregarControladores(this IServiceCollection servicios)
    {
        servicios.AddControllers(o => o.Filters.Add<FiltroValidacion>())
            .AddJsonOptions(o =>
            {
                o.JsonSerializerOptions.PropertyNamingPolicy =
                    System.Text.Json.JsonNamingPolicy.CamelCase;
                o.JsonSerializerOptions.DefaultIgnoreCondition =
                    System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
            });

        // El filtro propio ya devuelve el formato de la API
        servicios.Configure<ApiBehaviorOptions>(o => o.SuppressModelStateInvalidFilter = true);
        return servicios;
    }

    public static IServiceCollection AgregarDocumentacion(this IServiceCollection servicios)
    {
        servicios.AddEndpointsApiExplorer();
        servicios.AddSwaggerGen(c =>
        {
            // Swagger usa el nombre corto del tipo como identificador de
            // esquema, y dos módulos pueden tener un DTO que se llame igual
            // —VentaDto es una boleta en Ventas y los ajustes de IVA en
            // Configuracion—. Sin esto, la primera colisión tumba toda la
            // documentación con un 500.
            c.CustomSchemaIds(NombreDeEsquema);

            c.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "Colibrí API",
                Version = "v1",
                Description = "API de la florería Colibrí"
            });

            c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Pega solo el token, sin escribir 'Bearer'."
            });

            c.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme, Id = "Bearer"
                        }
                    },
                    Array.Empty<string>()
                }
            });

            var xml = Path.Combine(AppContext.BaseDirectory,
                $"{System.Reflection.Assembly.GetExecutingAssembly().GetName().Name}.xml");
            if (File.Exists(xml)) c.IncludeXmlComments(xml);
        });

        return servicios;
    }

    public static IServiceCollection AgregarCors(
        this IServiceCollection servicios, IConfiguration config)
    {
        servicios.AddCors(o => o.AddPolicy(PoliticaCors, p =>
        {
            var origenes = config.GetSection("Cors:Origenes").Get<string[]>()
                ?? new[] { "http://localhost:8080" };
            p.WithOrigins(origenes).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
        }));
        return servicios;
    }

    /// <summary>
    /// Limita los intentos de login. Sin esto, probar contraseñas por fuerza
    /// bruta contra /api/auth/login no tiene ningún costo para el atacante.
    /// </summary>
    public static IServiceCollection AgregarLimitesDePeticiones(this IServiceCollection servicios)
    {
        servicios.AddRateLimiter(opciones =>
        {
            opciones.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            opciones.AddPolicy(LimiteLogin, contexto =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: IpCliente.Obtener(contexto),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = 8,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0
                    }));
        });

        return servicios;
    }

    /// <summary>
    /// Identificador único para el esquema de un tipo: antepone el módulo al
    /// nombre, y resuelve los genéricos de forma legible.
    /// </summary>
    private static string NombreDeEsquema(Type tipo)
    {
        if (tipo.IsGenericType)
        {
            var baseName = tipo.Name[..tipo.Name.IndexOf('`')];
            var argumentos = string.Join("Y", tipo.GetGenericArguments().Select(NombreDeEsquema));
            return $"{baseName}De{argumentos}";
        }

        // Los tipos de Common no llevan prefijo: son transversales y únicos
        var ns = tipo.Namespace ?? string.Empty;
        const string marca = ".Features.";
        var i = ns.IndexOf(marca, StringComparison.Ordinal);

        if (i < 0) return tipo.Name;

        var modulo = ns[(i + marca.Length)..].Split('.')[0];

        // Si el nombre ya empieza con el módulo, no se repite:
        // ConfiguracionDto se queda igual, no ConfiguracionConfiguracionDto
        return tipo.Name.StartsWith(modulo, StringComparison.Ordinal)
            ? tipo.Name
            : modulo + tipo.Name;
    }
}
