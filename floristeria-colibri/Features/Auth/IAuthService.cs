using Colibri.Api.Features.Auth.Dtos;

namespace Colibri.Api.Features.Auth;

public interface IAuthService
{
    Task<LoginResponse> LoginAsync(LoginRequest peticion, CancellationToken ct = default);
    Task<SesionDto> SesionActualAsync(CancellationToken ct = default);
    Task CambiarPasswordPropiaAsync(CambiarPasswordRequest peticion, CancellationToken ct = default);
}
