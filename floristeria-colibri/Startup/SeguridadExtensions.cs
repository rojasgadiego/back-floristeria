using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace Colibri.Api.Startup;

/// <summary>
/// Configuración de proxy inverso, HTTPS y encabezados de seguridad.
///
/// Los dos primeros temas son en realidad uno solo: si el proxy termina el
/// TLS y reenvía en HTTP, la aplicación ve peticiones "inseguras". Sin
/// X-Forwarded-Proto, UseHttpsRedirection responde 307 hacia HTTPS, el proxy
/// vuelve a reenviar en HTTP, y el navegador queda en un bucle infinito de
/// redirecciones. Y sin X-Forwarded-For, RemoteIpAddress es la IP del proxy:
/// el límite de intentos de login pasa a contar todo el tráfico junto, con
/// lo que un atacante no queda aislado y además puede dejar fuera al local.
/// </summary>
public static class SeguridadExtensions
{
    /// <summary>
    /// Habilita la lectura de los encabezados que envía el proxy.
    ///
    /// CUIDADO: estos encabezados los puede falsificar cualquiera. Solo se
    /// aceptan si vienen del proxy que se declara de confianza. Por eso NO se
    /// limpian KnownProxies y KnownNetworks para "que funcione": eso haría
    /// que cualquier cliente pudiera declarar la IP que quisiera y saltarse
    /// el límite de intentos.
    /// </summary>
    public static IServiceCollection AgregarProxyInverso(
        this IServiceCollection servicios, IConfiguration config, ILogger? log = null)
    {
        servicios.Configure<ForwardedHeadersOptions>(o =>
        {
            o.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                               | ForwardedHeaders.XForwardedProto
                               | ForwardedHeaders.XForwardedHost;

            // Solo se lee un salto. Si hay dos proxies encadenados, súbelo a 2:
            // con un límite mayor al real, un cliente puede inyectar saltos falsos.
            o.ForwardLimit = config.GetValue<int?>("ProxyInverso:Saltos") ?? 1;

            // Por defecto ASP.NET ya confía en loopback, que es el caso típico
            // cuando Nginx o Passenger corren en la misma máquina (cPanel).
            // Solo hay que agregar proxies si están en otro host.
            var proxies = config.GetSection("ProxyInverso:Proxies").Get<string[]>() ?? [];
            foreach (var texto in proxies)
            {
                if (IPAddress.TryParse(texto, out var ip))
                    o.KnownProxies.Add(ip);
                else
                    log?.LogWarning("ProxyInverso:Proxies contiene una IP no válida: {Valor}", texto);
            }

            // Redes en formato CIDR: "10.0.0.0/8"
            var redes = config.GetSection("ProxyInverso:Redes").Get<string[]>() ?? [];
            foreach (var texto in redes)
            {
                var partes = texto.Split('/');
                if (partes.Length == 2 &&
                    IPAddress.TryParse(partes[0], out var direccion) &&
                    int.TryParse(partes[1], out var prefijo))
                {
                    // Calificada: .NET 8 agregó System.Net.IPNetwork y choca
                    // con la de HttpOverrides, que es la que espera KnownNetworks.
                    o.KnownNetworks.Add(
                        new Microsoft.AspNetCore.HttpOverrides.IPNetwork(direccion, prefijo));
                }
                else
                {
                    log?.LogWarning("ProxyInverso:Redes contiene un CIDR no válido: {Valor}", texto);
                }
            }
        });

        return servicios;
    }

    /// <summary>
    /// HSTS: le dice al navegador que nunca más use HTTP con este dominio.
    ///
    /// Preload queda desactivado a propósito. Entrar a la lista de precarga
    /// de los navegadores es prácticamente irreversible: si después hay que
    /// servir algo por HTTP, no hay vuelta atrás en meses.
    /// </summary>
    public static IServiceCollection AgregarHsts(
        this IServiceCollection servicios, IConfiguration config)
    {
        servicios.AddHsts(o =>
        {
            o.MaxAge = TimeSpan.FromDays(config.GetValue<int?>("Https:DiasHsts") ?? 365);
            o.IncludeSubDomains = config.GetValue<bool?>("Https:IncluirSubdominios") ?? true;
            o.Preload = false;
        });

        servicios.AddHttpsRedirection(o =>
        {
            // 307 conserva el método y el cuerpo: un POST redirigido no se
            // convierte en GET y pierde los datos.
            o.RedirectStatusCode = StatusCodes.Status307TemporaryRedirect;

            var puerto = config.GetValue<int?>("Https:Puerto");
            if (puerto.HasValue) o.HttpsPort = puerto.Value;
        });

        return servicios;
    }

    /// <summary>
    /// Encabezados que cierran vectores de ataque del navegador. Son baratos
    /// y no cuestan nada en tiempo de ejecución.
    /// </summary>
    public static IApplicationBuilder UsarEncabezadosDeSeguridad(this IApplicationBuilder app)
    {
        return app.Use(async (contexto, siguiente) =>
        {
            var cabeceras = contexto.Response.Headers;

            // Impide que el navegador adivine el tipo de contenido: sin esto,
            // un archivo subido puede terminar ejecutándose como script.
            cabeceras["X-Content-Type-Options"] = "nosniff";

            // La API no se muestra dentro de un iframe. Corta el clickjacking.
            cabeceras["X-Frame-Options"] = "DENY";

            // No filtrar la URL completa hacia otros sitios: las rutas de la
            // API llevan identificadores que no tienen por qué salir.
            cabeceras["Referrer-Policy"] = "no-referrer";

            cabeceras["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";

            // La API solo devuelve JSON: nada debe cargarse ni ejecutarse.
            // Protege sobre todo a la página de Swagger.
            if (!contexto.Request.Path.StartsWithSegments("/docs"))
                cabeceras["Content-Security-Policy"] = "default-src 'none'; frame-ancestors 'none'";

            await siguiente();
        });
    }
}