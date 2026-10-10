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
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // Los enums como texto: la base los castea ('buena'::calidad_reingreso).
        // Antes la calidad del desarme viajaba como número.
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };

    // ============================================================
    // Consultas
    // ============================================================

    /// <summary>
    /// `soloDe`: null para el administrador (todas); con un id, solo las
    /// mermas que registró esa persona.
    /// </summary>
    public async Task<IEnumerable<Merma>> Consultar(
        MermaFiltro f, int? soloDe, CancellationToken ct = default)
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
        p.Add("SoloDe", soloDe);

        return await _db.ConsultarLista<Merma>(
            """
            SELECT * FROM sp_mer_c_mermas(
                @Buscar::text, @ProductoId::int, @LoteId::int, @Motivo::text,
                @Destino::destino_merma, @Revertida::boolean,
                @Desde::date, @Hasta::date, @Pagina::int, @Tamano::int,
                @SoloDe::int)
            """,
            p, ct);
    }

    public async Task<Merma?> ConsultarUna(int id, int? soloDe = null, CancellationToken ct = default)
        => await _db.ConsultarUno<Merma>(
            "SELECT * FROM sp_mer_c_merma(@id, @soloDe::int)", new { id, soloDe }, ct);

    /// <summary>`todos` incluye los apagados: la pantalla de administración.</summary>
    public async Task<IEnumerable<MotivoMerma>> ConsultarMotivos(bool todos, CancellationToken ct = default)
        => await _db.ConsultarLista<MotivoMerma>(
            "SELECT * FROM sp_mer_c_motivos(@todos)", new { todos }, ct);

    public async Task<(int Id, string Error)> CrearMotivo(
        MotivoMermaRequest r, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_mer_i_motivo(@Nombre::text, @Categoria::text,
                    @RequiereDetalle::boolean, @Destino::destino_merma)
                """,
                new { r.Nombre, r.Categoria, r.RequiereDetalle, Destino = r.DestinoSugerido?.ToString() }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> ActualizarMotivo(
        int id, MotivoMermaRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_mer_u_motivo(@id, @Nombre::text, @Categoria::text,
                    @RequiereDetalle::boolean, @Destino::destino_merma,
                    @Activo::boolean, @Orden::int)
                """,
                new
                {
                    id, r.Nombre, r.Categoria, r.RequiereDetalle,
                    Destino = r.DestinoSugerido?.ToString(), r.Activo, r.Orden
                }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

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
    // Códigos de autorización (sql/17)
    // ============================================================

    public async Task GuardarCodigo(
        string codigo, int valor, int usuarioId, CancellationToken ct = default)
        => await _db.Ejecutar(
            """
            INSERT INTO codigos_autorizacion_merma (codigo, valor, usuario_id)
            VALUES (@codigo, @valor, @usuarioId)
            """,
            new { codigo, valor, usuarioId }, ct);

    /// <summary>
    /// Consume el código si es de esta persona, no se usó y no venció.
    /// True si sirvió.
    /// </summary>
    public async Task<bool> UsarCodigo(
        string codigo, int usuarioId, CancellationToken ct = default)
        => await _db.Ejecutar(
            """
            UPDATE codigos_autorizacion_merma
               SET usado = true
             WHERE codigo = @codigo
               AND usuario_id = @usuarioId
               AND NOT usado
               AND expira_en > now()
            """,
            new { codigo, usuarioId }, ct) > 0;

    // ============================================================
    // Resumen
    // ============================================================

    // `soloDe` como en el listado. Con un id, el resumen tampoco trae las
    // ventas del local: son un número del negocio, no de una persona.

    public async Task<ResumenMermas?> ConsultarResumen(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
        => await _db.ConsultarUno<ResumenMermas>(
            "SELECT * FROM sp_mer_c_resumen(@desde::date, @hasta::date, @soloDe::int)",
            new { desde, hasta, soloDe }, ct);

    public async Task<IEnumerable<MermaPorDestino>> PorDestino(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorDestino>(
            "SELECT * FROM sp_mer_c_resumen_destino(@desde::date, @hasta::date, @soloDe::int)",
            new { desde, hasta, soloDe }, ct);

    public async Task<IEnumerable<MermaPorProducto>> PorProducto(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorProducto>(
            "SELECT * FROM sp_mer_c_resumen_producto(@desde::date, @hasta::date, @soloDe::int)",
            new { desde, hasta, soloDe }, ct);

    public async Task<IEnumerable<MermaPorCategoria>> PorCategoria(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorCategoria>(
            "SELECT * FROM sp_mer_c_resumen_categoria(@desde::date, @hasta::date, @soloDe::int)",
            new { desde, hasta, soloDe }, ct);

    public async Task<IEnumerable<MermaPorMotivo>> PorMotivo(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
        => await _db.ConsultarLista<MermaPorMotivo>(
            "SELECT * FROM sp_mer_c_resumen_motivo(@desde::date, @hasta::date, @soloDe::int)",
            new { desde, hasta, soloDe }, ct);

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

    // ============================================================
    // Desarme
    // ============================================================

    public async Task<IEnumerable<LineaDesarme>> PlanDesarme(
        int productoId, int cantidad, CancellationToken ct = default)
        => await _db.ConsultarLista<LineaDesarme>(
            "SELECT * FROM sp_mer_c_plan_desarme(@productoId, @cantidad)",
            new { productoId, cantidad }, ct);

    public async Task<ResultadoOp<ResultadoDesarme>> Desarmar(
        int productoId, DesarmeRequest r, string? autorizadoPor, int usuarioId,
        CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("ProductoId", productoId);
            p.Add("Cantidad", r.Cantidad);
            p.Add("Motivo", r.Motivo);
            p.Add("Detalle", r.Detalle);
            p.Add("Lineas", JsonSerializer.Serialize(r.Lineas, JsonCamel));
            p.Add("Fotos", JsonSerializer.Serialize(r.Fotos, JsonCamel));
            p.Add("UsuarioId", usuarioId);
            p.Add("AutorizadoPor", autorizadoPor);

            // Los alias son necesarios: el SP devuelve las columnas con
            // prefijo o_ y Dapper las mapearía a OProductoId, que no existe
            // en el modelo. El resultado sería un objeto con todo en cero,
            // sin ningún error.
            var res = await _db.ConsultarUno<ResultadoDesarme>(
                """
                SELECT x.reporte_id, x.mermas, x.recuperadas, x.perdidas, x.foto_repetida,
                       @ProductoId AS producto_id, @Cantidad AS desarmados
                  FROM sp_mer_i_reporte_desarme(
                    @ProductoId::int, @Cantidad::int, @Motivo::text, @Detalle::text,
                    @Lineas::jsonb,
                    ARRAY(SELECT decode(f.v->>'base64', 'base64')
                            FROM jsonb_array_elements(@Fotos::jsonb) WITH ORDINALITY AS f(v, n)
                           ORDER BY f.n),
                    ARRAY(SELECT f.v->>'tipoMime'
                            FROM jsonb_array_elements(@Fotos::jsonb) WITH ORDINALITY AS f(v, n)
                           ORDER BY f.n),
                    @AutorizadoPor::text, @UsuarioId::int) x
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

    // ============================================================
    // Reportes con evidencia (sql/23)
    // ============================================================

    /// <summary>
    /// Todo o nada. Las fotos viajan en base64 dentro de un JSON y la base las
    /// convierte a binario en la misma llamada: así no dependemos de cómo pasa
    /// arreglos de bytea el driver.
    /// </summary>
    public async Task<ResultadoOp<ReporteRegistrado>> RegistrarReporte(
        RegistrarReporteRequest r, bool soloVitrina, string? autorizadoPor, int usuarioId,
        CancellationToken ct = default)
    {
        try
        {
            var res = await _db.ConsultarUno<ReporteRegistrado>(
                """
                SELECT x.reporte_id, x.mermas, x.valor, x.foto_repetida,
                       now() + interval '10 minutes' AS deshacible_hasta
                  FROM sp_mer_i_reporte(
                    @tipo::text, @lineas::jsonb, @motivo::text, @detalle::text,
                    ARRAY(SELECT decode(f.v->>'base64', 'base64')
                            FROM jsonb_array_elements(@fotos::jsonb) WITH ORDINALITY AS f(v, n)
                           ORDER BY f.n),
                    ARRAY(SELECT f.v->>'tipoMime'
                            FROM jsonb_array_elements(@fotos::jsonb) WITH ORDINALITY AS f(v, n)
                           ORDER BY f.n),
                    @soloVitrina::boolean, @autorizadoPor::text, @usuarioId::int) x
                """,
                new
                {
                    tipo = r.Tipo,
                    lineas = JsonSerializer.Serialize(r.Lineas, JsonCamel),
                    motivo = r.Motivo,
                    detalle = r.Detalle,
                    fotos = JsonSerializer.Serialize(r.Fotos, JsonCamel),
                    soloVitrina,
                    autorizadoPor,
                    usuarioId
                }, ct);

            return res is null
                ? ResultadoOp<ReporteRegistrado>.Error("La función no devolvió resultado.")
                : ResultadoOp<ReporteRegistrado>.Exito(res);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ReporteRegistrado>.Error(ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> DeshacerReporte(
        int reporteId, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_mer_u_deshacer_reporte(@reporteId, @usuarioId)",
                new { reporteId, usuarioId }, ct);
            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    /// <summary>Quién registró el reporte. Null si no existe.</summary>
    public async Task<int?> DuenoReporte(int reporteId, CancellationToken ct = default)
        => await _db.ConsultarUno<int?>(
            "SELECT usuario_id FROM merma_reportes WHERE id = @reporteId", new { reporteId }, ct);

    public async Task<IEnumerable<EvidenciaMerma>> Evidencias(int reporteId, CancellationToken ct = default)
        => await _db.ConsultarLista<EvidenciaMerma>(
            "SELECT * FROM sp_mer_c_evidencias(@reporteId)", new { reporteId }, ct);

    public async Task<FotoEvidencia?> Evidencia(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<FotoEvidencia>(
            "SELECT * FROM sp_mer_c_evidencia(@id)", new { id }, ct);

    /// <summary>Las líneas de un reporte, para el correo.</summary>
    public async Task<IEnumerable<Merma>> MermasDeReporte(int reporteId, CancellationToken ct = default)
        => await _db.ConsultarLista<Merma>(
            """
            SELECT * FROM sp_mer_c_mermas(p_revertida => NULL, p_tamano => 200)
             WHERE reporte_id = @reporteId
            """,
            new { reporteId }, ct);

    // ============================================================
    // Revisión (sql/24)
    // ============================================================

    public async Task<IEnumerable<ReportePendiente>> Pendientes(CancellationToken ct = default)
        => await _db.ConsultarLista<ReportePendiente>(
            "SELECT * FROM sp_mer_c_reportes_pendientes(100)", null, ct);

    public async Task<string> MarcarRevisado(
        int reporteId, int usuarioId, string? nota, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_mer_u_reporte_revisado(@reporteId, @usuarioId, @nota)",
                new { reporteId, usuarioId, nota }, ct);
            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
