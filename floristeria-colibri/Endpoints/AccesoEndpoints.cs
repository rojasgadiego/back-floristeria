using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de ACCESO: login, sesión y administración de usuarios.
///
/// /login y /yo van en un grupo aparte porque tienen reglas distintas: el
/// primero es público (si pidiera token, nadie podría obtener uno) y el segundo
/// solo pide estar autenticado, sin importar el rol.
/// </summary>
public class AccesoEndpoints : EndpointsBase
{
    public AccesoEndpoints(ILogger<AccesoEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // ── Público ──
        var auth = app.MapGroup("/api/auth")
            .WithTags("Autenticación")
            .AddEndpointFilter<RespuestaFilter>();

        auth.MapPost("/login", Login)
            .WithName("Login")
            .WithSummary("Entrega un token si el correo y la contraseña son correctos")
            .AllowAnonymous()
            .Produces<ResponseDto>(200)
            .Produces(401)
            .Produces(400);

        auth.MapGet("/me", Yo)
            .WithName("Yo")
            .WithSummary("El usuario del token. Para que el front se refresque al recargar.")
            .RequireAuthorization()
            .Produces<ResponseDto>(200)
            .Produces(401);

        auth.MapPost("/cambiar-password", CambiarMiPassword)
            .WithName("CambiarMiPassword")
            .WithSummary("Cambio de contraseña propio. Exige la actual.")
            .RequireAuthorization()
            .Produces<ResponseDto>(200)
            .Produces(400)
            .Produces(401);

        // ── Administración ──
        var usuarios = app.MapGroup("/api/usuarios")
            .WithTags("Usuarios")
            .RequireAuthorization(Politicas.Admin)
            .AddEndpointFilter<RespuestaFilter>();

        usuarios.MapGet("/", ListarUsuarios)
            .WithName("ListarUsuarios")
            .Produces<ResponseDto>(200)
            .Produces(401)
            .Produces(403);

        usuarios.MapGet("/{id:int}", ObtenerUsuario)
            .WithName("ObtenerUsuario")
            .Produces<ResponseDto>(200)
            .Produces(404)
            .Produces(401)
            .Produces(403);

        usuarios.MapPost("/", CrearUsuario)
            .WithName("CrearUsuario")
            .Produces<ResponseDto>(201)
            .Produces(400)
            .Produces(401)
            .Produces(403);

        usuarios.MapPut("/{id:int}", ActualizarUsuario)
            .WithName("ActualizarUsuario")
            .Produces<ResponseDto>(200)
            .Produces(400)
            .Produces(401)
            .Produces(403);

        usuarios.MapPost("/{id:int}/resetear-password", ResetearPassword)
            .WithName("ResetearPassword")
            .WithSummary("Reseteo por administrador. No pide la contraseña anterior.")
            .Produces<ResponseDto>(200)
            .Produces(400)
            .Produces(401)
            .Produces(403);

        usuarios.MapPatch(
                "/{id:int}/activar",
                (int id, AccesoBLL bll, HttpContext http, CancellationToken ct)
                    => CambiarEstado(id, true, bll, http, ct))
            .WithName("ActivarUsuario")
            .Produces<ResponseDto>(200)
            .Produces(400)
            .Produces(401)
            .Produces(403);

        usuarios.MapPatch(
                "/{id:int}/desactivar",
                (int id, AccesoBLL bll, HttpContext http, CancellationToken ct)
                    => CambiarEstado(id, false, bll, http, ct))
            .WithName("DesactivarUsuario")
            .Produces<ResponseDto>(200)
            .Produces(400)
            .Produces(401)
            .Produces(403);
    }

    #region Autenticación

