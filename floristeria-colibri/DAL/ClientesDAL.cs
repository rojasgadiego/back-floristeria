using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

public class ClientesDAL
{
    private readonly IAccesoDatos _db;

    public ClientesDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<IEnumerable<Cliente>> Consultar(
        ClienteFiltro f, CancellationToken ct = default)
        => await _db.ConsultarLista<Cliente>(
            """
            SELECT * FROM sp_cli_c_clientes(
                @Buscar::text, @Activo::boolean, @ConPuntos::boolean,
                @CumpleMes::int, @SinComprarDias::int, @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Buscar, f.Activo,
                ConPuntos = f.ConPuntos ?? false,
                f.CumpleMes, f.SinComprarDias,
                Pagina = f.PaginaReal, Tamano = f.TamanoReal
            }, ct);

    public async Task<ClienteDetalle?> ConsultarUno(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<ClienteDetalle>(
            "SELECT * FROM sp_cli_c_cliente(@id)", new { id }, ct);

    /// <summary>
    /// Null cuando no existe. NO es un error: en el mesón lo normal es que
    /// el cliente no esté registrado.
    /// </summary>
    public async Task<Cliente?> ConsultarPorRut(string rut, CancellationToken ct = default)
        => await _db.ConsultarUno<Cliente>(
            "SELECT * FROM sp_cli_c_cliente_rut(@rut)", new { rut }, ct);

    public async Task<IEnumerable<CompraCliente>> Compras(
        int clienteId, int pagina, int tamano, CancellationToken ct = default)
        => await _db.ConsultarLista<CompraCliente>(
            "SELECT * FROM sp_cli_c_compras(@clienteId, @pagina, @tamano)",
            new { clienteId, pagina, tamano }, ct);

    public async Task<IEnumerable<MovimientoPuntos>> Puntos(
        int clienteId, int pagina, int tamano, CancellationToken ct = default)
        => await _db.ConsultarLista<MovimientoPuntos>(
            "SELECT * FROM sp_cli_c_puntos(@clienteId, @pagina, @tamano)",
            new { clienteId, pagina, tamano }, ct);

    public async Task<IEnumerable<ProductoFrecuente>> Frecuentes(
        int clienteId, int limite, CancellationToken ct = default)
        => await _db.ConsultarLista<ProductoFrecuente>(
            "SELECT * FROM sp_cli_c_frecuentes(@clienteId, @limite)",
            new { clienteId, limite }, ct);

    public async Task<IEnumerable<ClienteCumpleanos>> Cumpleanos(
        int? mes, CancellationToken ct = default)
        => await _db.ConsultarLista<ClienteCumpleanos>(
            "SELECT * FROM sp_cli_c_cumpleanos(@mes::int)", new { mes }, ct);

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<(int Id, string Error)> Insertar(
        ClienteRequest r, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_cli_i_cliente(
                    @Rut::text, @Nombre::text, @Telefono::text, @Correo::text,
                    @Direccion::text, @CumpleMes::int, @CumpleDia::int, @Notas::text)
                """,
                r, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> Actualizar(
        int id, ClienteRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_cli_u_cliente(
                    @id::int, @Rut::text, @Nombre::text, @Telefono::text,
                    @Correo::text, @Direccion::text, @CumpleMes::int,
                    @CumpleDia::int, @Notas::text)
                """,
                new
                {
                    id, r.Rut, r.Nombre, r.Telefono, r.Correo,
                    r.Direccion, r.CumpleMes, r.CumpleDia, r.Notas
                }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarEstado(
        int id, bool activo, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_cli_u_estado(@id, @activo)", new { id, activo }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> AjustarPuntos(
        int clienteId, int cantidad, string motivo, int usuarioId,
        CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_cli_i_ajuste_puntos(@clienteId, @cantidad, @motivo, @usuarioId)",
                new { clienteId, cantidad, motivo, usuarioId }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
