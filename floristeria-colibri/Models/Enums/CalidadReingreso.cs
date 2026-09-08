namespace Colibri.Api.Models.Enums;

/// <summary>
/// Etiquetas confirmadas contra calidad_reingreso: optima, buena, limitada.
///
/// Es la clasificación de la flor que vuelve —del desarme de un ramo, de un
/// pedido que no se usó—. De ella depende cuánto se le rebaja al precio, y
/// por eso ese lote queda fuera del reparto automático: hay que escanearlo
/// para venderlo, o se cobraría flor de segunda a precio de primera.
/// </summary>
public enum CalidadReingreso
{
    optima,
    buena,
    limitada
}