    public async Task<ResponseDto> Login(
        [FromBody] LoginRequest peticion,
        AccesoBLL bll,
        CancellationToken ct)
    {
        try
        {
            if (peticion is null)
                return CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    "Request inválido",
                    null);

            var r = await bll.Login(peticion, ct);

            // 401 y no 400: la petición estaba bien formada, lo que falló fue la
            // credencial. El front distingue "corrige el formulario" de
            // "tus datos no sirven".
            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Bienvenido", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.Unauthorized, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error en login");

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                "Error al iniciar sesión.",
                null);
        }
    }

    public async Task<ResponseDto> Yo(
        AccesoBLL bll,
        HttpContext http,
        CancellationToken ct)
        => await ConsultarUno(
            () => bll.Yo(UsuarioActual(http), ct),
            "Usuario",
            "La sesión ya no es válida.");

    public async Task<ResponseDto> CambiarMiPassword(
        [FromBody] CambiarPasswordRequest peticion,
        AccesoBLL bll,
        HttpContext http,
        CancellationToken ct)
    {
        try
        {
            var error = await bll.CambiarMiPassword(UsuarioActual(http), peticion, ct);

            return string.IsNullOrWhiteSpace(error)
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Ok,
                    "Contraseña actualizada",
                    new { Cambiada = true })
                : CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    error,
                    new { Cambiada = false });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cambiar contraseña");

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                "Error al cambiar la contraseña.",
                null);
        }
    }

    #endregion

    #region Usuarios

    public async Task<ResponseDto> ListarUsuarios(
        [FromQuery] string? busqueda,
        [FromQuery] RolUsuario? rol,
        [FromQuery] bool? activo,
        AccesoBLL bll,
        CancellationToken ct)
        => await Consultar(
            () => bll.ListarUsuarios(busqueda, rol, activo, ct),
            "usuarios");

    public async Task<ResponseDto> ObtenerUsuario(
        int id,
        AccesoBLL bll,
        CancellationToken ct)
        => await ConsultarUno(
            () => bll.ObtenerUsuario(id, ct),
            "Usuario",
            $"No existe el usuario {id}.");

    public async Task<ResponseDto> CrearUsuario(
        [FromBody] CrearUsuarioRequest peticion,
        AccesoBLL bll,
        CancellationToken ct)
    {
        try
        {
            var r = await bll.CrearUsuario(peticion, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Created,
                    $"{r.Datos!.Nombre} agregado",
                    r.Datos)
                : CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    r.Mensaje,
                    null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al crear usuario");

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                $"Error al crear el usuario: {ex.Message}",
                null);
        }
    }

    public async Task<ResponseDto> ActualizarUsuario(
        int id,
        [FromBody] ActualizarUsuarioRequest peticion,
        AccesoBLL bll,
        CancellationToken ct)
    {
        try
        {
            var r = await bll.ActualizarUsuario(id, peticion, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Ok,
                    "Usuario actualizado",
                    r.Datos)
                : CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    r.Mensaje,
                    null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al actualizar usuario {Id}", id);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                $"Error al actualizar el usuario: {ex.Message}",
                null);
        }
    }

    public async Task<ResponseDto> ResetearPassword(
        int id,
        [FromBody] ResetPasswordRequest peticion,
        AccesoBLL bll,
        CancellationToken ct)
    {
        try
        {
            var error = await bll.ResetearPassword(id, peticion, ct);

            return string.IsNullOrWhiteSpace(error)
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Ok,
                    "Contraseña restablecida",
                    new { Cambiada = true })
                : CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    error,
                    new { Cambiada = false });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al resetear contraseña de {Id}", id);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                "Error al restablecer la contraseña.",
                null);
        }
    }

    public async Task<ResponseDto> CambiarEstado(
        int id,
        bool activo,
        AccesoBLL bll,
        HttpContext http,
        CancellationToken ct)
    {
        try
        {
            var r = await bll.CambiarEstado(id, activo, UsuarioActual(http), ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Ok,
                    activo ? "Usuario activado" : "Usuario desactivado",
                    r.Datos)
                : CustomUtilz.CreateResponse(
                    HttpStatusCodes.BadRequest,
                    r.Mensaje,
                    null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cambiar estado del usuario {Id}", id);

            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError,
                "Error al cambiar el estado.",
                null);
        }
    }

    #endregion
}
