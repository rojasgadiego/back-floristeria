namespace Colibri.Api.Models.Enums;

/// <summary>
/// Qué pasó con lo que salió del inventario. Cada destino tiene una
/// consecuencia contable distinta:
///
/// perdida              se botó. Costo completo contra el resultado.
///
/// reingreso            parte vuelve al stock con precio rebajado. Solo se
///                      pierde la diferencia entre lo que costó y lo que
///                      ahora vale, no el total.
///
/// devolucion_proveedor sale del stock pero NO es costo: el proveedor lo
///                      abona. Confundirlo con pérdida infla la merma del
///                      mes y hace ver mal a quien compró bien.
/// </summary>
public enum DestinoMerma
{
    perdida,
    reingreso,
    devolucion_proveedor
}