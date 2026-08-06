namespace Colibri.Api.Common.Ajustes;

/// <summary>
/// Lee la configuración guardada en la tabla `configuracion`.
///
/// Vive en la base y no en appsettings porque la administradora la cambia
/// desde la aplicación: el IVA, el valor del punto o el umbral de descuento
/// no deberían exigir un despliegue.
/// </summary>
public interface IAjustesService
{
    Task<AjustesLocal> LocalAsync(CancellationToken ct = default);
    Task<AjustesTicket> TicketAsync(CancellationToken ct = default);
    Task<AjustesVenta> VentaAsync(CancellationToken ct = default);
    Task<AjustesClub> ClubAsync(CancellationToken ct = default);

    /// <summary>Descarta la caché. Se llama al guardar la configuración.</summary>
    void Invalidar();
}