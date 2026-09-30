using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Correo;

/// <summary>
/// HTML de los correos. Tablas y estilos en línea porque es lo único que
/// Gmail y Outlook respetan por igual. Todo texto que venga de la base pasa
/// por <see cref="H"/>: un nombre de cliente con "&lt;" no puede romper el
/// correo.
/// </summary>
public static class PlantillaCorreo
{
    private const string Tallo = "#17392c";
    private const string Fucsia = "#c8306e";
    private const string Suave = "#6b5e57";
    private const string Linea = "#eadfe3";

    public static string H(string? texto) => WebUtility.HtmlEncode(texto ?? "");

    /// <summary>$25.000 sin depender de la cultura instalada en el contenedor.</summary>
    public static string Pesos(long monto)
        => (monto < 0 ? "-$" : "$") +
           Math.Abs(monto).ToString("#,0", CultureInfo.InvariantCulture).Replace(',', '.');

    private static readonly TimeZoneInfo? Chile = BuscarZona();

    private static TimeZoneInfo? BuscarZona()
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("America/Santiago"); }
        catch { return null; }
    }

    /// <summary>La base guarda en UTC; el correo se lee en hora de Chile.</summary>
    public static string Fecha(DateTime? f)
    {
        if (f is null) return "—";
        var d = f.Value;
        if (Chile is not null && d.Kind != DateTimeKind.Local)
            d = TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(d, DateTimeKind.Utc), Chile);
        return d.ToString("dd-MM-yyyy HH:mm", CultureInfo.InvariantCulture);
    }

    public static string Medio(MedioPago m) => m switch
    {
        MedioPago.efectivo => "Efectivo",
        MedioPago.debito => "Débito",
        MedioPago.credito => "Crédito",
        MedioPago.transferencia => "Transferencia",
        _ => m.ToString()
    };

    /// <summary>Un dato del JSON de configuración del local, o null.</summary>
    public static string? Dato(JsonElement? local, string clave)
        => local is { ValueKind: JsonValueKind.Object } l
           && l.TryGetProperty(clave, out var v)
           && v.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(v.GetString())
            ? v.GetString()
            : null;

    // ─── Piezas ───────────────────────────────────────────────

    /// <summary>Filas etiqueta / valor. El valor ya viene escapado o es HTML propio.</summary>
    public static string Datos(params (string Etiqueta, string ValorHtml)[] filas)
    {
        var sb = new StringBuilder($"<table role=\"presentation\" width=\"100%\" style=\"border-collapse:collapse;margin:0 0 16px\">");
        foreach (var (e, v) in filas)
            sb.Append($"<tr><td style=\"padding:6px 0;color:{Suave};font-size:14px;width:45%\">{H(e)}</td>" +
                      $"<td style=\"padding:6px 0;font-size:14px;text-align:right\">{v}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    /// <summary>Tabla de líneas: descripción, cantidad y monto.</summary>
    public static string Lineas(IEnumerable<(string Descripcion, long Cantidad, long Monto)> lineas)
    {
        var sb = new StringBuilder(
            $"<table role=\"presentation\" width=\"100%\" style=\"border-collapse:collapse;margin:0 0 16px\">" +
            $"<tr><td style=\"padding:6px 0;color:{Suave};font-size:12px;border-bottom:1px solid {Linea}\">Detalle</td>" +
            $"<td style=\"padding:6px 0;color:{Suave};font-size:12px;border-bottom:1px solid {Linea};text-align:center\">Cant.</td>" +
            $"<td style=\"padding:6px 0;color:{Suave};font-size:12px;border-bottom:1px solid {Linea};text-align:right\">Monto</td></tr>");
        foreach (var (d, c, m) in lineas)
            sb.Append($"<tr><td style=\"padding:8px 0;font-size:14px;border-bottom:1px solid {Linea}\">{H(d)}</td>" +
                      $"<td style=\"padding:8px 0;font-size:14px;border-bottom:1px solid {Linea};text-align:center\">{c}</td>" +
                      $"<td style=\"padding:8px 0;font-size:14px;border-bottom:1px solid {Linea};text-align:right\">{Pesos(m)}</td></tr>");
        return sb.Append("</table>").ToString();
    }

    public static string Total(string etiqueta, long monto)
        => $"<p style=\"margin:0 0 16px;font-size:20px;font-weight:700;text-align:right\">" +
           $"{H(etiqueta)} {Pesos(monto)}</p>";

    public static string Parrafo(string textoHtml)
        => $"<p style=\"margin:0 0 16px;font-size:15px;line-height:1.5\">{textoHtml}</p>";

    /// <summary>Un recuadro para lo que requiere atención.</summary>
    public static string Destacado(string textoHtml)
        => $"<p style=\"margin:0 0 16px;padding:12px 14px;border-radius:8px;background:#fbe4ec;" +
           $"color:{Fucsia};font-size:14px;line-height:1.5\">{textoHtml}</p>";

    /// <summary>El marco de todos los correos: encabezado, contenido y pie.</summary>
    public static string Pagina(string titulo, string contenidoHtml, string pieHtml)
        => "<!doctype html><html lang=\"es\"><head><meta charset=\"utf-8\"></head>" +
           $"<body style=\"margin:0;padding:24px 12px;background:#fcedf1;font-family:Arial,Helvetica,sans-serif;color:{Tallo}\">" +
           "<table role=\"presentation\" width=\"100%\" style=\"max-width:560px;margin:0 auto;background:#ffffff;border-radius:12px\">" +
           "<tr><td style=\"padding:24px 24px 8px\">" +
           $"<p style=\"margin:0;font-size:14px;font-weight:700;color:{Fucsia}\">Floristería Colibrí</p>" +
           $"<h1 style=\"margin:8px 0 16px;font-size:22px;line-height:1.25\">{H(titulo)}</h1>" +
           "</td></tr>" +
           $"<tr><td style=\"padding:0 24px 8px\">{contenidoHtml}</td></tr>" +
           $"<tr><td style=\"padding:16px 24px 24px;border-top:1px solid {Linea};color:{Suave};font-size:12px;line-height:1.5\">{pieHtml}</td></tr>" +
           "</table></body></html>";
}
