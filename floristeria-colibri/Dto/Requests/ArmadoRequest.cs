namespace Colibri.Api.Dto.Requests;

public class ArmarRequest
{
    public int Cantidad { get; set; } = 1;

    /// <summary>Baldes de flor recuperada que se autoriza usar. Se consumen primero.</summary>
    public List<int> LotesAutorizados { get; set; } = [];
}
