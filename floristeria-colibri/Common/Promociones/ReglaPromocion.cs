using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;

namespace Colibri.Api.Common.Promociones;

/// <summary>
/// Una línea del carrito, reducida a lo que la promoción necesita saber.
/// </summary>
public record LineaPromocion(int? ProductoId, int CategoriaId, int Subtotal, bool EsServicio);

/// <summary>
/// La regla de una promoción: cuándo aplica y cuánto descuenta.
///
/// Vive acá y no dentro de Ventas porque la usan dos flujos que deben dar
/// exactamente el mismo resultado: cobrar una boleta y simular la promoción
/// contra ventas pasadas. Si fueran dos copias, el simulador terminaría
/// mintiendo sobre lo que la promoción realmente hace.
/// </summary>
public static class ReglaPromocion
{
    /// <summary>
    /// Si la promoción corre en esa fecha: dentro de su vigencia y en un día
    /// de la semana habilitado.
    /// </summary>
    public static bool EstaVigente(Promocion promo, DateOnly fecha)
    {
        if (promo.Desde.HasValue && fecha < promo.Desde.Value) return false;
        if (promo.Hasta.HasValue && fecha > promo.Hasta.Value) return false;

        // dias vacío significa todos los días. 0 = domingo, igual que en
        // PostgreSQL y que DayOfWeek de .NET.
        if (promo.Dias.Length > 0)
        {
            var dia = (short)fecha.DayOfWeek;
            if (!promo.Dias.Contains(dia)) return false;
        }

        return true;
    }

    /// <summary>
    /// Descuento sobre la base que corresponde al alcance.
    ///
    /// Se recalcula siempre, nunca se guarda: un monto congelado seguiría
    /// descontando lo mismo aunque el cliente quite la mitad del carrito.
    /// </summary>
    public static int Calcular(Promocion promo, IEnumerable<LineaPromocion> lineas, int bruto)
    {
        var baseCalculo = promo.Alcance switch
        {
            AlcancePromocion.boleta => bruto,
            AlcancePromocion.categoria => lineas
                .Where(l => !l.EsServicio && l.CategoriaId == promo.CategoriaId)
                .Sum(l => l.Subtotal),
            AlcancePromocion.producto => lineas
                .Where(l => l.ProductoId == promo.ProductoId)
                .Sum(l => l.Subtotal),
            _ => 0
        };

        if (baseCalculo <= 0) return 0;

        // El mínimo siempre se mide sobre el total de la boleta: es lo que
        // entiende el cliente cuando lee "en compras sobre $20.000".
        if (promo.Minimo > 0 && bruto < promo.Minimo) return 0;

        var descuento = promo.Tipo == TipoPromocion.porcentaje
            ? (int)Math.Round(baseCalculo * promo.Valor / 100m, MidpointRounding.AwayFromZero)
            : promo.Valor;

        return Math.Min(descuento, baseCalculo);
    }

    /// <summary>
    /// Si dos promociones pueden aplicar a la misma boleta. No es un error
    /// —el punto de venta ofrece la más conveniente— pero conviene saberlo:
    /// una promoción siempre peor que otra nunca se va a usar.
    /// </summary>
    public static bool SeSolapan(Promocion a, Promocion b)
    {
        if (a.Id == b.Id) return false;

        // Alcances distintos pueden convivir sin pisarse: una de boleta y una
        // de categoría se suman en la percepción del cliente, no se anulan.
        if (a.Alcance != b.Alcance) return false;

        if (a.Alcance == AlcancePromocion.categoria && a.CategoriaId != b.CategoriaId)
            return false;

        if (a.Alcance == AlcancePromocion.producto && a.ProductoId != b.ProductoId)
            return false;

        // Vigencias que no se cruzan nunca coinciden
        if (a.Hasta.HasValue && b.Desde.HasValue && a.Hasta.Value < b.Desde.Value) return false;
        if (b.Hasta.HasValue && a.Desde.HasValue && b.Hasta.Value < a.Desde.Value) return false;

        // Días: vacío significa todos, así que siempre cruza
        if (a.Dias.Length > 0 && b.Dias.Length > 0 && !a.Dias.Intersect(b.Dias).Any())
            return false;

        return true;
    }
}