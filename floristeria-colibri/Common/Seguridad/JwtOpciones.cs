namespace Colibri.Api.Common.Seguridad;

/// <summary>Sección Jwt de la configuración, tipada.</summary>
public class JwtOpciones
{
    public const string Seccion = "Jwt";

    public string Clave { get; set; } = string.Empty;
    public string Emisor { get; set; } = "colibri-api";
    public string Audiencia { get; set; } = "colibri-app";

    /// <summary>
    /// Vigencia del token. 480 minutos cubre un turno completo: si expirara
    /// a media tarde, la vendedora tendría que volver a entrar con un cliente
    /// esperando en el mesón.
    /// </summary>
    public int MinutosVigencia { get; set; } = 480;
}
