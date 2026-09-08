using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Filtros de la grilla, todos opcionales. Null significa "no filtres por esto"
/// y llega así al SP, que resuelve todas las combinaciones en una sola función.
/// </summary>
public class ProductoFiltro
{
    public string? Buscar { get; set; }
    public int? CategoriaId { get; set; }
    public TipoProducto? Tipo { get; set; }
    public bool? Activo { get; set; }
    public bool? BajoMinimo { get; set; }
    public bool? ControlaLotes { get; set; }

    /// <summary>
    /// Para la vista del mostrador: trae únicamente lo que tiene existencias
    /// adelante. En la grilla de bodega va en null y se ve todo el catálogo.
    /// </summary>
    public bool? SoloEnVenta { get; set; }

    // Nulables a propósito: con [AsParameters], un tipo de valor no nulable se
    // vuelve OBLIGATORIO en el query string aunque tenga inicializador, y el
    // request falla con 400 si el front no lo manda. El default se resuelve
    // en Normalizar().
    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 50;

    /// <summary>
    /// Un tamaño de 5000 no lo pide un usuario, lo pide un script. El tope
    /// protege la memoria del VPS.
    /// </summary>
    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;

        TamanoReal = Tamano switch
        {
            null or < 1 => 50,
            > 200 => 200,
            _ => Tamano.Value
        };

        Buscar = string.IsNullOrWhiteSpace(Buscar) ? null : Buscar.Trim();
    }
}