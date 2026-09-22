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
    // Helpers
    // ============================================================

    /// <summary>
    /// PostgreSQL espera rol_usuario, no integer.
    /// Si se manda el enum directo, Dapper/Npgsql lo envía como entero.
    /// Por eso siempre convertimos a texto y en SQL casteamos a ::rol_usuario.
    /// </summary>
    private static string RolParaPostgres(RolUsuario rol)
        => rol.ToString().Trim().ToLowerInvariant();

    private static string? RolParaPostgres(RolUsuario? rol)
        => rol is null ? null : RolParaPostgres(rol.Value);

    // ============================================================
    // Autenticación
    // ============================================================

    /// <summary>
    /// sp_acc_c_usuario_login. Devuelve el usuario con su hash, activo o no:
    /// quién decide el mensaje es el BLL, después de verificar la contraseña.
    /// </summary>
    internal async Task<UsuarioAuth?> BuscarParaLogin(string email, CancellationToken ct = default)
        => await _db.ConsultarUno<UsuarioAuth>(
            "SELECT * FROM sp_acc_c_usuario_login(@email::text)",
            new { email },
            ct);

    /// <summary>
    /// sp_acc_u_ultimo_acceso. No devuelve error: si falla, el usuario ya entró
    /// y negarle la sesión por no poder escribir una fecha sería absurdo.
    /// </summary>
    public async Task SellarUltimoAcceso(int id, CancellationToken ct = default)
        => await _db.Escalar<int>(
            "SELECT sp_acc_u_ultimo_acceso(@id::int)",
            new { id },
            ct);

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResumenInventario?> ConsultarResumen(
        ProductoFiltro f,
        CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("CategoriaId", f.CategoriaId);
        p.Add("Tipo", f.Tipo?.ToString().Trim().ToLowerInvariant());
        p.Add("Activo", f.Activo);

        return await _db.ConsultarUno<ResumenInventario>(
            """
            SELECT * 
            FROM sp_inv_c_resumen(
                @Buscar::text,
                @CategoriaId::int,
                @Tipo::tipo_producto,
                @Activo::boolean
            )
            """,
            p,
            ct);
    }

    public async Task<IEnumerable<Usuario>> ConsultarUsuarios(
        string? busqueda,
        RolUsuario? rol,
        bool? activo,
        CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("busqueda", busqueda);
        p.Add("rol", RolParaPostgres(rol));
        p.Add("activo", activo);

        return await _db.ConsultarLista<Usuario>(
            """
            SELECT * 
            FROM sp_acc_c_usuarios(
                @busqueda::text,
                @rol::rol_usuario,
                @activo::boolean
            )
            """,
            p,
            ct);
    }

    public async Task<Usuario?> ConsultarUsuario(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Usuario>(
            "SELECT * FROM sp_acc_c_usuario(@id::int)",
            new { id },
            ct);

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>
    /// sp_acc_i_usuario. El hash llega ya calculado desde el BLL.
    /// 
    /// Importante:
    /// r.Rol NO se manda directo porque Dapper lo envía como integer.
    /// Se manda como string y se castea a rol_usuario.
    /// </summary>
    public async Task<(int Id, string Error)> InsertarUsuario(
        CrearUsuarioRequest r,
        string passwordHash,
        CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("Nombre", r.Nombre);
            p.Add("Email", r.Email);
            p.Add("PasswordHash", passwordHash);
            p.Add("Rol", RolParaPostgres(r.Rol));

            var id = await _db.Escalar<int>(
                """
                SELECT sp_acc_i_usuario(
                    @Nombre::text,
                    @Email::text,
                    @PasswordHash::text,
                    @Rol::rol_usuario
                )
                """,
                p,
                ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>
    /// sp_acc_u_usuario.
    /// 
    /// Importante:
    /// r.Rol NO se manda directo porque Dapper lo envía como integer.
    /// Se manda como string y se castea a rol_usuario.
    /// </summary>
    public async Task<string> ActualizarUsuario(
        int id,
        ActualizarUsuarioRequest r,
        CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("Id", id);
            p.Add("Nombre", r.Nombre);
            p.Add("Email", r.Email);
            p.Add("Rol", RolParaPostgres(r.Rol));

            await _db.Escalar<int>(
                """
                SELECT sp_acc_u_usuario(
                    @Id::int,
                    @Nombre::text,
                    @Email::text,
                    @Rol::rol_usuario
                )
                """,
                p,
                ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarPassword(
        int id,
        string passwordHash,
        CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_acc_u_usuario_password(
                    @id::int,
                    @passwordHash::text
                )
                """,
                new { id, passwordHash },
                ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarEstado(
        int id,
        bool activo,
        CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_acc_u_usuario_estado(
                    @id::int,
                    @activo::boolean
                )
                """,
                new { id, activo },
                ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
