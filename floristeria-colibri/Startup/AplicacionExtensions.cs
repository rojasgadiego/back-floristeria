using Colibri.Api.Common;
using Colibri.Api.Context;
using Colibri.Api.Context;
using Microsoft.EntityFrameworkCore;

namespace Colibri.Api.Startup;

public static class AplicacionExtensions
{
    /// <summary>
    /// Orden de la tubería. Acá el orden no es estilo: cambiarlo rompe cosas.
    /// </summary>
    public static WebApplication ConfigurarTuberia(this WebApplication app)
    {
        // 1. PRIMERO DE TODO. Corrige el esquema y la IP de origen antes de
        //    que cualquier otro middleware los lea. Si va después de
        //    UseHttpsRedirection, se produce un bucle de redirecciones; si va
        //    después del limitador, este cuenta la IP del proxy.
        app.UseForwardedHeaders();

        // 2. Cualquier excepción posterior debe salir con el formato de la API
        app.UsarManejadorDeExcepciones();

        app.UsarEncabezadosDeSeguridad();

        // 3. HSTS solo en producción: en desarrollo dejaría el navegador
        //    forzando HTTPS contra localhost, y eso después cuesta revertir.
        if (app.Environment.IsProduction())
        {
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // Swagger en /docs, separado del prefijo /api de los controladores.
        // Si comparten ruta, el enrutador gana y la interfaz no aparece.
        if (!app.Environment.IsProduction())
        {
            app.UseSwagger();
            app.UseSwaggerUI(c =>
            {
                c.SwaggerEndpoint("/swagger/v1/swagger.json", "Colibrí API v1");
                c.RoutePrefix = "docs";
                c.DocumentTitle = "Colibrí API";
            });
        }

        app.UseCors(ServiciosExtensions.PoliticaCors);
        app.UseRateLimiter();
        app.UseAuthentication();
        app.UseAuthorization();

        app.MapControllers();
        //app.MapHealthChecks("/salud");

        return app;
    }

    /// <summary>
    /// Comprueba la conexión al arrancar. Mejor fallar acá con un mensaje
    /// claro que en la primera petición con un 500.
    /// </summary>
    public static async Task<WebApplication> VerificarBaseDeDatosAsync(this WebApplication app)
    {
        using var alcance = app.Services.CreateScope();
        var log = alcance.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            var db = alcance.ServiceProvider.GetRequiredService<ColibriDbContext>();
            if (await db.Database.CanConnectAsync())
                log.LogInformation("Conectado a PostgreSQL");
            else
                log.LogError("No se pudo conectar a PostgreSQL. Revisa ConnectionStrings:Colibri.");
        }
        catch (Exception ex)
        {
            log.LogError(ex, "Fallo al conectar con la base de datos");
        }

        return app;
    }

    /// <summary>
    /// Revisa la configuración sensible al arrancar en producción.
    /// Detecta el descuido clásico: subir con los valores de ejemplo puestos.
    /// </summary>
    public static WebApplication VerificarConfiguracion(this WebApplication app)
    {
        if (!app.Environment.IsProduction()) return app;

        var log = app.Services.GetRequiredService<ILogger<Program>>();
        var config = app.Configuration;
        var fallas = new List<string>();

        var clave = config["Jwt:Clave"] ?? string.Empty;
        if (clave.Contains("CAMBIAR", StringComparison.OrdinalIgnoreCase))
            fallas.Add("Jwt:Clave sigue teniendo el valor de ejemplo.");
        if (clave.Length < 32)
            fallas.Add("Jwt:Clave debe tener al menos 32 caracteres.");

        var cadena = config.GetConnectionString("Colibri") ?? string.Empty;
        if (cadena.Contains("CAMBIAR", StringComparison.OrdinalIgnoreCase))
            fallas.Add("La cadena de conexión sigue teniendo la contraseña de ejemplo.");
        if (cadena.Contains("Username=postgres", StringComparison.OrdinalIgnoreCase))
            log.LogWarning(
                "La API se conecta como superusuario 'postgres'. Conviene un rol " +
                "sin permisos de DDL para acotar el daño ante una inyección.");

        var origenes = config.GetSection("Cors:Origenes").Get<string[]>() ?? [];
        if (origenes.Any(o => o.Contains("localhost")))
            log.LogWarning("Cors:Origenes todavía incluye localhost en producción.");

        if (fallas.Count > 0)
        {
            // Se detiene el arranque: una clave de ejemplo en producción
            // permite firmar un token de admin a cualquiera que la conozca,
            // y está publicada en este repositorio.
            throw new InvalidOperationException(
                "Configuración insegura para producción:" +
                Environment.NewLine + string.Join(Environment.NewLine, fallas.Select(f => " · " + f)));
        }

        return app;
    }
}