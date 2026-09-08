namespace Colibri.Api.Models.Enums;

/// <summary>
/// borrador → se arma y se corrige · recibida → generó lotes y movió stock
/// anulada  → se descartó antes de recibir
/// </summary>
public enum EstadoCompra
{
    borrador,
    recibida,
    anulada
}


/// <summary>
/// Etiquetas confirmadas contra tipo_presentacion: vara, paquete, caja.
/// El orden importa: coincide con el enumsortorder de Postgres.
///
/// `vara` sigue en el enum porque quitar una etiqueta de un tipo en
/// Postgres obliga a recrearlo y a tocar cada columna que lo use. Pero la
/// compra al detalle no existe en este negocio: todo entra en paquetes o
/// cajas, y sp_abs_i_presentacion lo rechaza.
/// </summary>
public enum TipoPresentacion
{
    vara,
    paquete,
    caja
}