using Dapper;
using Npgsql;

namespace Colibri.Api.DbAccess;

/// <summary>
/// Todas las conexiones nacen del NpgsqlDataSource, NUNCA de
/// `new NpgsqlConnection(cadena)`. El mapeo de los enums (MapEnum) vive en el
/// data source: una conexión creada a mano no conoce tipo_producto y cualquier
/// consulta que lo devuelva revienta en runtime con un error que no menciona la
/// causa.
/// </summary>
public sealed class AccesoDatosPostgres : IAccesoDatos
{
    private readonly NpgsqlDataSource _ds;
    private readonly int _timeout;

    public AccesoDatosPostgres(NpgsqlDataSource ds, IConfiguration cfg)
    {
        _ds = ds;
        _timeout = cfg.GetValue<int?>("Postgres:CommandTimeout") ?? 30;
    }

    private CommandDefinition Cmd(string sql, object? p, CancellationToken ct)
        => new(sql, p, commandTimeout: _timeout, cancellationToken: ct);

    public async Task<IEnumerable<T>> ConsultarLista<T>(
        string sql, object? p = null, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        // QueryAsync bufferiza: se cierra la conexión y la lista sigue viva.
        return await conn.QueryAsync<T>(Cmd(sql, p, ct));
    }

    public async Task<T?> ConsultarUno<T>(
        string sql, object? p = null, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.QueryFirstOrDefaultAsync<T>(Cmd(sql, p, ct));
    }

    public async Task<T?> Escalar<T>(
        string sql, object? p = null, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteScalarAsync<T>(Cmd(sql, p, ct));
    }

    public async Task<int> Ejecutar(
        string sql, object? p = null, CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        return await conn.ExecuteAsync(Cmd(sql, p, ct));
    }

    public async Task<T> EnTransaccion<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> trabajo,
        CancellationToken ct = default)
    {
        await using var conn = await _ds.OpenConnectionAsync(ct);
        await using var tx = await conn.BeginTransactionAsync(ct);
        try
        {
            var r = await trabajo(conn, tx);
            await tx.CommitAsync(ct);
            return r;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }
}
