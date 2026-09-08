using QRCoder;

namespace Colibri.Api.Utils;

/// <summary>
/// Generación y lectura de los QR de lote.
///
/// DOS DECISIONES QUE DEFINEN SI LA ETIQUETA SIRVE:
///
/// 1. NIVEL DE CORRECCIÓN H (30%)
///    Estas etiquetas viven pegadas a un balde dentro de una cámara fría,
///    con humedad, manoseo y a veces una hoja encima. El nivel H permite
///    reconstruir el dato aunque casi un tercio del código esté dañado o
///    tapado. Con un payload tan corto, subir de Q a H apenas agrega
///    módulos: es corrección casi gratis.
///
/// 2. EL CONTENIDO VA EN MAYÚSCULAS Y CON '*' COMO SEPARADOR
///    El modo alfanumérico del QR solo acepta 0-9, A-Z, espacio y
///    $ % * + - . / : — es más compacto que el modo byte, así que el mismo
///    dato ocupa menos módulos y los cuadraditos quedan más grandes en el
///    mismo papel. Un separador fuera de esa lista —una barra vertical, por
///    ejemplo— tira todo a modo byte sin que nadie lo note hasta que las
///    etiquetas cuestan de leer a medio metro.
///
/// Formato:  CLB1*9*6*20260823*LOT-000009
///            │  │ │     │        └── código legible, para tipear si se rompe
///            │  │ │     └── fecha de ingreso
///            │  │ └── producto: permite detectar una etiqueta mal pegada
///            │  └── lote: la llave para descontar
///            └── versión del formato
/// </summary>
public static class QRCodeHelper
{
    /// <summary>Prefijo y versión. Si el formato cambia, sube a CLB2.</summary>
    public const string Prefijo = "CLB1";

    /// <summary>
    /// Píxeles por módulo. 10 da una etiqueta de ~3 cm que un teléfono lee a
    /// medio metro. Bajar de 6 empieza a fallar con cámaras de gama baja.
    /// </summary>
    public const int PixelesPorModulo = 10;

    /// <summary>
    /// El PNG del QR. Lanza si el contenido viene vacío: un QR en blanco es
    /// una etiqueta impresa que no sirve para nada y nadie lo nota hasta que
    /// el vendedor la escanea.
    /// </summary>
    public static byte[] GenerarPng(string contenido, int pixelesPorModulo = PixelesPorModulo)
    {
        if (string.IsNullOrWhiteSpace(contenido))
            throw new ArgumentException("El contenido del QR no puede estar vacío.", nameof(contenido));

        using var generador = new QRCodeGenerator();
        using var datos = generador.CreateQrCode(
            contenido,
            QRCodeGenerator.ECCLevel.H,
            // forceUtf8 = false: el contenido es ASCII puro a propósito.
            // Forzar UTF-8 agregaría una cabecera ECI innecesaria y sacaría
            // el código del modo alfanumérico.
            forceUtf8: false);

        using var png = new PngByteQRCode(datos);
        return png.GetGraphic(Math.Clamp(pixelesPorModulo, 4, 40));
    }

    /// <summary>
    /// Para incrustar en el HTML de la hoja de etiquetas. Así la hoja se
    /// puede guardar, mandar por correo o abrir sin conexión y sigue
    /// mostrando los códigos.
    /// </summary>
    public static string GenerarDataUri(string contenido, int pixelesPorModulo = 6)
        => "data:image/png;base64," + Convert.ToBase64String(GenerarPng(contenido, pixelesPorModulo));

    /// <summary>
    /// Saca el código legible de lo que sea que haya mandado el lector.
    ///
    /// Acepta las tres formas que llegan en el mesón:
    ///   · el QR completo, que es lo que manda el escáner
    ///   · el código pelado, cuando el vendedor lo tipea porque la etiqueta
    ///     se mojó o se despegó
    ///   · el formato viejo con '|', por si quedó alguna etiqueta impresa
    ///
    /// El SP hace lo mismo del lado de la base; esto sirve para limpiar antes
    /// de consultar —algunos lectores agregan un salto de línea al final— y
    /// para los mensajes de error.
    /// </summary>
    public static string? ExtraerCodigo(string? entrada)
    {
        if (string.IsNullOrWhiteSpace(entrada)) return null;

        var texto = entrada.Trim().ToUpperInvariant();

        // El separador es el quinto carácter: CLB1*... o CLB1|...
        if (texto.Length > Prefijo.Length && texto.StartsWith(Prefijo))
        {
            var separador = texto[Prefijo.Length];

            if (separador is '*' or '|')
            {
                var partes = texto.Split(separador);
                // El código legible es siempre el último campo.
                return partes.Length >= 5 ? partes[4] : null;
            }
        }

        return texto;
    }

    /// <summary>
    /// Arma el contenido del QR. Normalmente no hace falta —los SP lo
    /// devuelven ya armado en la columna `qr`—, pero sirve para pruebas y
    /// para verificar que ambos lados producen lo mismo.
    /// </summary>
    public static string Construir(int loteId, int productoId, DateOnly fechaIngreso, string codigo)
        => $"{Prefijo}*{loteId}*{productoId}*{fechaIngreso:yyyyMMdd}*{codigo.ToUpperInvariant()}";
}