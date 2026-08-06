using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Reportes.Dtos;

namespace Colibri.Api.Features.Reportes;

public class ReportesService : IReportesService
{
    private static readonly string[] NombresDia =
    {
        "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"
    };

    // Un rango mayor cargaría demasiadas filas para agrupar en memoria. Un año
    // es de sobra para las preguntas que este sistema responde.
    private const int MaximoDias = 366;

    private readonly ColibriDbContext _db;

    public ReportesService(ColibriDbContext db) => _db = db;

    /* ==================================================================
       PANEL
       ================================================================== */

    public async Task<PanelDto> PanelAsync(CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var comparacion = hoy.AddDays(-7);

        var dias = await _db.ResultadosDiarios.AsNoTracking()
            .Where(r => r.Dia == hoy || r.Dia == comparacion)
            .ToListAsync(ct);

        var deHoy = ADia(dias.FirstOrDefault(d => d.Dia == hoy), hoy);
        var pasado = dias.FirstOrDefault(d => d.Dia == comparacion);

        return new PanelDto
        {
            Fecha = hoy,
            Hoy = deHoy,
            SemanaPasada = pasado is null ? null : ADia(pasado, comparacion),
            VariacionSemanal = pasado is not null && pasado.Ingresos > 0
                ? Math.Round(100m * (deHoy.Ingresos - pasado.Ingresos) / pasado.Ingresos, 1)
                : null,
            Caja = await EstadoCajaAsync(ct),
            Alertas = await AlertasAsync(ct),
            ProximosEventos = await EventosProximosAsync(ct)
        };
    }

