using Colibri.Api.DAL;
using Colibri.Api.Models.Tablas;

namespace Colibri.Api.BLL;

public class ReportesBLL
{
    private readonly ReportesDAL _dal;

    public ReportesBLL(ReportesDAL dal) => _dal = dal;

    // ============================================================
    // Panel
    // ============================================================

    /// <summary>
    /// Tres consultas: los números, las alertas y lo que viene. Van juntas
    /// porque es una sola pantalla, y tres viajes desde el front harían que
    /// se pinte por partes.
    /// </summary>
    public async Task<Panel?> ObtenerPanel(CancellationToken ct = default)
    {
        var c = await _dal.Panel(ct);
        if (c is null) return null;

        return new Panel
        {
            Hoy = new PanelHoy
            {
                Boletas = c.HoyBoletas,
                Vendido = c.HoyVendido,
                TicketPromedio = c.HoyTicketPromedio,
                Unidades = c.HoyUnidades,
                Costo = c.HoyCosto,
                Utilidad = c.HoyUtilidad,
                Margen = c.HoyMargen,
                Anuladas = c.HoyAnuladas,
                Descuentos = c.HoyDescuentos,
                ClientesNuevos = c.HoyClientesNuevos
            },

            SemanaPasada = new PanelComparativo
            {
                Boletas = c.SemBoletas,
                Vendido = c.SemVendido
            },

            VariacionSemanal = c.Variacion,

            /* Null cuando no hay turno: el front muestra "abre la caja" en vez
               de ceros, que parecerían un día sin ventas. */
            Caja = c.CajaId is null ? null : new PanelCaja
            {
                Id = c.CajaId.Value,
                AbiertaPor = c.CajaAbiertaPor,
                AbiertaEn = c.CajaAbiertaEn ?? default,
                Fondo = c.CajaFondo ?? 0,
                Efectivo = c.CajaEfectivo ?? 0,
                EnCajon = c.CajaEnCajon ?? 0,
                Boletas = c.CajaBoletas
            },

            Contexto = new PanelContexto
            {
                MesVendido = c.MesVendido,
                MesBoletas = c.MesBoletas,
                InventarioValorizado = c.InventarioValorizado,
                ClientesActivos = c.ClientesActivos,
                PuntosPorPagar = c.PuntosPorPagar
            },

            Alertas = (await _dal.Alertas(ct)).ToList(),
            ProximosEventos = (await _dal.Eventos(14, ct)).ToList()
        };
    }

    // ============================================================
    // Resultado
    // ============================================================

    /// <summary>
    /// Sin fechas toma los últimos 30 días. El rango se limita a un año: más
    /// que eso no es un reporte de pantalla, es una exportación.
    /// </summary>
    public async Task<ResultadoPeriodo?> Resultado(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        (desde, hasta) = NormalizarRango(desde, hasta);

        var r = await _dal.Resultado(desde, hasta, ct);
        if (r is null) return null;

        r.PorDia = (await _dal.ResultadoDias(desde, hasta, ct)).ToList();
        r.PorCategoria = (await _dal.Categorias(desde, hasta, ct)).ToList();

        return r;
    }

    public async Task<RendimientoProductos> Productos(
        DateOnly? desde, DateOnly? hasta, int? limite, CancellationToken ct = default)
    {
        (desde, hasta) = NormalizarRango(desde, hasta);

        return new RendimientoProductos
        {
            Desde = desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
            Hasta = hasta ?? DateOnly.FromDateTime(DateTime.Today),
            Productos = (await _dal.Productos(desde, hasta, Math.Clamp(limite ?? 20, 1, 100), ct)).ToList(),
            Categorias = (await _dal.Categorias(desde, hasta, ct)).ToList()
        };
    }

    public async Task<ValorInventario?> Inventario(CancellationToken ct = default)
    {
        var v = await _dal.Inventario(ct);
        if (v is null) return null;

        v.PorCategoria = (await _dal.InventarioCategorias(ct)).ToList();
        return v;
    }

    public async Task<RendimientoEquipo> Equipo(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        (desde, hasta) = NormalizarRango(desde, hasta);

        return new RendimientoEquipo
        {
            Desde = desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30)),
            Hasta = hasta ?? DateOnly.FromDateTime(DateTime.Today),
            Personas = (await _dal.Equipo(desde, hasta, ct)).ToList()
        };
    }

    public async Task<DesgloseTurno?> Turno(int cajaId, CancellationToken ct = default)
        => cajaId <= 0 ? null : await _dal.Turno(cajaId, ct);

    /// <summary>
    /// Si las fechas vienen al revés se dan vuelta en vez de devolver vacío:
    /// es un error de dedo, no una consulta legítima de cero días.
    ///
    /// Y el rango se corta a un año: más que eso hace que el gráfico por día
    /// devuelva 400 puntos que nadie puede leer.
    /// </summary>
    private static (DateOnly?, DateOnly?) NormalizarRango(DateOnly? desde, DateOnly? hasta)
    {
        if (desde.HasValue && hasta.HasValue && desde > hasta)
            (desde, hasta) = (hasta, desde);

        if (desde.HasValue && hasta.HasValue && hasta.Value.DayNumber - desde.Value.DayNumber > 366)
            desde = hasta.Value.AddDays(-366);

        return (desde, hasta);
    }
}
