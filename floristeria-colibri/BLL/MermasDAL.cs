using System.Text.Json;
using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de MERMAS.
///
/// La merma es la tapadera natural del robo —"se marchitó" es lo que se dice
/// cuando la flor se fue para la casa— así que este módulo carga varios
/// controles que otros no tienen: el origen se escanea, el costo lo pone el
/// sistema, y sobre cierto monto hace falta una firma.
/// </summary>
public class MermasDAL
{
    private readonly IAccesoDatos _db;

    public MermasDAL(IAccesoDatos db) => _db = db;

    /// <summary>camelCase: el SP lee 'componenteId' del JSONB.</summary>
    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<IEnumerable<Merma>> Consultar(
        MermaFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("ProductoId", f.ProductoId);
        p.Add("LoteId", f.LoteId);
        p.Add("Motivo", f.Motivo);
        // Enum nulable como texto: Dapper lo mandaría como integer y Postgres
        // no convierte integer a destino_merma.
        p.Add("Destino", f.Destino?.ToString());
        p.Add("Revertida", f.Revertida);
        p.Add("Desde", f.Desde);
        p.Add("Hasta", f.Hasta);
        p.Add("Pagina", f.PaginaReal);
        p.Add("Tamano", f.TamanoReal);

        return await _db.ConsultarLista<Merma>(
            """
            SELECT * FROM sp_mer_c_mermas(
                @Buscar::text, @ProductoId::int, @LoteId::int, @Motivo::text,
                @Destino::destino_merma, @Revertida::boolean,
                @Desde::date, @Hasta::date, @Pagina::int, @Tamano::int)
            """,
            p, ct);
    }