    /// <summary>
    /// Lo que exige atención hoy, de lo más urgente a lo menos.
    ///
    /// Se limita a lo accionable: una alerta que nadie puede resolver hoy
    /// solo entrena a la gente a ignorar el panel.
    /// </summary>
    private async Task<List<AlertaDto>> AlertasAsync(CancellationToken ct)
    {
        var alertas = new List<AlertaDto>();
        var hoy = DateOnly.FromDateTime(DateTime.Today);

        var lotes = await _db.LotesActivos.AsNoTracking()
            .Where(l => l.Alerta != "normal")
            .Select(l => new { l.Alerta, l.ValorRestante })
            .ToListAsync(ct);

        var vencidos = lotes.Where(l => l.Alerta == "vencido").ToList();
        if (vencidos.Count > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "lote_vencido",
                Urgencia = "alta",
                Cantidad = vencidos.Count,
                Mensaje = $"{vencidos.Count} lote(s) vencido(s) por " +
                          $"{vencidos.Sum(l => l.ValorRestante):N0}. Revísalos y dales de baja.",
                Ruta = "/inventario/lotes?alerta=vencido"
            });
        }

        var porVencer = lotes.Where(l => l.Alerta == "por vencer").ToList();
        if (porVencer.Count > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "lote_por_vencer",
                Urgencia = "media",
                Cantidad = porVencer.Count,
                Mensaje = $"{porVencer.Count} lote(s) vencen en menos de 3 días " +
                          $"({porVencer.Sum(l => l.ValorRestante):N0}). Conviene moverlos hoy.",
                Ruta = "/inventario/lotes?alerta=por-vencer"
            });
        }

        var rezagados = lotes.Count(l => l.Alerta == "resto por liquidar");
        if (rezagados > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "lote_rezagado",
                Urgencia = "baja",
                Cantidad = rezagados,
                Mensaje = $"{rezagados} lote(s) con restos sin liquidar.",
                Ruta = "/inventario/lotes?rezagados=true"
            });
        }

        var bajoMinimo = await _db.ProductosDisponibles.AsNoTracking()
            .CountAsync(p => p.Activo && p.Minimo > 0 && p.Disponible < p.Minimo, ct);

        if (bajoMinimo > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "bajo_minimo",
                Urgencia = "media",
                Cantidad = bajoMinimo,
                Mensaje = $"{bajoMinimo} producto(s) bajo el mínimo.",
                Ruta = "/inventario?bajoMinimo=true"
            });
        }

        var vencidosCobro = await _db.CotizacionesSaldo.AsNoTracking()
            .Where(c => c.Vencido > 0)
            .Select(c => c.Vencido)
            .ToListAsync(ct);

        if (vencidosCobro.Count > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "cobro_vencido",
                Urgencia = "alta",
                Cantidad = vencidosCobro.Count,
                Mensaje = $"{vencidosCobro.Count} evento(s) con pagos atrasados por " +
                          $"{vencidosCobro.Sum():N0}.",
                Ruta = "/cotizaciones/por-cobrar"
            });
        }

        // Eventos de los próximos 7 días cuya flor el stock no cubre. Es la
        // alerta más valiosa: todavía hay tiempo de comprar.
        var limite = hoy.AddDays(7);
        var sinStock = await (from i in _db.CotizacionItems.AsNoTracking()
                              join c in _db.Cotizaciones on i.CotizacionId equals c.Id
                              join d in _db.ProductosDisponibles on i.ProductoId equals d.Id
                              where c.Estado == EstadoCotizacion.aprobada
                                    && c.FechaEvento != null
                                    && c.FechaEvento >= hoy && c.FechaEvento <= limite
                                    && i.Cantidad > d.Disponible
                              select c.Id).Distinct().CountAsync(ct);

        if (sinStock > 0)
        {
            alertas.Add(new AlertaDto
            {
                Tipo = "evento_sin_stock",
                Urgencia = "alta",
                Cantidad = sinStock,
                Mensaje = $"{sinStock} evento(s) de esta semana necesitan flor que no hay. " +
                          "Todavía alcanzas a comprarla.",
                Ruta = "/cotizaciones/agenda"
            });
        }

        var orden = new Dictionary<string, int> { ["alta"] = 0, ["media"] = 1, ["baja"] = 2 };
        return alertas.OrderBy(a => orden[a.Urgencia]).ToList();
    }

    private async Task<EstadoCajaDto?> EstadoCajaAsync(CancellationToken ct)
    {
        var caja = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estado == EstadoCaja.abierta)
            .Select(c => new
            {
                c.Id,
                c.AbiertaEn,
                c.FondoInicial,
                Responsable = c.UsuarioApertura.Nombre
            })
            .FirstOrDefaultAsync(ct);

        if (caja is null) return null;

        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => v.CajaId == caja.Id && !v.Anulada)
            .Select(v => new { v.Total, v.MedioPago })
            .ToListAsync(ct);

        var efectivo = ventas.Where(v => v.MedioPago == MedioPago.efectivo).Sum(v => (long)v.Total);

        return new EstadoCajaDto
        {
            CajaId = caja.Id,
            Abierta = true,
            AbiertaEn = caja.AbiertaEn,
            Responsable = caja.Responsable,
            FondoInicial = caja.FondoInicial,
            EfectivoEsperado = caja.FondoInicial + efectivo,
            TotalVendido = ventas.Sum(v => (long)v.Total),
            Boletas = ventas.Count
        };
    }

    private async Task<List<EventoProximoDto>> EventosProximosAsync(CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var limite = hoy.AddDays(14);

        var eventos = await _db.CotizacionesSaldo.AsNoTracking()
            .Where(c => c.Estado == EstadoCotizacion.aprobada
                     && c.FechaEvento != null
                     && c.FechaEvento >= hoy && c.FechaEvento <= limite)
            .OrderBy(c => c.FechaEvento)
            .Take(10)
            .Select(c => new EventoProximoDto
            {
                CotizacionId = c.Id,
                Folio = c.Folio,
                Cliente = c.ClienteNombre,
                TipoEvento = c.TipoEvento,
                FechaEvento = c.FechaEvento!.Value,
                DiasParaEvento = c.DiasParaEvento ?? 0,
                Total = c.Total,
                Saldo = c.Saldo
            })
            .ToListAsync(ct);

        if (eventos.Count == 0) return eventos;

        var ids = eventos.Select(e => e.CotizacionId).ToList();

        var faltantes = await (from i in _db.CotizacionItems.AsNoTracking()
                               join d in _db.ProductosDisponibles on i.ProductoId equals d.Id
                               where ids.Contains(i.CotizacionId) && i.Cantidad > d.Disponible
                               group i by i.CotizacionId into g
                               select new { CotizacionId = g.Key, Faltantes = g.Count() })
                              .ToDictionaryAsync(x => x.CotizacionId, x => x.Faltantes, ct);

        foreach (var evento in eventos)
        {
            if (faltantes.TryGetValue(evento.CotizacionId, out var n))
                evento.ProductosFaltantes = n;
        }

        return eventos;
    }

    /* ==================================================================
       RESULTADO DEL PERÍODO
       ================================================================== */

    public async Task<ResultadoPeriodoDto> ResultadoAsync(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var (inicio, fin) = Rango(desde, hasta, 30);

        var serie = await _db.ResultadosDiarios.AsNoTracking()
            .Where(r => r.Dia >= inicio && r.Dia <= fin)
            .OrderBy(r => r.Dia)
            .ToListAsync(ct);

        var dias = serie.Select(d => ADia(d, d.Dia)).ToList();

        var ingresos = dias.Sum(d => d.Ingresos);
        var boletas = dias.Sum(d => d.Boletas);
        var costo = dias.Sum(d => d.CostoVendido);
        var mermas = dias.Sum(d => d.Mermas);

        var porDia = dias
            .GroupBy(d => (short)d.Dia.DayOfWeek)
            .Select(g => new PorDiaSemanaDto
            {
                Dia = g.Key,
                Nombre = NombresDia[g.Key],
                Boletas = g.Sum(d => d.Boletas),
                Ingresos = g.Sum(d => d.Ingresos),
                // Promedio por jornada, no por boleta: dice cuánto rinde
                // abrir ese día
                Promedio = g.Count() > 0 ? (int)(g.Sum(d => d.Ingresos) / g.Count()) : 0
            })
            .OrderByDescending(d => d.Ingresos)
            .ToList();

        var desdeDt = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaDt = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var medios = await _db.Ventas.AsNoTracking()
            .Where(v => !v.Anulada && v.CreadoEn >= desdeDt && v.CreadoEn < hastaDt)
            .GroupBy(v => v.MedioPago)
            .Select(g => new
            {
                Medio = g.Key,
                Boletas = g.LongCount(),
                Total = g.Sum(v => (long)v.Total)
            })
            .ToListAsync(ct);

        var totalMedios = medios.Sum(m => m.Total);

        return new ResultadoPeriodoDto
        {
            Desde = inicio,
            Hasta = fin,
            Dias = fin.DayNumber - inicio.DayNumber + 1,
            Boletas = boletas,
            Bruto = dias.Sum(d => d.Bruto),
            Descuentos = dias.Sum(d => d.Descuentos),
            Ingresos = ingresos,
            Neto = dias.Sum(d => d.Neto),
            Iva = dias.Sum(d => d.Iva),
            CostoVendido = costo,
            Mermas = mermas,
            UtilidadBruta = dias.Sum(d => d.UtilidadBruta),
            Resultado = dias.Sum(d => d.Resultado),
            MargenPorcentaje = ingresos > 0
                ? Math.Round(100m * (ingresos - costo) / ingresos, 1) : 0,
            MermaPorcentaje = ingresos > 0
                ? Math.Round(100m * mermas / ingresos, 2) : 0,
            TicketPromedio = boletas > 0 ? (int)(ingresos / boletas) : 0,
            BoletasPorDia = serie.Count > 0
                ? Math.Round((decimal)boletas / serie.Count, 1) : 0,
            Serie = dias,
            PorDiaSemana = porDia,
            PorMedioPago = medios.Select(m => new PorMedioPagoDto
            {
                MedioPago = m.Medio.ToString(),
                Boletas = m.Boletas,
                Total = m.Total,
                Porcentaje = totalMedios > 0
                    ? Math.Round(100m * m.Total / totalMedios, 1) : 0
            }).OrderByDescending(m => m.Total).ToList()
        };
    }

    /* ==================================================================
       PRODUCTOS
       ================================================================== */

    public async Task<RendimientoProductosDto> ProductosAsync(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var (inicio, fin) = Rango(desde, hasta, 30);

        var desdeDt = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaDt = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Lo vendido, por producto
        var vendido = await _db.VentaItems.AsNoTracking()
            .Where(i => !i.Venta.Anulada && i.ProductoId != null
                     && i.Venta.CreadoEn >= desdeDt && i.Venta.CreadoEn < hastaDt)
            .GroupBy(i => new
            {
                i.ProductoId,
                i.Producto!.Nombre,
                i.Producto.Emoji,
                Categoria = i.Producto.Categoria.Nombre,
                i.Producto.CategoriaId
            })
            .Select(g => new
            {
                g.Key.ProductoId,
                g.Key.Nombre,
                g.Key.Emoji,
                g.Key.Categoria,
                g.Key.CategoriaId,
                Unidades = g.Sum(x => x.Cantidad),
                Ingresos = g.Sum(x => (long)x.Subtotal),
                Apariciones = g.Count()
            })
            .ToListAsync(ct);

        // El costo real sale de venta_consumos: es lo que efectivamente
        // salió de los lotes, no el costo teórico del producto.
        var costos = await _db.VentaConsumos.AsNoTracking()
            .Where(c => !c.Venta.Anulada
                     && c.Venta.CreadoEn >= desdeDt && c.Venta.CreadoEn < hastaDt)
            .GroupBy(c => c.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Costo = g.Sum(x => (decimal)(x.CostoUnitario ?? 0) * x.Cantidad)
            })
            .ToDictionaryAsync(x => x.ProductoId, x => (long)Math.Round(x.Costo), ct);

        var mermado = await _db.Mermas.AsNoTracking()
            .Where(m => !m.Revertida && m.CreadoEn >= desdeDt && m.CreadoEn < hastaDt)
            .GroupBy(m => m.ProductoId)
            .Select(g => new
            {
                ProductoId = g.Key,
                Unidades = g.Sum(x => x.Cantidad - x.CantidadRecuperada)
            })
            .ToDictionaryAsync(x => x.ProductoId, x => x.Unidades, ct);

        var top = vendido.Select(v =>
        {
            costos.TryGetValue(v.ProductoId!.Value, out var costo);
            mermado.TryGetValue(v.ProductoId.Value, out var perdido);

            return new ProductoRendimientoDto
            {
                ProductoId = v.ProductoId.Value,
                Producto = v.Nombre,
                Emoji = v.Emoji,
                Categoria = v.Categoria,
                Unidades = v.Unidades,
                Ingresos = v.Ingresos,
                Costo = costo,
                Utilidad = v.Ingresos - costo,
                MargenPorcentaje = v.Ingresos > 0
                    ? Math.Round(100m * (v.Ingresos - costo) / v.Ingresos, 1) : 0,
                Mermado = perdido,
                Apariciones = v.Apariciones
            };
        })
        // Ordenado por utilidad, no por ingresos: lo que más se vende no
        // siempre es lo que más deja.
        .OrderByDescending(p => p.Utilidad)
        .ToList();

        var porCategoria = top
            .GroupBy(p => p.Categoria)
            .Select(g => new CategoriaRendimientoDto
            {
                Categoria = g.Key,
                Ingresos = g.Sum(p => p.Ingresos),
                Utilidad = g.Sum(p => p.Utilidad)
            })
            .OrderByDescending(c => c.Ingresos)
            .ToList();

        var totalIngresos = porCategoria.Sum(c => c.Ingresos);
        foreach (var cat in porCategoria)
        {
            cat.ParticipacionPorcentaje = totalIngresos > 0
                ? Math.Round(100m * cat.Ingresos / totalIngresos, 1) : 0;
        }

        return new RendimientoProductosDto
        {
            Desde = inicio,
            Hasta = fin,
            Top = top.Take(20).ToList(),
            SinMovimiento = await SinMovimientoAsync(desdeDt, ct),
            PorCategoria = porCategoria
        };
    }

    /// <summary>
    /// Lo que ocupa cámara sin moverse. Es donde está la plata dormida, y la
    /// pregunta que nadie se hace hasta que la flor ya se perdió.
    /// </summary>
    private async Task<List<ProductoSinMovimientoDto>> SinMovimientoAsync(
        DateTimeOffset desde, CancellationToken ct)
    {
        var conStock = await _db.ProductosDisponibles.AsNoTracking()
            .Where(p => p.Activo && p.Disponible > 0)
            .Select(p => new { p.Id, p.Nombre, p.Disponible })
            .ToListAsync(ct);

        if (conStock.Count == 0) return new List<ProductoSinMovimientoDto>();

        var ids = conStock.Select(p => p.Id).ToList();

        var ultimaVenta = await _db.VentaItems.AsNoTracking()
            .Where(i => i.ProductoId != null && ids.Contains(i.ProductoId.Value)
                     && !i.Venta.Anulada)
            .GroupBy(i => i.ProductoId!.Value)
            .Select(g => new { ProductoId = g.Key, Ultima = g.Max(x => x.Venta.CreadoEn) })
            .ToDictionaryAsync(x => x.ProductoId, x => x.Ultima, ct);

        var datos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Emoji, p.Costo })
            .ToDictionaryAsync(p => p.Id, ct);

        var ahora = DateTimeOffset.UtcNow;

        return conStock
            .Where(p => !ultimaVenta.ContainsKey(p.Id) || ultimaVenta[p.Id] < desde)
            .Select(p =>
            {
                ultimaVenta.TryGetValue(p.Id, out var ultima);
                datos.TryGetValue(p.Id, out var info);

                return new ProductoSinMovimientoDto
                {
                    ProductoId = p.Id,
                    Producto = p.Nombre,
                    Emoji = info?.Emoji ?? "🌿",
                    Stock = p.Disponible,
                    ValorInmovilizado = p.Disponible * (info?.Costo ?? 0),
                    UltimaVenta = ultima == default ? null : ultima,
                    DiasSinVender = ultima == default
                        ? null
                        : (int)(ahora - ultima).TotalDays
                };
            })
            .OrderByDescending(p => p.ValorInmovilizado)
            .Take(20)
            .ToList();
    }

    /* ==================================================================
       INVENTARIO
       ================================================================== */

    public async Task<ValorInventarioDto> InventarioAsync(CancellationToken ct = default)
    {
        var lotes = await _db.LotesActivos.AsNoTracking()
            .Select(l => new
            {
                l.Id,
                l.Codigo,
                l.ProductoId,
                l.Producto,
                l.Emoji,
                l.VarasDisponibles,
                l.CostoPorVara,
                l.ValorRestante,
                l.DiasEnCamara,
                l.DiasParaVencer,
                l.Alerta,
                l.EsRecuperado
            })
            .ToListAsync(ct);

        var porProducto = lotes
            .GroupBy(l => new { l.ProductoId, l.Producto, l.Emoji })
            .Select(g => new ValorPorProductoDto
            {
                ProductoId = g.Key.ProductoId,
                Producto = g.Key.Producto,
                Emoji = g.Key.Emoji,
                Varas = g.Sum(l => l.VarasDisponibles),
                CostoPromedio = g.Sum(l => l.VarasDisponibles) > 0
                    ? Math.Round(
                        g.Sum(l => l.CostoPorVara * l.VarasDisponibles) /
                        g.Sum(l => l.VarasDisponibles), 2)
                    : 0,
                Valor = g.Sum(l => (long)l.ValorRestante),
                Lotes = g.Count()
            })
            .OrderByDescending(p => p.Valor)
            .ToList();

        var criticos = lotes
            .Where(l => l.Alerta is "vencido" or "por vencer")
            .OrderBy(l => l.DiasParaVencer ?? int.MaxValue)
            .Take(15)
            .Select(l => new LoteCriticoDto
            {
                LoteId = l.Id,
                Codigo = l.Codigo,
                Producto = l.Producto,
                VarasDisponibles = l.VarasDisponibles,
                DiasEnCamara = l.DiasEnCamara,
                DiasParaVencer = l.DiasParaVencer,
                ValorRestante = l.ValorRestante,
                Alerta = l.Alerta
            })
            .ToList();

        return new ValorInventarioDto
        {
            Fecha = DateOnly.FromDateTime(DateTime.Today),
            ValorTotal = lotes.Sum(l => (long)l.ValorRestante),
            ValorSano = lotes.Where(l => l.Alerta == "normal").Sum(l => (long)l.ValorRestante),
            ValorPorVencer = lotes.Where(l => l.Alerta == "por vencer")
                .Sum(l => (long)l.ValorRestante),
            ValorVencido = lotes.Where(l => l.Alerta == "vencido")
                .Sum(l => (long)l.ValorRestante),
            ValorRecuperado = lotes.Where(l => l.EsRecuperado).Sum(l => (long)l.ValorRestante),
            LotesActivos = lotes.Count,
            VarasEnCamara = lotes.Sum(l => l.VarasDisponibles),
            PorProducto = porProducto,
            Criticos = criticos
        };
    }

    /* ==================================================================
       EQUIPO
       ================================================================== */

    public async Task<RendimientoEquipoDto> EquipoAsync(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var (inicio, fin) = Rango(desde, hasta, 30);

        var desdeDt = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaDt = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        var usuarios = await _db.Usuarios.AsNoTracking()
            .Where(u => u.Activo)
            .Select(u => new { u.Id, u.Nombre, u.Rol })
            .ToListAsync(ct);

        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => v.CreadoEn >= desdeDt && v.CreadoEn < hastaDt)
            .GroupBy(v => v.UsuarioId)
            .Select(g => new
            {
                UsuarioId = g.Key,
                Boletas = g.LongCount(v => !v.Anulada),
                Total = g.Sum(v => v.Anulada ? 0L : v.Total),
                Descuentos = g.Sum(v => v.Anulada ? 0L : v.DescuentoManual),
                Anuladas = g.Count(v => v.Anulada)
            })
            .ToDictionaryAsync(x => x.UsuarioId, ct);

        // Las diferencias de caja se toman de los turnos cerrados: es el dato
        // que más rinde de todos, porque un patrón de faltantes dice algo que
        // el total vendido no dice.
        var turnos = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estado == EstadoCaja.cerrada
                     && c.AbiertaEn >= desdeDt && c.AbiertaEn < hastaDt)
            .Select(c => new { c.AbiertaPor, c.Diferencia })
            .ToListAsync(ct);

        var vendedores = usuarios.Select(u =>
        {
            ventas.TryGetValue(u.Id, out var v);
            var mios = turnos.Where(t => t.AbiertaPor == u.Id).ToList();

            var boletas = v?.Boletas ?? 0;
            var total = v?.Total ?? 0;

            return new VendedorDto
            {
                UsuarioId = u.Id,
                Nombre = u.Nombre,
                Rol = u.Rol.ToString(),
                Turnos = mios.Count,
                Boletas = boletas,
                TotalVendido = total,
                TicketPromedio = boletas > 0 ? (int)(total / boletas) : 0,
                DescuentosOtorgados = v?.Descuentos ?? 0,
                BoletasAnuladas = v?.Anuladas ?? 0,
                DiferenciaCajaAcumulada = mios.Sum(t => t.Diferencia ?? 0),
                TurnosDescuadrados = mios.Count(t => (t.Diferencia ?? 0) != 0)
            };
        })
        .Where(v => v.Boletas > 0 || v.Turnos > 0)
        .OrderByDescending(v => v.TotalVendido)
        .ToList();

        return new RendimientoEquipoDto
        {
            Desde = inicio,
            Hasta = fin,
            Vendedores = vendedores
        };
    }

    /* ==================================================================
       CIERRE DE TURNO
       ================================================================== */

    /// <summary>
    /// Desglosa el turno separando la venta de mostrador de los abonos de
    /// eventos. Son plata distinta: una ya entregó flor, la otra es un
    /// compromiso pendiente, y el cierre debería distinguirlas.
    /// </summary>
    public async Task<DesgloseTurnoDto> TurnoAsync(int cajaId, CancellationToken ct = default)
    {
        var caja = await _db.Cajas.AsNoTracking()
            .Where(c => c.Id == cajaId)
            .Select(c => new
            {
                c.Id,
                c.AbiertaEn,
                c.CerradaEn,
                c.Estado,
                c.FondoInicial,
                c.EfectivoEsperado,
                c.EfectivoContado,
                c.Diferencia,
                Responsable = c.UsuarioApertura.Nombre
            })
            .FirstOrDefaultAsync(ct)
            ?? throw new NoEncontradoException("La caja");

        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => v.CajaId == cajaId && !v.Anulada)
            .Select(v => new
            {
                v.Id,
                v.Total,
                v.MedioPago,
                Lineas = v.Items.Select(i => new { i.EsServicio, i.Subtotal }).ToList()
            })
            .ToListAsync(ct);

        var ventaIds = ventas.Select(v => v.Id).ToList();

        // Los abonos se identifican por su registro en cotizacion_pagos, no
        // por el texto de la línea: el texto se puede editar, el vínculo no.
        var abonos = await _db.CotizacionPagos.AsNoTracking()
            .Where(p => !p.Anulado && p.VentaId != null && ventaIds.Contains(p.VentaId.Value))
            .Select(p => new AbonoTurnoDto
            {
                CotizacionId = p.CotizacionId,
                Folio = p.Cotizacion.Folio,
                Cliente = p.Cotizacion.ClienteNombre,
                Monto = p.Monto,
                MedioPago = p.MedioPago.ToString(),
                VentaFolio = p.Venta!.Folio
            })
            .ToListAsync(ct);

        var idsAbono = await _db.CotizacionPagos.AsNoTracking()
            .Where(p => !p.Anulado && p.VentaId != null && ventaIds.Contains(p.VentaId.Value))
            .Select(p => p.VentaId!.Value)
            .ToListAsync(ct);

        var deMostrador = ventas.Where(v => !idsAbono.Contains(v.Id)).ToList();

        var totalVendido = ventas.Sum(v => (long)v.Total);

        var porMedio = ventas
            .GroupBy(v => v.MedioPago)
            .Select(g => new PorMedioPagoDto
            {
                MedioPago = g.Key.ToString(),
                Boletas = g.LongCount(),
                Total = g.Sum(v => (long)v.Total),
                Porcentaje = totalVendido > 0
                    ? Math.Round(100m * g.Sum(v => (long)v.Total) / totalVendido, 1) : 0
            })
            .OrderByDescending(m => m.Total)
            .ToList();

        return new DesgloseTurnoDto
        {
            CajaId = caja.Id,
            AbiertaEn = caja.AbiertaEn,
            CerradaEn = caja.CerradaEn,
            Responsable = caja.Responsable,
            Estado = caja.Estado.ToString(),
            FondoInicial = caja.FondoInicial,
            TotalVendido = totalVendido,
            Boletas = ventas.Count,

            Mostrador = deMostrador
                .SelectMany(v => v.Lineas).Where(l => !l.EsServicio)
                .Sum(l => (long)l.Subtotal),
            AbonosEventos = abonos.Sum(a => (long)a.Monto),
            Servicios = deMostrador
                .SelectMany(v => v.Lineas).Where(l => l.EsServicio)
                .Sum(l => (long)l.Subtotal),

            PorMedioPago = porMedio,
            Abonos = abonos,

            EfectivoEsperado = caja.EfectivoEsperado
                ?? caja.FondoInicial + ventas
                    .Where(v => v.MedioPago == MedioPago.efectivo)
                    .Sum(v => (long)v.Total),
            EfectivoContado = caja.EfectivoContado,
            Diferencia = caja.Diferencia
        };
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    private static ResultadoDiaDto ADia(ResultadoDiario? r, DateOnly dia)
    {
        if (r is null) return new ResultadoDiaDto { Dia = dia };

        return new ResultadoDiaDto
        {
            Dia = r.Dia,
            Boletas = r.Boletas,
            Bruto = r.Bruto,
            Descuentos = r.Descuentos,
            Ingresos = r.Ingresos,
            Neto = r.Neto,
            Iva = r.Iva,
            CostoVendido = r.CostoVendido,
            Mermas = r.Mermas,
            UtilidadBruta = r.UtilidadBruta,
            Resultado = r.Resultado,
            MargenPorcentaje = r.Ingresos > 0
                ? Math.Round(100m * (r.Ingresos - r.CostoVendido) / r.Ingresos, 1) : 0,
            TicketPromedio = r.Boletas > 0 ? (int)(r.Ingresos / r.Boletas) : 0
        };
    }

    private static (DateOnly inicio, DateOnly fin) Rango(
        DateOnly? desde, DateOnly? hasta, int diasPorDefecto)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var inicio = desde ?? hoy.AddDays(-diasPorDefecto);
        var fin = hasta ?? hoy;

        if (fin < inicio)
            throw new ExcepcionNegocio("La fecha de término es anterior a la de inicio.");

        if (fin.DayNumber - inicio.DayNumber > MaximoDias)
            throw new ExcepcionNegocio($"El período no puede superar {MaximoDias} días.");

        return (inicio, fin);
    }
}