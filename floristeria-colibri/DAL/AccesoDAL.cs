using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Models.Tablas;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de ACCESO.
///
/// El password_hash entra y sale SOLO por BuscarParaLogin y CambiarPassword.
/// Ninguna otra consulta lo toca, y el modelo público (Usuario) ni siquiera
/// tiene la propiedad.
/// </summary>
public class AccesoDAL
{
    private readonly IAccesoDatos _db;

    public AccesoDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Autenticación
    // ============================================================

    /// <summary>
    /// sp_acc_c_usuario_login. Devuelve el usuario con su hash, activo o no:
    /// quién decide el mensaje es el BLL, después de verificar la contraseña.
    /// </summary>
    internal async Task<UsuarioAuth?> BuscarParaLogin(string email, CancellationToken ct = default)
        => await _db.ConsultarUno<UsuarioAuth>(
            "SELECT * FROM sp_acc_c_usuario_login(@email)", new { email }, ct);

    /// <summary>
    /// sp_acc_u_ultimo_acceso. No devuelve error: si falla, el usuario ya entró
    /// y negarle la sesión por no poder escribir una fecha sería absurdo.
    /// </summary>
    public async Task SellarUltimoAcceso(int id, CancellationToken ct = default)
        => await _db.Escalar<int>("SELECT sp_acc_u_ultimo_acceso(@id)", new { id }, ct);

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResumenInventario?> ConsultarResumen(
    ProductoFiltro f, CancellationToken ct = default)
{
    var p = new DynamicParameters();
    p.Add("Buscar", f.Buscar);
    p.Add("CategoriaId", f.CategoriaId);
    p.Add("Tipo", f.Tipo?.ToString());   // el enum nulable, otra vez
    p.Add("Activo", f.Activo);

    return await _db.ConsultarUno<ResumenInventario>(
        """
        SELECT * FROM sp_inv_c_resumen(
            @Buscar::text, @CategoriaId::int, @Tipo::tipo_producto, @Activo::boolean)
        """,
        p, ct);
}

    public async Task<IEnumerable<Usuario>> ConsultarUsuarios(
    string? busqueda, RolUsuario? rol, bool? activo, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("busqueda", busqueda);
        p.Add("rol", rol?.ToString());   // mismo motivo que arriba
        p.Add("activo", activo);

        return await _db.ConsultarLista<Usuario>(
            "SELECT * FROM sp_acc_c_usuarios(@busqueda::text, @rol::rol_usuario, @activo::boolean)",
            p, ct);
    }

    public async Task<Usuario?> ConsultarUsuario(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Usuario>(
            "SELECT * FROM sp_acc_c_usuario(@id)", new { id }, ct);

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>sp_acc_i_usuario. El hash llega ya calculado desde el BLL.</summary>
    public async Task<(int Id, string Error)> InsertarUsuario(
        CrearUsuarioRequest r, string passwordHash, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                "SELECT sp_acc_i_usuario(@Nombre, @Email, @passwordHash, @Rol)",
                new { r.Nombre, r.Email, passwordHash, r.Rol }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> ActualizarUsuario(
        int id, ActualizarUsuarioRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_acc_u_usuario(@id, @Nombre, @Email, @Rol)",
                new { id, r.Nombre, r.Email, r.Rol }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarPassword(
        int id, string passwordHash, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_acc_u_usuario_password(@id, @passwordHash)",
                new { id, passwordHash }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarEstado(int id, bool activo, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_acc_u_usuario_estado(@id, @activo)", new { id, activo }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
