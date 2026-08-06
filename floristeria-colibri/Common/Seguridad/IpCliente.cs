namespace Colibri.Api.Common.Seguridad;

public static class IpCliente
{
    /// <summary>
    /// IP real de quien hace la petición.
    ///
    /// Solo es correcta si UseForwardedHeaders ya corrió y el proxy está
    /// declarado como de confianza: ese middleware reemplaza RemoteIpAddress
    /// por la IP que viene en X-Forwarded-For. Por eso va primero en la
    /// tubería, antes que cualquier otra cosa.
    ///
    /// Nunca se lee el encabezado a mano: hacerlo saltaría la validación de
    /// proxies de confianza y dejaría que cualquiera declarara la IP que
    /// quisiera para saltarse el límite de intentos.
    /// </summary>
    public static string Obtener(HttpContext contexto)
        => contexto.Connection.RemoteIpAddress?.ToString() ?? "desconocida";
}