namespace Colibri.Api.Common.Paginacion;

public class ResultadoPagina<T>
{
    public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
    public int Total { get; init; }
    public int Pagina { get; init; }
    public int PorPagina { get; init; }
    public int TotalPaginas => PorPagina > 0 ? (int)Math.Ceiling(Total / (double)PorPagina) : 0;
    public bool HayAnterior => Pagina > 1;
    public bool HaySiguiente => Pagina < TotalPaginas;

    public static ResultadoPagina<T> Crear(IReadOnlyList<T> items, int total, ParametrosPagina p) =>
        new() { Items = items, Total = total, Pagina = p.Pagina, PorPagina = p.PorPagina };
}
