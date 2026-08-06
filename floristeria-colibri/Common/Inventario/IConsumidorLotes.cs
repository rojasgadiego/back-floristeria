using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;

namespace Colibri.Api.Common.Inventario;

/// <summary>
/// Descuenta varas de los lotes aplicando FIFO.
///
/// Vive acá y no dentro de un módulo porque lo usan tres flujos distintos
/// —armar, vender y desarmar— y la regla tiene que ser una sola. Duplicarla
/// haría que con el tiempo cada uno consumiera de forma ligeramente distinta.
/// </summary>
public interface IConsumidorLotes
{
    /// <summary>
    /// Consume <paramref name="cantidad"/> varas del producto.
    ///
    /// Los lotes de <paramref name="lotesAutorizados"/> se toman primero y de
    /// forma explícita: son los que quedaron fuera del reparto automático por
    /// tener precio propio —flor recuperada— y alguien decidió usarlos. El
    /// resto se completa por antigüedad.
    /// </summary>
    Task<IReadOnlyList<ConsumoLote>> ConsumirAsync(
        int productoId,
        int cantidad,
        string motivo,
        int? usuarioId,
        string? referenciaTipo,
        int? referenciaId,
        TipoMovimiento tipoMovimiento,
        int? lotePreferido = null,
        IEnumerable<int>? lotesAutorizados = null,
        CancellationToken ct = default);
}