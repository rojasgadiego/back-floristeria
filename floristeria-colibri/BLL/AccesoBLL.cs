using Colibri.Api.Auth;
using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// Business Logic Layer de ACCESO.
///
/// Acá vive la única regla que NO puede estar en la base: verificar la
/// contraseña. BCrypt necesita el hash y la clave en el mismo lugar, y ese
/// lugar es C#.
/// </summary>
public class AccesoBLL
{
    private readonly AccesoDAL _dal;
    private readonly IJwtTokenService _jwt;
    private readonly ILogger<AccesoBLL> _log;

    /// <summary>
    /// Hash de descarte para cuando el correo no existe. Ver Login().
    /// </summary>
    private static readonly string HashSenuelo =
        BCrypt.Net.BCrypt.HashPassword("señuelo", PasswordHasher.WorkFactor);

    public AccesoBLL(AccesoDAL dal, IJwtTokenService jwt, ILogger<AccesoBLL> log)
    {
        _dal = dal;
        _jwt = jwt;
        _log = log;
    }

    // ============================================================
    // Login
    // ============================================================

    public async Task<ResultadoOp<LoginResponse>> Login(
        LoginRequest r,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Email) || string.IsNullOrWhiteSpace(r.Password))
            return ResultadoOp<LoginResponse>.Error("Debe indicar correo y contraseña.");

        var u = await _dal.BuscarParaLogin(r.Email.Trim(), ct);

        // Si el correo no existe igual se verifica contra un hash de descarte.
        // Sin esto, "correo inexistente" responde en 1 ms y "clave incorrecta"
        // en 80 ms, y esa diferencia deja enumerar qué correos están
        // registrados. Cuesta nada y cierra la puerta.
        if (u is null)
        {
            PasswordHasher.Verificar(r.Password, HashSenuelo);
            return ResultadoOp<LoginResponse>.Error("Correo o contraseña incorrectos.");
        }

        if (!PasswordHasher.Verificar(r.Password, u.PasswordHash))
        {
            _log.LogWarning("Login fallido para {Email}", u.Email);

            // Mismo mensaje que arriba, a propósito: decir "la clave está mala"
            // ya confirma que ese correo existe.
            return ResultadoOp<LoginResponse>.Error("Correo o contraseña incorrectos.");
        }

        // Recién acá, con la contraseña ya verificada, se puede decir la verdad:
        // es su cuenta y merece saber por qué no entra.
        if (!u.Activo)
            return ResultadoOp<LoginResponse>.Error(
                "Tu cuenta está desactivada. Habla con un administrador.");

        var usuario = new Usuario
        {
            Id = u.Id,
            Nombre = u.Nombre,
            Email = u.Email,
            Rol = u.Rol,
            Activo = u.Activo,
            Permisos = PermisosDe(u.Rol)
        };

        var (token, expira) = _jwt.Generar(usuario);

        // El sello no bloquea la sesión: si falla, el usuario ya entró y
        // negarle el acceso por no poder escribir una fecha sería absurdo.
        try
        {
            await _dal.SellarUltimoAcceso(u.Id, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "No se pudo sellar el último acceso de {Id}", u.Id);
        }

        return ResultadoOp<LoginResponse>.Exito(new LoginResponse
        {
            Token = token,
            ExpiraEn = expira,
            Usuario = usuario
        });
    }

    /// <summary>
    /// Quién soy, según el token. Para que el front se refresque al recargar.
    /// </summary>
    public async Task<Usuario?> Yo(int id, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var u = await _dal.ConsultarUsuario(id, ct);
        if (u is not null) u.Permisos = PermisosDe(u.Rol);

        return u;
    }

    // ============================================================
    // Contraseñas
    // ============================================================

    public async Task<string> CambiarMiPassword(
        int id,
        CambiarPasswordRequest r,
        CancellationToken ct = default)
    {
        if (id <= 0) return "Sesión inválida.";
        if (string.IsNullOrWhiteSpace(r.PasswordActual)) return "Debe indicar la contraseña actual.";

        var error = ValidarPassword(r.PasswordNueva);
        if (error is not null) return error;

        var usuarioActual = await _dal.ConsultarUsuario(id, ct);
        if (usuarioActual is null) return "El usuario no existe.";

        var u = await _dal.BuscarParaLogin(usuarioActual.Email, ct);
        if (u is null) return "El usuario no existe.";

        // Exigir la actual es lo que impide que una sesión abierta en un
        // computador prestado alcance para secuestrar la cuenta.
        if (!PasswordHasher.Verificar(r.PasswordActual, u.PasswordHash))
            return "La contraseña actual no es correcta.";

        if (PasswordHasher.Verificar(r.PasswordNueva, u.PasswordHash))
            return "La contraseña nueva debe ser distinta de la actual.";

        return await _dal.CambiarPassword(id, PasswordHasher.Hash(r.PasswordNueva), ct);
    }

    /// <summary>
    /// Reseteo por administrador: no pide la anterior.
    /// </summary>
    public async Task<string> ResetearPassword(
        int id,
        ResetPasswordRequest r,
        CancellationToken ct = default)
    {
        if (id <= 0) return "El ID debe ser mayor a 0.";

        var error = ValidarPassword(r.PasswordNueva);
        if (error is not null) return error;

        return await _dal.CambiarPassword(id, PasswordHasher.Hash(r.PasswordNueva), ct);
    }

    /// <summary>
    /// Mínimo 8. No exige mayúsculas ni símbolos a propósito: esas reglas
    /// producen "Verano2024!" y post-its pegados al monitor. El largo es lo que
    /// realmente cuesta romper.
    /// </summary>
    private static string? ValidarPassword(string? clave)
    {
        if (string.IsNullOrWhiteSpace(clave)) return "Debe indicar la contraseña nueva.";
        if (clave.Length < 8) return "La contraseña debe tener al menos 8 caracteres.";
        if (clave.Length > 72) return "La contraseña no puede superar los 72 caracteres.";

        return null;
    }

    // ============================================================
    // Administración de usuarios
    // ============================================================

    public async Task<IEnumerable<Usuario>> ListarUsuarios(
        string? busqueda,
        RolUsuario? rol,
        bool? activo,
        CancellationToken ct = default)
        => await _dal.ConsultarUsuarios(
            string.IsNullOrWhiteSpace(busqueda) ? null : busqueda.Trim(),
            rol,
            activo,
            ct);

    public async Task<Usuario?> ObtenerUsuario(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarUsuario(id, ct);

    public async Task<ResultadoOp<Usuario>> CrearUsuario(
        CrearUsuarioRequest r,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre))
            return ResultadoOp<Usuario>.Error("Debe indicar el nombre.");

        if (string.IsNullOrWhiteSpace(r.Email))
            return ResultadoOp<Usuario>.Error("Debe indicar el correo.");

        var error = ValidarPassword(r.Password);
        if (error is not null) return ResultadoOp<Usuario>.Error(error);

        var (id, err) = await _dal.InsertarUsuario(r, PasswordHasher.Hash(r.Password), ct);
        if (!string.IsNullOrWhiteSpace(err)) return ResultadoOp<Usuario>.Error(err);

        var creado = await _dal.ConsultarUsuario(id, ct);

        return creado is null
            ? ResultadoOp<Usuario>.Error("El usuario se creó pero no se pudo leer.")
            : ResultadoOp<Usuario>.Exito(creado);
    }

    public async Task<ResultadoOp<Usuario>> ActualizarUsuario(
        int id,
        ActualizarUsuarioRequest r,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return ResultadoOp<Usuario>.Error("El ID debe ser mayor a 0.");

        if (string.IsNullOrWhiteSpace(r.Nombre))
            return ResultadoOp<Usuario>.Error("Debe indicar el nombre.");

        if (string.IsNullOrWhiteSpace(r.Email))
            return ResultadoOp<Usuario>.Error("Debe indicar el correo.");

        var err = await _dal.ActualizarUsuario(id, r, ct);
        if (!string.IsNullOrWhiteSpace(err)) return ResultadoOp<Usuario>.Error(err);

        var u = await _dal.ConsultarUsuario(id, ct);

        return u is null
            ? ResultadoOp<Usuario>.Error("El usuario no existe.")
            : ResultadoOp<Usuario>.Exito(u);
    }

    /// <summary>
    /// El propio id viene del token, no del body: es lo que impide que un admin
    /// se desactive solo y quede fuera de su propia instalación.
    /// </summary>
    public async Task<ResultadoOp<Usuario>> CambiarEstado(
        int id,
        bool activo,
        int idSolicitante,
        CancellationToken ct = default)
    {
        if (id <= 0)
            return ResultadoOp<Usuario>.Error("El ID debe ser mayor a 0.");

        if (id == idSolicitante && !activo)
            return ResultadoOp<Usuario>.Error("No puedes desactivar tu propia cuenta.");

        var err = await _dal.CambiarEstado(id, activo, ct);
        if (!string.IsNullOrWhiteSpace(err)) return ResultadoOp<Usuario>.Error(err);

        var u = await _dal.ConsultarUsuario(id, ct);

        return u is null
            ? ResultadoOp<Usuario>.Error("El usuario no existe.")
            : ResultadoOp<Usuario>.Exito(u);
    }

    /// <summary>
    /// El front usa esto para decidir qué DIBUJA. Lo que se PERMITE lo deciden
    /// las políticas de cada endpoint — si acá dice que sí y allá que no, el
    /// resultado es un botón que devuelve 403.
    ///
    /// Los nombres son los de `meta.permiso` en router/index.js. Si agregas
    /// una ruta con un permiso nuevo y olvidas sumarlo acá, esa sección
    /// queda invisible para todos menos el admin, que lleva "*".
    /// </summary>
    private static string[] PermisosDe(RolUsuario rol) => rol switch
    {
        RolUsuario.admin => ["*"],

        // Bodega maneja lo que entra y lo que hay. No ve caja ni precios de
        // venta al público, ni toca el equipo.
        RolUsuario.bodega =>
        [
            "dashboard",
            "inventario",
            "lotes",
            "compras",
            "mermas",
            "reportes"
        ],

        // Vendedor atiende el mesón: vende, cotiza y consulta clientes. Ve el
        // inventario para saber qué hay, pero no lo modifica —eso lo frena la
        // política Inventario del endpoint, no este permiso—. Registra las
        // mermas del mostrador, que es la flor que tiene a la vista.
        RolUsuario.vendedor =>
        [
            "dashboard",
            "pos",
            "ventas",
            "cotizaciones",
            "clientes",
            "inventario",
            "lotes",
            "mermas",
            "promociones"
        ],

        _ => []
    };
}
