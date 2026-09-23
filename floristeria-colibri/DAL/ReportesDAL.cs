using Colibri.Api.DbAccess;
using Colibri.Api.Models.Tablas;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de REPORTES.
///
/// Todo se calcula al vuelo. Una tabla de resumen se desincroniza en cuanto
/// se anula una boleta, y en un local con decenas de ventas diarias el
/// cálculo directo cuesta milisegundos.
/// </summary>
public class ReportesDAL
{
    private readonly IAccesoDatos _db;

    public ReportesDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Panel
    // ============================================================

    /// <summary>
    /// Las columnas vienen planas del SP y el BLL las arma en el objeto
    /// anidado que espera el front. Devolver JSON desde Postgres evitaría
    /// esta clase, pero perdería el tipado en C#.
    /// </summary>
    public async Task<PanelCrudo?> Panel(CancellationToken ct = default)
        => await _db.ConsultarUno<PanelCrudo>("SELECT * FROM sp_rep_c_panel()", null, ct);

    /// <summary>
    /// El mismo panel con solo las boletas de un usuario, y en null lo que es
    /// del local: costo, utilidad, inventario, clientes, puntos, cajón.
    /// </summary>
    public async Task<PanelCrudo?> PanelDe(int usuarioId, CancellationToken ct = default)
        => await _db.ConsultarUno<PanelCrudo>(
            "SELECT * FROM sp_rep_c_panel_usuario(@usuarioId::int)", new { usuarioId }, ct);

    public async Task<IEnumerable<Alerta>> Alertas(CancellationToken ct = default)
        => await _db.ConsultarLista<Alerta>("SELECT * FROM sp_rep_c_alertas()", null, ct);

    public async Task<IEnumerable<EventoProximo>> Eventos(
        int dias, CancellationToken ct = default)
        => await _db.ConsultarLista<EventoProximo>(
            "SELECT * FROM sp_rep_c_eventos(@dias)", new { dias }, ct);

    // ============================================================
    // Resultado
    // ============================================================

    public async Task<ResultadoPeriodo?> Resultado(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarUno<ResultadoPeriodo>(
            "SELECT * FROM sp_rep_c_resultado(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<DiaResultado>> ResultadoDias(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<DiaResultado>(
            "SELECT * FROM sp_rep_c_resultado_dias(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<IEnumerable<CategoriaResultado>> Categorias(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<CategoriaResultado>(
            "SELECT * FROM sp_rep_c_categorias(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    // ============================================================
    // Productos
    // ============================================================

    public async Task<IEnumerable<ProductoRendimiento>> Productos(
        DateOnly? desde, DateOnly? hasta, int limite, CancellationToken ct = default)
        => await _db.ConsultarLista<ProductoRendimiento>(
            "SELECT * FROM sp_rep_c_productos(@desde::date, @hasta::date, @limite)",
            new { desde, hasta, limite }, ct);

    // ============================================================
    // Inventario
    // ============================================================

    public async Task<ValorInventario?> Inventario(CancellationToken ct = default)
        => await _db.ConsultarUno<ValorInventario>(
            "SELECT * FROM sp_rep_c_inventario()", null, ct);

    public async Task<IEnumerable<InventarioPorCategoria>> InventarioCategorias(
        CancellationToken ct = default)
        => await _db.ConsultarLista<InventarioPorCategoria>(
            "SELECT * FROM sp_rep_c_inventario_categorias()", null, ct);

    // ============================================================
    // Equipo y turno
    // ============================================================

    public async Task<IEnumerable<PersonaRendimiento>> Equipo(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
        => await _db.ConsultarLista<PersonaRendimiento>(
            "SELECT * FROM sp_rep_c_equipo(@desde::date, @hasta::date)",
            new { desde, hasta }, ct);

    public async Task<DesgloseTurno?> Turno(int cajaId, CancellationToken ct = default)
        => await _db.ConsultarUno<DesgloseTurno>(
            "SELECT * FROM sp_rep_c_turno(@cajaId)", new { cajaId }, ct);
}

/// <summary>
/// Las columnas planas de sp_rep_c_panel. Solo existe para el salto entre
/// la fila del SP y el objeto anidado que consume el front.
/// </summary>
public class PanelCrudo
{
    public long HoyBoletas { get; set; }
    public long HoyVendido { get; set; }
    public long HoyTicketPromedio { get; set; }
    public long HoyUnidades { get; set; }
    public long? HoyCosto { get; set; }
    public long? HoyUtilidad { get; set; }
    public decimal? HoyMargen { get; set; }
    public long HoyAnuladas { get; set; }
    public long HoyDescuentos { get; set; }
    public long? HoyClientesNuevos { get; set; }

    public long SemBoletas { get; set; }
    public long SemVendido { get; set; }
    public decimal Variacion { get; set; }

    public int? CajaId { get; set; }
    public string? CajaAbiertaPor { get; set; }
    public DateTime? CajaAbiertaEn { get; set; }
    public int? CajaFondo { get; set; }
    public int? CajaEfectivo { get; set; }
    public int? CajaEnCajon { get; set; }
    public long CajaBoletas { get; set; }

    public long MesVendido { get; set; }
    public long MesBoletas { get; set; }
    public decimal? InventarioValorizado { get; set; }
    public long? ClientesActivos { get; set; }
    public long? PuntosPorPagar { get; set; }
}
