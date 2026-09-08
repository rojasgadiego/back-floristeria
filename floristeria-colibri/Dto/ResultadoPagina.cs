using System.Text.Json.Serialization;

namespace Colibri.Api.Dto;

public class ResultadoPagina<T>
{
    [JsonPropertyName("items")]
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();

    [JsonPropertyName("total")]
    public long Total { get; init; }

    [JsonPropertyName("pagina")]
    public int Pagina { get; init; }

    // El front lo lee como porPagina; acá se llamó Tamano.
    [JsonPropertyName("porPagina")]
    public int Tamano { get; init; }

    [JsonPropertyName("totalPaginas")]
    public int TotalPaginas => Tamano <= 0 ? 0 : (int)Math.Ceiling(Total / (double)Tamano);

    [JsonIgnore]
    public bool HayMas => Pagina < TotalPaginas;
}