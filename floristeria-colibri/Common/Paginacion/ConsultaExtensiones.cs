using Microsoft.EntityFrameworkCore;

namespace Colibri.Api.Common.Paginacion;

public static class ConsultaExtensiones
{
    /// <summary>
    /// Ejecuta el conteo y la página en dos consultas. Es lo que evita traer
    /// la tabla completa a memoria para contar.
    /// </summary>
    public static async Task<ResultadoPagina<T>> APaginaAsync<T>(
        this IQueryable<T> consulta,
        ParametrosPagina parametros,
        CancellationToken ct = default)
    {
        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .Skip(parametros.Saltar)
            .Take(parametros.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<T>.Crear(items, total, parametros);
    }

    /// <summary>Aplica un filtro solo si la condición se cumple. Evita encadenar ifs.</summary>
    public static IQueryable<T> SiCumple<T>(
        this IQueryable<T> consulta,
        bool condicion,
        System.Linq.Expressions.Expression<Func<T, bool>> filtro)
        => condicion ? consulta.Where(filtro) : consulta;
}
