using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Filtros del libro mayor. Todos opcionales, igual que en la grilla.
///
/// Los int y las fechas van nulables porque con [AsParameters] un tipo de valor
/// no nulable se vuelve OBLIGATORIO en el query string aunque tenga
/// inicializador, y el request falla con 400 si el front no lo manda.
/// </summary>
public class MovimientoFiltro
{
    public int? ProductoId { get; set; }
    public TipoMovimiento? Tipo { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 50;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;

        TamanoReal = Tamano switch
        {
            null or < 1 => 50,
            > 200 => 200,
            _ => Tamano.Value
        };
    }
}

public class RecetaRequest
{
    public List<LineaRecetaRequest> Lineas { get; set; } = [];
}

public class LineaRecetaRequest
{
    public int ComponenteId { get; set; }
    public int Cantidad { get; set; }
}