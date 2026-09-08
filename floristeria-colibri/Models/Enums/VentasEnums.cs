using Colibri.Api.Models.Enums;

/// <summary>
/// Etiquetas confirmadas contra medio_pago: efectivo, debito, credito,
/// transferencia.
///
/// Solo `efectivo` entra al cajón, y es lo único que el arqueo compara
/// contra lo contado al cerrar. Las otras tres pasan por máquina o banco y
/// se cuadran con la liquidación del proveedor de pagos, no con billetes.
/// </summary>
public enum MedioPago
{
    efectivo,
    debito,
    credito,
    transferencia
}

/// <summary>Un turno de caja. Solo puede haber uno `abierta` a la vez.</summary>
public enum EstadoCaja
{
    abierta,
    cerrada
}

/// <summary>
/// Cómo salió el producto del inventario.
///
/// simple → varas de un lote: se descuentan de una partida del mostrador
/// listo  → una unidad de un armado ya montado; sus insumos se consumieron
///          al armarlo, no al venderlo
/// </summary>
public enum TipoConsumo
{
    listo,
    simple
}

public enum TipoPromocion
{
    porcentaje,
    monto
}

/// <summary>
/// Sobre qué se aplica el descuento. El mínimo se compara contra la base que
/// le corresponde: una promo de "20% en rosas sobre $10.000" mira cuánto hay
/// de rosas, no cuánto suma la boleta con el jarrón incluido.
/// </summary>
public enum AlcancePromocion
{
    boleta,
    categoria,
    producto
}