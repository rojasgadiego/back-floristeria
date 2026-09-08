using System.Globalization;
using System.Net;
using System.Text;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Arma la hoja de etiquetas lista para imprimir.
///
/// Se genera en el servidor y no en el front porque imprimir es donde los
/// navegadores más se diferencian: márgenes, saltos de página y escalado.
/// Una hoja con @page y medidas en milímetros sale igual en Chrome, en
/// Firefox y en el equipo de la florería.
///
/// Los QR van embebidos como data URI: la hoja se puede guardar, mandar por
/// correo o abrir sin conexión, y sigue funcionando.
/// </summary>
public static class EtiquetasHtml
{
    private static readonly CultureInfo Cl = new("es-CL");

    public static string Construir(IEnumerable<LoteEtiqueta> lotes, string titulo)
    {
        var lista = lotes.ToList();
        var sb = new StringBuilder();

        sb.Append("""
<!doctype html>
<html lang="es">
<head>
<meta charset="utf-8">
<title>Etiquetas</title>
<style>
  /* Medidas en milímetros: es la única unidad que el navegador traduce a
     papel sin sorpresas. Cuatro columnas de 47mm entran en A4 con margen. */
  @page { size: A4; margin: 8mm; }

  * { box-sizing: border-box; }

  body {
    margin: 0;
    font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif;
    color: #111;
  }

  .barra {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 12px;
    padding: 10px 4px 14px;
    border-bottom: 1px solid #ddd;
    margin-bottom: 10px;
  }

  .barra h1 { margin: 0; font-size: 15px; }
  .barra .sub { font-size: 12px; color: #666; }

  .btn {
    padding: 8px 16px;
    border: 1px solid #111;
    border-radius: 6px;
    background: #111;
    color: #fff;
    font: inherit;
    font-size: 13px;
    cursor: pointer;
  }

  /* La barra es para la pantalla: en el papel solo van las etiquetas. */
  @media print { .barra { display: none; } }

  .hoja {
    display: grid;
    grid-template-columns: repeat(4, 47mm);
    gap: 3mm;
  }

  .et {
    width: 47mm;
    height: 36mm;
    border: 1px dashed #bbb;
    border-radius: 2mm;
    padding: 2mm;
    display: flex;
    gap: 2mm;
    align-items: center;
    /* Que una etiqueta no quede partida entre dos hojas. */
    break-inside: avoid;
    page-break-inside: avoid;
  }

  .et img { width: 24mm; height: 24mm; flex-shrink: 0; }

  .datos { min-width: 0; display: flex; flex-direction: column; gap: 0.6mm; }

  .prod {
    font-size: 8pt;
    font-weight: 700;
    line-height: 1.15;
    /* Dos líneas y corta: "Arreglo en jarrón grande edición primavera" no
       puede empujar la fecha fuera de la etiqueta. */
    display: -webkit-box;
    -webkit-line-clamp: 2;
    -webkit-box-orient: vertical;
    overflow: hidden;
  }

  .cod { font-size: 7.5pt; font-weight: 700; letter-spacing: .3px; font-variant-numeric: tabular-nums; }
  .lin { font-size: 6.5pt; color: #444; font-variant-numeric: tabular-nums; }
  .vence { font-weight: 700; }
  .pronto { color: #b45309; }
  .vencido { color: #b91c1c; }
</style>
</head>
<body>
""");

        sb.Append("<div class=\"barra\"><div><h1>")
          .Append(WebUtility.HtmlEncode(titulo))
          .Append("</h1><div class=\"sub\">")
          .Append(lista.Count).Append(" etiqueta(s) · generadas el ")
          .Append(DateTime.Now.ToString("dd-MM-yyyy HH:mm", Cl))
          .Append("</div></div>")
          .Append("<button class=\"btn\" onclick=\"window.print()\">Imprimir</button></div>");

        sb.Append("<div class=\"hoja\">");

        foreach (var l in lista)
        {
            var qr = QRCodeHelper.GenerarDataUri(l.Qr, 6);

            sb.Append("<div class=\"et\">")
              .Append("<img src=\"").Append(qr).Append("\" alt=\"\">")
              .Append("<div class=\"datos\">")
              .Append("<div class=\"prod\">").Append(WebUtility.HtmlEncode(l.Producto)).Append("</div>")
              .Append("<div class=\"cod\">").Append(WebUtility.HtmlEncode(l.Codigo)).Append("</div>")
              .Append("<div class=\"lin\">").Append(l.VarasIniciales).Append(" varas</div>")
              .Append("<div class=\"lin\">Ing. ").Append(l.FechaIngreso.ToString("dd-MM-yy", Cl)).Append("</div>");

            if (l.FechaVencimiento is { } vence)
            {
                // El vencimiento es lo único que el vendedor mira sin escanear,
                // así que va con color: rojo si ya pasó, ámbar si queda poco.
                var clase = l.DiasRestantes switch
                {
                    < 0 => "lin vence vencido",
                    <= 2 => "lin vence pronto",
                    _ => "lin vence"
                };

                sb.Append("<div class=\"").Append(clase).Append("\">Vence ")
                  .Append(vence.ToString("dd-MM-yy", Cl)).Append("</div>");
            }

            sb.Append("</div></div>");
        }

        sb.Append("</div></body></html>");
        return sb.ToString();
    }
}