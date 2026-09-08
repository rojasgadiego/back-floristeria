using System.Text.Json.Serialization;

namespace Colibri.Api.Dto;

/// <summary>
/// Los nombres JSON son los que el front ya esperaba (esSobre busca `exito` y
/// `datos`). En C# se mantienen en inglés porque así está escrito el resto del
/// backend; el atributo traduce solo en el borde.
///
/// `errores` va aparte de `mensaje`: el mensaje es la frase que se muestra, y
/// errores es para validaciones campo por campo. Hoy siempre viaja vacío, pero
/// el front ya lo contempla.
/// </summary>
public class ResponseDto
{
    [JsonPropertyName("exito")]
    public bool Success => StatusCode is >= 200 and < 300;

    [JsonPropertyName("datos")]
    public object? Data { get; set; }

    [JsonPropertyName("mensaje")]
    public string Message { get; set; } = string.Empty;

    [JsonPropertyName("errores")]
    public string[] Errores { get; set; } = [];

    [JsonPropertyName("statusCode")]
    public int StatusCode { get; set; }
}