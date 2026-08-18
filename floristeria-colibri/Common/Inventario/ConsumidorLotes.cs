// Common/Inventario/ConsumidorLotes.cs
// Reemplaza el contenido COMPLETO del archivo que ya tienes.
// Acá va SOLO la clase: la interfaz vive en IConsumidorLotes.cs, el enum en
// Domain/Ubicacion.cs y ConsumoLote sigue donde siempre estuvo.

using Microsoft.EntityFrameworkCore;

using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;

namespace Colibri.Api.Common.Inventario;

public class ConsumidorLotes : IConsumidorLotes
{
    private readonly ColibriDbContext _db;
    private readonly ILogger<ConsumidorLotes> _log;

    public ConsumidorLotes(ColibriDbContext db, ILogger<ConsumidorLotes> log)
    {
        _db = db;
        _log = log;
    }

    public async Task<IReadOnlyList<ConsumoLote>> ConsumirAsync(
        int productoId,
        int cantidad,
        Ubicacion ubicacion,
        string motivo,
        int? usuarioId,
        string? referenciaTipo,
        int? referenciaId,
        TipoMovimiento tipoMovimiento,
        int? lotePreferido = null,
        IEnumerable<int>? lotesAutorizados = null,
        CancellationToken ct = default)
    {
        if (cantidad <= 0)
            throw new ExcepcionNegocio("La cantidad a consumir debe ser mayor que cero.");

        var consumidos = new List<ConsumoLote>();
        var restante = cantidad;

        var autorizados = (lotesAutorizados ?? Enumerable.Empty<int>())
            .Where(id => id != lotePreferido)
            .Distinct()
            .ToList();

        if (autorizados.Count > 0)
        {
            // Se consultan ordenados por antigüedad: aunque la persona haya
            // elegido varios lotes, entre ellos sigue rigiendo FIFO.
            //
            // El filtro por ubicación es nuevo y es importante: un lote
            // autorizado que está en bodega no sirve para una venta del
            // mostrador, aunque sea del mismo producto.
            var lotes = await _db.Lotes.AsNoTracking()
                .Where(l => autorizados.Contains(l.Id)
                         && l.ProductoId == productoId
                         && l.Ubicacion == ubicacion
                         && l.Estado == EstadoLote.activo)
                .OrderBy(l => l.FechaVencimiento).ThenBy(l => l.FechaIngreso).ThenBy(l => l.Id)
                .Select(l => new { l.Id, l.Codigo, l.VarasDisponibles })
                .ToListAsync(ct);

            var ajenos = autorizados.Except(lotes.Select(l => l.Id)).ToList();
            if (ajenos.Count > 0)
            {
                _log.LogWarning(
                    "Se ignoraron lotes autorizados que no aplican al producto {Producto} en {Ubicacion}: {Lotes}",
                    productoId, ubicacion, string.Join(", ", ajenos));
            }

            foreach (var lote in lotes)
            {
                if (restante <= 0) break;

                var toma = Math.Min(restante, lote.VarasDisponibles);
                if (toma <= 0) continue;

                // Llamada explícita por lote: pedir exactamente lo que tiene
                // hace que fn_consumir no siga hacia otros lotes, y así lo
                // autorizado se respeta al pie de la letra.
                consumidos.AddRange(await LlamarAsync(
                    productoId, toma, ubicacion, motivo, usuarioId, referenciaTipo,
                    referenciaId, tipoMovimiento, lote.Id, ct));

                restante -= toma;
            }
        }

        if (restante > 0)
        {
            consumidos.AddRange(await LlamarAsync(
                productoId, restante, ubicacion, motivo, usuarioId, referenciaTipo,
                referenciaId, tipoMovimiento, lotePreferido, ct));
        }

        return consumidos;
    }

    private async Task<List<ConsumoLote>> LlamarAsync(
        int productoId, int cantidad, Ubicacion ubicacion, string motivo, int? usuarioId,
        string? referenciaTipo, int? referenciaId, TipoMovimiento tipo,
        int? lotePreferido, CancellationToken ct)
    {
        // Los enum de PostgreSQL viajan como texto y se convierten en la base:
        // es la única forma de pasarlos por parámetro.
        var nombreTipo = tipo.ToString();
        var nombreUbicacion = ubicacion.ToString();

        return await _db.ConsumosLote
            .FromSqlInterpolated($@"
                SELECT * FROM fn_consumir(
                    {productoId}, {cantidad},
                    {nombreUbicacion}::ubicacion_inventario,
                    {motivo}, {usuarioId},
                    {referenciaTipo}, {referenciaId},
                    {nombreTipo}::tipo_movimiento, {lotePreferido})")
            .ToListAsync(ct);
    }
}