    public async Task<Merma?> ConsultarUna(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Merma>("SELECT * FROM sp_mer_c_merma(@id)", new { id }, ct);

    public async Task<IEnumerable<MotivoMerma>> ConsultarMotivos(CancellationToken ct = default)
        => await _db.ConsultarLista<MotivoMerma>("SELECT * FROM sp_mer_c_motivos()", null, ct);

    /// <summary>
    /// Acepta el código de un lote o de una partida, o el QR completo de
    /// cualquiera de los dos. Null cuando no existe: el mensaje lo arma el
    /// endpoint, que sabe qué se escaneó.
    /// </summary>
    public async Task<OrigenMerma?> Escanear(string codigo, CancellationToken ct = default)
        => await _db.ConsultarUno<OrigenMerma>(
            "SELECT * FROM sp_mer_c_escanear(@codigo)", new { codigo }, ct);

    public async Task<int> UmbralAutorizacion(CancellationToken ct = default)
        => await _db.Escalar<int>("SELECT fn_mer_umbral_autorizacion()", null, ct);

    // ============================================================
    // Resumen
    // ============================================================

    public async Task<ResumenMermas?> ConsultarResumen(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarUno<ResumenMermas>(
            "SELECT * FROM sp_mer_c_resumen(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<MermaPorDestino>> PorDestino(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorDestino>(
            "SELECT * FROM sp_mer_c_resumen_destino(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<MermaPorProducto>> PorProducto(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorProducto>(
            "SELECT * FROM sp_mer_c_resumen_producto(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<MermaPorMotivo>> PorMotivo(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorMotivo>(
            "SELECT * FROM sp_mer_c_resumen_motivo(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    // ============================================================
    // Patrones
    // ============================================================

    public async Task<IEnumerable<PatronUsuario>> PatronUsuario(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<PatronUsuario>(
            "SELECT * FROM sp_mer_c_patron_usuario(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<PatronHorario>> PatronHorario(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<PatronHorario>(
            "SELECT * FROM sp_mer_c_patron_horario(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<MermaSinEscanear>> SinEscanear(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaSinEscanear>(
            "SELECT * FROM sp_mer_c_sin_escanear(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>
    /// `autorizadoPor` es el NOMBRE de quien firmó, no su id: queda congelado
    /// en el registro aunque esa cuenta después se desactive.
    /// </summary>
    public async Task<(int Id, string Error)> Registrar(
        RegistrarMermaRequest r, string? autorizadoPor, int usuarioId,
        CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("ProductoId", r.ProductoId);
            p.Add("LoteId", r.LoteId);
            p.Add("PartidaId", r.PartidaId);
            p.Add("Cantidad", r.Cantidad);
            p.Add("Motivo", r.Motivo);
            p.Add("Detalle", r.Detalle);
            p.Add("Destino", r.Destino.ToString());
            p.Add("Recuperada", r.CantidadRecuperada);
            p.Add("Calidad", r.Calidad?.ToString());
            p.Add("CodigoEscaneado", r.CodigoEscaneado);
            p.Add("Escaneado", r.Escaneado);
            p.Add("AutorizadoPor", autorizadoPor);
            p.Add("UsuarioId", usuarioId);

            var id = await _db.Escalar<int>(
                """
                SELECT sp_mer_i_merma(
                    @ProductoId::int, @LoteId::int, @PartidaId::int, @Cantidad::int,
                    @Motivo::text, @Detalle::text, @Destino::destino_merma,
                    @Recuperada::int, @Calidad::calidad_reingreso,
                    @CodigoEscaneado::text, @Escaneado::boolean,
                    @AutorizadoPor::text, @UsuarioId::int)
                """,
                p, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<(int Id, string Error)> DescartarLote(
        int loteId, DescartarLoteRequest r, string? autorizadoPor, int usuarioId,
        CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_mer_i_descarte_lote(
                    @loteId::int, @Motivo::text, @Detalle::text,
                    @EsDevolucionProveedor::boolean, @autorizadoPor::text, @usuarioId::int)
                """,
                new
                {
                    loteId, r.Motivo, r.Detalle, r.EsDevolucionProveedor,
                    autorizadoPor, usuarioId
                }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> Revertir(
        int id, string motivo, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_mer_u_revertir(@id, @motivo, @usuarioId)",
                new { id, motivo, usuarioId }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    // ============================================================
    // Desarme
    // ============================================================

    public async Task<IEnumerable<LineaDesarme>> PlanDesarme(
        int productoId, int cantidad, CancellationToken ct = default)
        => await _db.ConsultarLista<LineaDesarme>(
            "SELECT * FROM sp_mer_c_plan_desarme(@productoId, @cantidad)",
            new { productoId, cantidad }, ct);

    public async Task<ResultadoOp<ResultadoDesarme>> Desarmar(
        int productoId, DesarmeRequest r, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("ProductoId", productoId);
            p.Add("Cantidad", r.Cantidad);
            p.Add("Motivo", r.Motivo);
            p.Add("Detalle", r.Detalle);
            p.Add("Lineas", JsonSerializer.Serialize(r.Lineas, JsonCamel));
            p.Add("UsuarioId", usuarioId);

            // Los alias son necesarios: el SP devuelve las columnas con
            // prefijo o_ y Dapper las mapearía a OProductoId, que no existe
            // en el modelo. El resultado sería un objeto con todo en cero,
            // sin ningún error.
            var res = await _db.ConsultarUno<ResultadoDesarme>(
                """
                SELECT o_producto_id AS producto_id,
                       o_producto    AS producto,
                       o_desarmados  AS desarmados,
                       o_mermas      AS mermas,
                       o_recuperadas AS recuperadas,
                       o_perdidas    AS perdidas
                FROM sp_mer_i_desarme(
                    @ProductoId::int, @Cantidad::int, @Motivo::text,
                    @Detalle::text, @Lineas::jsonb, @UsuarioId::int)
                """,
                p, ct);

            return res is null
                ? ResultadoOp<ResultadoDesarme>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoDesarme>.Exito(res);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoDesarme>.Error(ErroresPg.Mensaje(ex));
        }
    }
}
