using Colibri.Api.Features.Configuracion.Dtos;

namespace Colibri.Api.Features.Configuracion;

public interface IConfiguracionService
{
    Task<ConfiguracionDto> ObtenerAsync(CancellationToken ct = default);

    Task<AjustesLocalDto> GuardarLocalAsync(AjustesLocalDto peticion, CancellationToken ct = default);
    Task<AjustesTicketDto> GuardarTicketAsync(AjustesTicketDto peticion, CancellationToken ct = default);
    Task<AjustesVentaDto> GuardarVentaAsync(AjustesVentaDto peticion, CancellationToken ct = default);
    Task<AjustesClubDto> GuardarClubAsync(GuardarClubRequest peticion, CancellationToken ct = default);

    /// <summary>Qué mueve cambiar el valor del punto, antes de aplicarlo.</summary>
    Task<ImpactoClubDto> ImpactoClubAsync(int valorPuntoNuevo, CancellationToken ct = default);
}