using System.ComponentModel.DataAnnotations;

namespace Colibri.Api.Common.Paginacion;

/// <summary>
/// Parámetros de paginación que heredan todos los filtros de listado.
/// El tope de 200 no es capricho: sin él, un GET sin parámetros contra
/// movimientos_inventario traería medio millón de filas.
/// </summary>
public class ParametrosPagina
{
    private const int TamanoMaximo = 200;
    private int _porPagina = 25;

    [Range(1, int.MaxValue, ErrorMessage = "La página debe ser 1 o mayor.")]
    public int Pagina { get; set; } = 1;

    public int PorPagina
    {
        get => _porPagina;
        set => _porPagina = value switch
        {
            < 1 => 25,
            > TamanoMaximo => TamanoMaximo,
            _ => value
        };
    }

    /// <summary>Texto libre de búsqueda. Cada módulo decide sobre qué campos aplica.</summary>
    public string? Buscar { get; set; }

    public int Saltar => (Pagina - 1) * PorPagina;
}
