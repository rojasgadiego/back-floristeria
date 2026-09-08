using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de LOTES.
///
/// El lote es entidad propia y no un detalle del producto: tiene QR,
/// ubicación física, vencimiento y una posición en la fila de consumo. Por
/// eso tiene su propio DAL y no cuelga de InventarioDAL.
/// </summary>
public class LotesDAL
{
    private readonly IAccesoDatos _db;

    public LotesDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<IEnumerable<Lote>> ConsultarLotes(
        LoteFiltro f, CancellationToken ct = default)
        => await _db.ConsultarLista<Lote>(
            """
            SELECT * FROM sp_lot_c_lotes(
                @Buscar::text, @ProductoId::int, @ProveedorId::int, @Alerta::text,
                @SoloRezagados::boolean, @Historial::boolean, @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Buscar, f.ProductoId, f.ProveedorId, f.Alerta,
                SoloRezagados = f.SoloRezagados ?? false,
                Historial = f.Historial ?? false,
                Pagina = f.PaginaReal,
                Tamano = f.TamanoReal
            }, ct);

    public async Task<LoteDetalle?> ConsultarLote(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<LoteDetalle>(
            "SELECT * FROM sp_lot_c_lote(@id)", new { id }, ct);

    /// <summary>Acepta el código pelado o el QR completo. Lo resuelve el SP.</summary>
    public async Task<LoteDetalle?> ConsultarLotePorCodigo(
        string codigo, CancellationToken ct = default)
        => await _db.ConsultarUno<LoteDetalle>(
            "SELECT * FROM sp_lot_c_lote_codigo(@codigo)", new { codigo }, ct);

    public async Task<IEnumerable<MovimientoLote>> ConsultarMovimientos(
        int loteId, CancellationToken ct = default)
        => await _db.ConsultarLista<MovimientoLote>(
            "SELECT * FROM sp_lot_c_movimientos(@loteId)", new { loteId }, ct);

    public async Task<IEnumerable<LoteAlerta>> ConsultarRezagados(CancellationToken ct = default)
        => await _db.ConsultarLista<LoteAlerta>(
            "SELECT * FROM sp_lot_c_rezagados()", null, ct);

    public async Task<IEnumerable<LoteAlerta>> ConsultarPorVencer(
        int dias, CancellationToken ct = default)
        => await _db.ConsultarLista<LoteAlerta>(
            "SELECT * FROM sp_lot_c_por_vencer(@dias)", new { dias }, ct);

    public async Task<IEnumerable<LoteAlerta>> ConsultarRecuperados(CancellationToken ct = default)
        => await _db.ConsultarLista<LoteAlerta>(
            "SELECT * FROM sp_lot_c_recuperados()", null, ct);

    /// <summary>
    /// El SP devuelve la columna como costo_promedio; la propiedad se llama
    /// CostoPromedioPonderado para que nadie la confunda con un promedio
    /// simple, así que necesita alias explícito.
    /// </summary>
    public async Task<IEnumerable<CostoPromedio>> ConsultarCostoPromedio(
        CancellationToken ct = default)
        => await _db.ConsultarLista<CostoPromedio>(
            """
            SELECT producto_id, producto, emoji, lotes, varas,
                   costo_promedio AS costo_promedio_ponderado,
                   costo_minimo, costo_maximo, valor_total
            FROM sp_lot_c_costo_promedio()
            """,
            null, ct);

    /// <summary>
    /// Npgsql mapea int[] de C# a int[] de Postgres sin ayuda: no hace falta
    /// armar una lista de parámetros ni concatenar el SQL.
    /// </summary>
    public async Task<IEnumerable<EtiquetaLote>> ConsultarEtiquetas(
        int[] ids, CancellationToken ct = default)
        => await _db.ConsultarLista<EtiquetaLote>(
            "SELECT * FROM sp_lot_c_etiquetas(@ids)", new { ids }, ct);

    public async Task<IEnumerable<EtiquetaLote>> ConsultarEtiquetasCompra(
        int compraId, CancellationToken ct = default)
        => await _db.ConsultarLista<EtiquetaLote>(
            """
            SELECT * FROM sp_lot_c_etiquetas(
                ARRAY(SELECT id FROM lotes WHERE compra_id = @compraId))
            """,
            new { compraId }, ct);

    /// <summary>
    /// sp_lot_c_validar — el escaneo del punto de venta. Informativo: la
    /// validación que manda ocurre al cobrar.
    /// </summary>
    public async Task<ValidacionLote?> Validar(
        string codigo, int cantidad, CancellationToken ct = default)
        => await _db.ConsultarUno<ValidacionLote>(
            "SELECT * FROM sp_lot_c_validar(@codigo, @cantidad)",
            new { codigo, cantidad }, ct);

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>
    /// Lo único editable de un lote. Las varas se mueven recibiendo,
    /// vendiendo, traspasando o mermando.
    /// </summary>
    public async Task<string> ActualizarUbicacion(
        int id, string ubicacion, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_lot_u_ubicacion(@id, @ubicacion, @usuarioId)",
                new { id, ubicacion, usuarioId }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
