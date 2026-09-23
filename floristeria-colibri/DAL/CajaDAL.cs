using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de CAJA.
///
/// Sin caja abierta no se vende: este DAL lo consulta el punto de venta
/// antes que nada. La primera pregunta no es qué vender sino si se puede.
/// </summary>
public class CajaDAL
{
    private readonly IAccesoDatos _db;

    public CajaDAL(IAccesoDatos db) => _db = db;

    /// <summary>
    /// Null cuando no hay turno abierto. NO es un error: es el estado normal
    /// antes de que alguien abra por la mañana.
    /// </summary>
    public async Task<Caja?> ConsultarActual(CancellationToken ct = default)
        => await _db.ConsultarUno<Caja>("SELECT * FROM sp_ven_c_caja_actual()", null, ct);

    public async Task<Caja?> ConsultarCaja(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Caja>("SELECT * FROM sp_ven_c_caja(@id)", new { id }, ct);

    public async Task<IEnumerable<Caja>> ConsultarHistorial(
        CajaFiltro f, CancellationToken ct = default)
        => await _db.ConsultarLista<Caja>(
            """
            SELECT * FROM sp_ven_c_cajas(
                @Desde::date, @Hasta::date, @UsuarioId::int, @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Desde, f.Hasta, f.UsuarioId,
                Pagina = f.PaginaReal, Tamano = f.TamanoReal
            }, ct);

    /// <summary>
    /// La caja vista por un vendedor: sus propios totales y el arqueo en
    /// null (sp_ven_c_caja_usuario). id null = la caja abierta.
    /// </summary>
    public async Task<Caja?> ConsultarCajaDe(
        int? id, int usuarioId, CancellationToken ct = default)
        => await _db.ConsultarUno<Caja>(
            "SELECT * FROM sp_ven_c_caja_usuario(@id::int, @usuarioId::int)",
            new { id, usuarioId }, ct);

    /// <summary>Los turnos en los que el vendedor vendió, con sus totales.</summary>
    public async Task<IEnumerable<Caja>> ConsultarHistorialDe(
        CajaFiltro f, int usuarioId, CancellationToken ct = default)
        => await _db.ConsultarLista<Caja>(
            """
            SELECT * FROM sp_ven_c_cajas_usuario(
                @Desde::date, @Hasta::date, @UsuarioId::int, @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Desde, f.Hasta, UsuarioId = usuarioId,
                Pagina = f.PaginaReal, Tamano = f.TamanoReal
            }, ct);

    public async Task<(int Id, string Error)> Abrir(
        int fondoInicial, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                "SELECT sp_ven_i_caja(@fondoInicial, @usuarioId)",
                new { fondoInicial, usuarioId }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<(int Id, string Error)> Cerrar(
        int efectivoContado, int usuarioId, string? nota, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                "SELECT sp_ven_u_caja_cerrar(@efectivoContado, @usuarioId, @nota)",
                new { efectivoContado, usuarioId, nota }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }
}
