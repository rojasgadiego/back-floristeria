using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

public class MostradorDAL
{
    private readonly IAccesoDatos _db;

    public MostradorDAL(IAccesoDatos db) => _db = db;

    public async Task<IEnumerable<Partida>> ConsultarPartidas(
        PartidaFiltro f, CancellationToken ct = default)
        => await _db.ConsultarLista<Partida>(
            """
            SELECT * FROM sp_ven_c_partidas(
                @Buscar::text, @ProductoId::int, @Historial::boolean,
                @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Buscar, f.ProductoId,
                Historial = f.Historial ?? false,
                Pagina = f.PaginaReal, Tamano = f.TamanoReal
            }, ct);

    /// <summary>Acepta el código pelado o el QR completo. Lo resuelve el SP.</summary>
    public async Task<PartidaEscaneada?> Escanear(string codigo, CancellationToken ct = default)
        => await _db.ConsultarUno<PartidaEscaneada>(
            "SELECT * FROM sp_ven_c_partida(@codigo)", new { codigo }, ct);

    public async Task<IEnumerable<PartidaOrden>> DeProducto(
        int productoId, CancellationToken ct = default)
        => await _db.ConsultarLista<PartidaOrden>(
            "SELECT * FROM sp_ven_c_partidas_producto(@productoId)", new { productoId }, ct);

    /// <summary>
    /// Los alias son necesarios: el SP devuelve las columnas con prefijo o_
    /// —para que dentro de PL/pgSQL no choquen con las columnas de las
    /// tablas— y Dapper las mapearía a OPartidaId, OCodigo… que no existen
    /// en el modelo. El resultado sería un objeto con todo en cero, sin
    /// ningún error.
    /// </summary>
    public async Task<ResultadoOp<ResultadoTraspaso>> Traspasar(
        string lote, int cantidad, int usuarioId, string? notas, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoTraspaso>(
                """
                SELECT o_partida_id   AS partida_id,
                       o_codigo       AS codigo,
                       o_qr           AS qr,
                       o_producto_id  AS producto_id,
                       o_producto     AS producto,
                       o_lote_codigo  AS lote_codigo,
                       o_cantidad     AS cantidad,
                       o_en_bodega    AS en_bodega,
                       o_en_mostrador AS en_mostrador,
                       o_precio       AS precio,
                       o_vencimiento  AS vencimiento
                FROM sp_ven_i_traspaso(@lote, @cantidad, @usuarioId, @notas)
                """,
                new { lote, cantidad, usuarioId, notas }, ct);

            return r is null
                ? ResultadoOp<ResultadoTraspaso>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoTraspaso>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoTraspaso>.Error(ErroresPg.Mensaje(ex));
        }
    }

     public async Task<ResultadoOp<ResultadoTraspaso>> TraspasarSimple(
        int productoId, int cantidad, int usuarioId, string? notas, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoTraspaso>(
                """
                SELECT o_partida_id   AS partida_id,
                       o_codigo       AS codigo,
                       o_qr           AS qr,
                       o_producto_id  AS producto_id,
                       o_producto     AS producto,
                       o_lote_codigo  AS lote_codigo,
                       o_cantidad     AS cantidad,
                       o_en_bodega    AS en_bodega,
                       o_en_mostrador AS en_mostrador,
                       o_precio       AS precio,
                       o_vencimiento  AS vencimiento
                FROM sp_ven_i_traspaso_simple(@productoId, @cantidad, @usuarioId, @notas)
                """,
                new { productoId, cantidad, usuarioId, notas }, ct);

            return r is null
                ? ResultadoOp<ResultadoTraspaso>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoTraspaso>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoTraspaso>.Error(ErroresPg.Mensaje(ex));
        }
    }

   public async Task<ResultadoOp<ResultadoRetorno>> Retornar(
        string partida, int cantidad, int usuarioId, string? notas, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoRetorno>(
                """
                SELECT o_partida_id AS partida_id,
                       o_codigo     AS codigo,
                       o_producto   AS producto,
                       o_devuelto   AS devuelto,
                       o_en_partida AS en_partida,
                       o_en_bodega  AS en_bodega
                FROM sp_ven_u_partida_retorno(@partida, @cantidad, @usuarioId, @notas)
                """,
                new { partida, cantidad, usuarioId, notas }, ct);

            return r is null
                ? ResultadoOp<ResultadoRetorno>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoRetorno>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoRetorno>.Error(ErroresPg.Mensaje(ex));
        }
    }
}
