namespace Colibri.Api.Models.Enums;

/// <summary>
/// Etiquetas de tipo_movimiento. 'traspaso' va al final porque se agregó con
/// ALTER TYPE ADD VALUE y Postgres lo pone al final del enumsortorder.
/// </summary>
public enum TipoMovimiento
{
    alta, entrada, salida, ajuste, venta,
    consumo, armado, merma, baja, traspaso
}