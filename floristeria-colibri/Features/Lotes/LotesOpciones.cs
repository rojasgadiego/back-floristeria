namespace Colibri.Api.Features.Lotes;

/// <summary>Ajustes de la etiqueta y del código QR.</summary>
public class LotesOpciones
{
    public const string Seccion = "Lotes";

    /// <summary>
    /// Base de la URL que se codifica en el QR. El código del lote se agrega
    /// al final: https://colibri.cl/l/LOT-000002
    ///
    /// En el papel no vive ningún dato, solo la identidad del lote: todo lo
    /// demás está en la base. Por eso una etiqueta perdida se reimprime.
    /// </summary>
    public string UrlBase { get; set; } = "https://colibri.cl/l";

    /// <summary>
    /// Píxeles por módulo del QR. 8 da unos 250 px, suficiente para leerlo
    /// impreso en una etiqueta pequeña.
    /// </summary>
    public int PixelesPorModulo { get; set; } = 8;
}