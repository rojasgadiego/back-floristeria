using Npgsql;

namespace Colibri.Api.DbAccess;

/// <summary>
/// Equivalente a IDbAccess (LoadData / SaveData), adaptado a PostgreSQL.
/// Es la única puerta de salida hacia la base.
/// </summary>
public interface IAccesoDatos
{
    Task<IEnumerable<T>> ConsultarLista<T>(string sql, object? p = null, CancellationToken ct = default);

    Task<T?> ConsultarUno<T>(string sql, object? p = null, CancellationToken ct = default);

    Task<T?> Escalar<T>(string sql, object? p = null, CancellationToken ct = default);

    Task<int> Ejecutar(string sql, object? p = null, CancellationToken ct = default);

    /// <summary>
    /// Varias funciones en un mismo commit. Solo hace falta cuando una operación
    /// toca dos funciones distintas; una sola función ya es atómica.
    /// </summary>
    Task<T> EnTransaccion<T>(
        Func<NpgsqlConnection, NpgsqlTransaction, Task<T>> trabajo,
        CancellationToken ct = default);
}
