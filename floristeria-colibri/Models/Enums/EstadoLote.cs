namespace Colibri.Api.Models.Enums;

/// <summary>
/// Etiquetas confirmadas contra estado_lote: activo, agotado, descartado.
///
/// activo     → tiene varas y se puede vender
/// agotado    → se consumieron todas; la fila queda para el historial
/// descartado → se botó entero (se pudrió, llegó malo, se rompió el balde)
/// </summary>
public enum EstadoLote
{
    activo,
    agotado,
    descartado
}