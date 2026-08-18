using Colibri.Api.Common;
using Colibri.Api.Common.Ajustes;
using Colibri.Api.Common.Inventario;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Common.Promociones;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Ventas.Dtos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Colibri.Api.Features.Ventas;

public class VentasService : IVentasService
{
    private readonly ColibriDbContext _db;
    private readonly ICajaService _caja;
    private readonly IAjustesService _ajustes;
    private readonly IConsumidorLotes _consumidor;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<VentasService> _log;

    public VentasService(
        ColibriDbContext db,
        ICajaService caja,
        IAjustesService ajustes,
        IConsumidorLotes consumidor,
        IUsuarioActual usuarioActual,
        ILogger<VentasService> log)
    {
        _db = db;
        _caja = caja;
        _ajustes = ajustes;
        _consumidor = consumidor;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       REGISTRO DE VENTA
       ================================================================== */

    /// <summary>
    /// Cobra la boleta.
    ///
    /// El servidor NO confía en nada de lo que manda la pantalla: los precios
    /// se leen de la base, la promoción se recalcula con sus reglas y el total
    /// se arma de cero. Si el cliente envía montos, se ignoran. Un punto de
    /// venta que acepta el total que le dictan es un punto de venta que se
    /// puede vaciar desde el navegador.
    /// </summary>
    public async Task<VentaDetalleDto> RegistrarAsync(
        RegistrarVentaRequest peticion, CancellationToken ct = default)
    {
        var cajaId = await _caja.CajaAbiertaIdAsync(ct);
        var usuarioId = _usuarioActual.IdRequerido();
        var medioPago = AMedioPago(peticion.MedioPago);

        var ajustesVenta = await _ajustes.VentaAsync(ct);
        var club = await _ajustes.ClubAsync(ct);

        // 1. Resolver las líneas contra la base
        var lineas = await ResolverLineasAsync(peticion.Items, ct);
        var bruto = lineas.Sum(l => l.Subtotal);

        if (bruto <= 0)
            throw new ExcepcionNegocio("El total de la venta debe ser mayor que cero.");

        // 2. Promoción: se recalcula, nunca se acepta un monto del cliente
        var (descuentoPromo, promocion) =
            await CalcularPromocionAsync(peticion.PromocionId, lineas, bruto, ct);

        // 3. Descuento manual: sobre el umbral exige autorización real
        var autorizadoPor = await ValidarDescuentoManualAsync(
            peticion, ajustesVenta, bruto, ct);

        // 4. Canje de puntos, validado contra el saldo real del cliente
        var (descuentoCanje, puntosCanjeados, cliente) =
            await CalcularCanjeAsync(peticion, club, bruto - descuentoPromo - peticion.DescuentoManual, ct);

        var descuentoTotal = descuentoPromo + peticion.DescuentoManual + descuentoCanje;

        // El abono previo NO lo manda la pantalla: se lee de la cotización.
        // Es plata que ya entró por otras boletas, y aceptar el monto que
        // dicte el cliente sería regalar el saldo del evento.
        var abonoPrevio = 0;

        if (peticion.CotizacionId.HasValue)
        {
            abonoPrevio = await _db.Cotizaciones.AsNoTracking()
                .Where(c => c.Id == peticion.CotizacionId.Value)
                .Select(c => c.Abono)
                .FirstOrDefaultAsync(ct);
        }

        if (descuentoTotal > bruto)
            throw new ExcepcionNegocio(
                $"Los descuentos ({descuentoTotal:N0}) superan el total de la boleta ({bruto:N0}).");

        var total = bruto - descuentoTotal - abonoPrevio;

        if (total < 0)
            throw new ExcepcionNegocio(
                $"El abono previo ({abonoPrevio:N0}) supera lo que queda por cobrar. " +
                "Revisa el presupuesto: puede que se haya reducido después de los pagos.");

        // 5. El desglose de IVA se calcula desde el total, que ya lo incluye
        var tasa = ajustesVenta.Iva / 100m;
        var neto = (int)Math.Round(total / (1 + tasa), MidpointRounding.AwayFromZero);
        var ivaMonto = total - neto;

        if (medioPago == MedioPago.efectivo && peticion.Recibido.HasValue &&
            peticion.Recibido.Value < total)
        {
            throw new ExcepcionNegocio(
                $"El efectivo recibido ({peticion.Recibido:N0}) no alcanza para cubrir " +
                $"el total ({total:N0}).");
        }

        var puntosGanados = club.Activo && cliente is not null && club.PuntosPorPeso > 0
            ? total / club.PuntosPorPeso
            : 0;

        // 6. Todo lo que sigue es una sola transacción: si falla el descuento
        //    de stock, la boleta no queda registrada.
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var (folio, atencion) = await SiguientesNumerosAsync(ct);

        var venta = new Venta
        {
            Folio = folio,
            NumeroAtencion = atencion,
            CajaId = cajaId,
            UsuarioId = usuarioId,
            ClienteId = cliente?.Id,
            PromocionId = promocion?.Id,
            CotizacionId = peticion.CotizacionId,

            Bruto = bruto,
            DescuentoPromo = descuentoPromo,
            DescuentoManual = peticion.DescuentoManual,
            DescuentoCanje = descuentoCanje,
            DescuentoTotal = descuentoTotal,
            AbonoPrevio = abonoPrevio,

            IvaTasa = ajustesVenta.Iva,
            Neto = neto,
            IvaMonto = ivaMonto,
            Total = total,

            MedioPago = medioPago,
            Recibido = medioPago == MedioPago.efectivo ? peticion.Recibido : null,
            Vuelto = medioPago == MedioPago.efectivo && peticion.Recibido.HasValue
                ? peticion.Recibido.Value - total
                : null,
            AutorizadoPor = autorizadoPor,

            PuntosGanados = puntosGanados,
            PuntosCanjeados = puntosCanjeados
        };

        _db.Ventas.Add(venta);
        await _db.SaveChangesAsync(ct);

        foreach (var linea in lineas)
        {
            _db.VentaItems.Add(new VentaItem
            {
                VentaId = venta.Id,
                ProductoId = linea.ProductoId,
                Nombre = linea.Nombre,
                PrecioUnitario = linea.Precio,
                Cantidad = linea.Cantidad,
                Subtotal = linea.Subtotal,
                EsServicio = linea.EsServicio
            });
        }
        await _db.SaveChangesAsync(ct);

        await DescontarInventarioAsync(venta, lineas, ct);
        await RegistrarPuntosAsync(venta, cliente, puntosGanados, puntosCanjeados, ct);

        if (promocion is not null)
        {
            promocion.Usos += 1;
            await _db.SaveChangesAsync(ct);
        }

        if (peticion.CotizacionId.HasValue)
            await MarcarCotizacionCobradaAsync(peticion.CotizacionId.Value, venta.Id, ct);

        await tx.CommitAsync(ct);

        _log.LogInformation(
            "Venta {Folio} por {Total} ({Medio}) registrada por {Autor}",
            venta.Folio, venta.Total, medioPago, _usuarioActual.Email);

        return await ObtenerAsync(venta.Id, ct);
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    public async Task<ResultadoPagina<VentaDto>> ListarAsync(
        VentaFiltro filtro, CancellationToken ct = default)
    {
        var consulta = Proyectar();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(v =>
                v.Folio.ToLower().Contains(q) ||
                v.NumeroAtencion.ToLower().Contains(q) ||
                (v.Cliente != null && v.Cliente.ToLower().Contains(q)));
        }

        if (filtro.CajaId.HasValue)
            consulta = consulta.Where(v => v.CajaId == filtro.CajaId.Value);

        if (filtro.ClienteId.HasValue)
            consulta = consulta.Where(v => v.ClienteId == filtro.ClienteId.Value);

        if (!string.IsNullOrWhiteSpace(filtro.MedioPago))
            consulta = consulta.Where(v => v.MedioPago == AMedioPago(filtro.MedioPago).ToString());

        if (filtro.Anulada.HasValue)
            consulta = consulta.Where(v => v.Anulada == filtro.Anulada.Value);

        if (filtro.Desde.HasValue)
        {
            var desde = new DateTimeOffset(
                filtro.Desde.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(v => v.Fecha >= desde);
        }

        if (filtro.Hasta.HasValue)
        {
            var hasta = new DateTimeOffset(
                filtro.Hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(v => v.Fecha < hasta);
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(v => v.Fecha).ThenByDescending(v => v.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<VentaDto>.Crear(items, total, filtro);
    }

    public async Task<VentaDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var b = await Proyectar().FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NoEncontradoException("La venta");

        var items = await _db.VentaItems.AsNoTracking()
            .Where(i => i.VentaId == id)
            .OrderBy(i => i.Id)
            .Select(i => new VentaItemDto
            {
                Id = i.Id,
                ProductoId = i.ProductoId,
                Nombre = i.Nombre,
                Emoji = i.Producto != null ? i.Producto.Emoji : null,
                PrecioUnitario = i.PrecioUnitario,
                Cantidad = i.Cantidad,
                Subtotal = i.Subtotal,
                EsServicio = i.EsServicio
            })
            .ToListAsync(ct);

        var consumos = await _db.VentaConsumos.AsNoTracking()
            .Where(c => c.VentaId == id)
            .OrderBy(c => c.ProductoId)
            .Select(c => new VentaConsumoDto
            {
                ProductoId = c.ProductoId,
                Producto = c.Producto.Nombre,
                Tipo = c.Tipo.ToString(),
                LoteCodigo = c.Lote != null ? c.Lote.Codigo : null,
                Cantidad = c.Cantidad,
                CostoUnitario = c.CostoUnitario
            })
            .ToListAsync(ct);

        var costo = (int)Math.Round(consumos.Sum(c => (c.CostoUnitario ?? 0) * c.Cantidad));

        return new VentaDetalleDto
        {
            Id = b.Id,
            Folio = b.Folio,
            NumeroAtencion = b.NumeroAtencion,
            Fecha = b.Fecha,
            CajaId = b.CajaId,
            Vendedor = b.Vendedor,
            ClienteId = b.ClienteId,
            Cliente = b.Cliente,
            Bruto = b.Bruto,
            DescuentoPromo = b.DescuentoPromo,
            DescuentoManual = b.DescuentoManual,
            DescuentoCanje = b.DescuentoCanje,
            DescuentoTotal = b.DescuentoTotal,
            IvaTasa = b.IvaTasa,
            Neto = b.Neto,
            IvaMonto = b.IvaMonto,
            Total = b.Total,
            MedioPago = b.MedioPago,
            Recibido = b.Recibido,
            Vuelto = b.Vuelto,
            AutorizadoPor = b.AutorizadoPor,
            Promocion = b.Promocion,
            PuntosGanados = b.PuntosGanados,
            PuntosCanjeados = b.PuntosCanjeados,
            Anulada = b.Anulada,
            MotivoAnulacion = b.MotivoAnulacion,
            AnuladaPor = b.AnuladaPor,
            AnuladaEn = b.AnuladaEn,
            Lineas = items.Count,
            Items = items,
            Consumos = consumos,
            CostoVendido = costo,
            UtilidadBruta = b.Total - costo
        };
    }

    public async Task<TicketDto> TicketAsync(int id, CancellationToken ct = default)
    {
        var venta = await ObtenerAsync(id, ct);
        var local = await _ajustes.LocalAsync(ct);
        var ticket = await _ajustes.TicketAsync(ct);

        int? saldo = venta.ClienteId.HasValue
            ? await _db.Clientes.AsNoTracking()
                .Where(c => c.Id == venta.ClienteId.Value)
                .Select(c => (int?)c.Puntos).FirstOrDefaultAsync(ct)
            : null;

        return new TicketDto
        {
            Folio = venta.Folio,
            NumeroAtencion = venta.NumeroAtencion,
            Fecha = venta.Fecha,
            Vendedor = venta.Vendedor,

            LocalNombre = local.Nombre,
            LocalRut = local.Rut,
            LocalDireccion = local.Direccion,
            LocalTelefono = local.Telefono,
            LocalInstagram = local.Instagram,

            Cliente = venta.Cliente,
            Items = venta.Items,

            Bruto = venta.Bruto,
            DescuentoTotal = venta.DescuentoTotal,
            Promocion = venta.Promocion,
            Neto = venta.Neto,
            IvaMonto = venta.IvaMonto,
            IvaTasa = venta.IvaTasa,
            Total = venta.Total,

            MedioPago = venta.MedioPago,
            Recibido = venta.Recibido,
            Vuelto = venta.Vuelto,

            MostrarPuntos = ticket.MostrarPuntos && venta.ClienteId.HasValue,
            PuntosGanados = venta.PuntosGanados,
            SaldoPuntos = saldo,

            Mensaje = ticket.Mensaje,
            Leyenda = ticket.Leyenda,
            Anulada = venta.Anulada
        };
    }

    /* ==================================================================
       ANULACIÓN
       ================================================================== */

    /// <summary>
    /// Anula la boleta y devuelve al inventario exactamente lo que sacó.
    ///
    /// Esto es posible porque la venta guardó su plan de consumo: qué salió,
    /// de qué lote y a qué costo. Recalcularlo hoy daría otro resultado,
    /// porque el stock y los lotes ya cambiaron.
    /// </summary>
    public async Task<VentaDto> AnularAsync(
        int id, AnularVentaRequest peticion, CancellationToken ct = default)
    {
        var venta = await _db.Ventas
            .Include(v => v.Caja)
            .FirstOrDefaultAsync(v => v.Id == id, ct)
            ?? throw new NoEncontradoException("La venta");

        if (venta.Anulada)
            throw new ExcepcionNegocio($"La boleta {venta.Folio} ya está anulada.");

        // Anular una boleta de un turno cerrado descuadra un arqueo que ya se
        // firmó. Se permite, porque a veces hay que hacerlo, pero queda
        // registrado con énfasis.
        if (venta.Caja.Estado == EstadoCaja.cerrada)
        {
            _log.LogWarning(
                "Anulación de {Folio} sobre la caja {Caja}, que ya está cerrada. " +
                "El arqueo de ese turno deja de cuadrar. Autor: {Autor}",
                venta.Folio, venta.CajaId, _usuarioActual.Email);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await DevolverInventarioAsync(venta, ct);
        await RevertirPuntosAsync(venta, ct);

        if (venta.PromocionId.HasValue)
        {
            var promo = await _db.Promociones
                .FirstOrDefaultAsync(p => p.Id == venta.PromocionId.Value, ct);
            if (promo is not null && promo.Usos > 0) promo.Usos -= 1;
        }

        venta.Anulada = true;
        venta.MotivoAnulacion = peticion.Motivo.Trim();
        venta.AnuladaPor = _usuarioActual.Id;
        venta.AnuladaEn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogWarning("Boleta {Folio} anulada por {Autor}: {Motivo}",
            venta.Folio, _usuarioActual.Email, venta.MotivoAnulacion);

        return await Proyectar().FirstAsync(v => v.Id == id, ct);
    }

    /* ==================================================================
       PROMOCIONES
       ================================================================== */

    public async Task<IReadOnlyList<PromocionAplicableDto>> PromocionesAplicablesAsync(
        List<LineaVentaRequest> items, CancellationToken ct = default)
    {
        var lineas = await ResolverLineasAsync(items, ct);
        var bruto = lineas.Sum(l => l.Subtotal);

        var candidatas = await _db.Promociones.AsNoTracking()
            .Where(p => p.Activa)
            .ToListAsync(ct);

        var aplicables = new List<PromocionAplicableDto>();

        foreach (var promo in candidatas)
        {
            if (!EstaVigente(promo)) continue;

            var descuento = CalcularDescuento(promo, lineas, bruto);
            if (descuento <= 0) continue;

            aplicables.Add(new PromocionAplicableDto
            {
                Id = promo.Id,
                Nombre = promo.Nombre,
                Descripcion = promo.Descripcion,
                Tipo = promo.Tipo.ToString(),
                Valor = promo.Valor,
                Alcance = promo.Alcance.ToString(),
                Descuento = descuento
            });
        }

        // La más conveniente primero: es la que el mesón va a querer aplicar
        return aplicables.OrderByDescending(p => p.Descuento).ToList();
    }

    /* ==================================================================
       INTERNO · resolución de líneas
       ================================================================== */

    private sealed class LineaResuelta
    {
        public int? ProductoId { get; init; }
        public string Nombre { get; init; } = string.Empty;
        public int Precio { get; init; }
        public int Cantidad { get; init; }
        public int Subtotal => Precio * Cantidad;
        public bool EsServicio { get; init; }
        public int? LoteId { get; init; }
        public IReadOnlyList<int> LotesAutorizados { get; init; } = Array.Empty<int>();
        public int CategoriaId { get; init; }
        public TipoProducto Tipo { get; init; }
        public bool ControlaLotes { get; init; }
        public int? Costo { get; init; }
        public int? StockListo { get; init; }
    }

    /// <summary>
    /// Convierte lo que llegó por HTTP en líneas con precio de la base.
    /// El precio que venga en la petición se ignora para productos: solo se
    /// respeta en servicios, que por definición no están en el catálogo.
    /// </summary>
    private async Task<List<LineaResuelta>> ResolverLineasAsync(
        List<LineaVentaRequest> items, CancellationToken ct)
    {
        if (items is null || items.Count == 0)
            throw new ExcepcionNegocio("La venta necesita al menos un producto.");

        var ids = items.Where(i => i.ProductoId.HasValue && !i.EsServicio)
                       .Select(i => i.ProductoId!.Value).Distinct().ToList();

        var productos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new
            {
                p.Id,
                p.Nombre,
                p.Precio,
                p.CategoriaId,
                p.Tipo,
                p.ControlaLotes,
                p.Costo,
                p.StockListo,
                p.Activo
            })
            .ToListAsync(ct);

        var lineas = new List<LineaResuelta>();

        foreach (var item in items)
        {
            if (item.Cantidad < 1)
                throw new ExcepcionNegocio("Hay una línea con cantidad menor a 1.");

            if (item.EsServicio || !item.ProductoId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(item.Nombre) || item.Precio is null)
                    throw new ExcepcionNegocio(
                        "Un servicio necesita nombre y precio (por ejemplo: Despacho, $5.000).");

                lineas.Add(new LineaResuelta
                {
                    Nombre = item.Nombre.Trim(),
                    Precio = item.Precio.Value,
                    Cantidad = item.Cantidad,
                    EsServicio = true
                });
                continue;
            }

            var producto = productos.FirstOrDefault(p => p.Id == item.ProductoId.Value)
                ?? throw new ExcepcionNegocio($"El producto {item.ProductoId} no existe.");

            if (!producto.Activo)
                throw new ExcepcionNegocio($"{producto.Nombre} está desactivado: no se puede vender.");

            var precio = producto.Precio;

            if (item.LoteId.HasValue)
            {
                var precioLote = await _db.Lotes.AsNoTracking()
                    .Where(l => l.Id == item.LoteId.Value && l.ProductoId == producto.Id)
                    .Select(l => l.PrecioUnitario)
                    .FirstOrDefaultAsync(ct);

                if (precioLote.HasValue) precio = precioLote.Value;
            }

            lineas.Add(new LineaResuelta
            {
                ProductoId = producto.Id,
                Nombre = producto.Nombre,
                Precio = precio,   // ← de la base o del lote, nunca de la pantalla
                Cantidad = item.Cantidad,
                LoteId = item.LoteId,
                LotesAutorizados = item.LotesAutorizados ?? new List<int>(),
                CategoriaId = producto.CategoriaId,
                Tipo = producto.Tipo,
                ControlaLotes = producto.ControlaLotes,
                Costo = producto.Costo,
                StockListo = producto.StockListo
            });
        }

        return lineas;
    }

    /* ==================================================================
       INTERNO · descuentos
       ================================================================== */

    private async Task<(int descuento, Promocion? promocion)> CalcularPromocionAsync(
        int? promocionId, List<LineaResuelta> lineas, int bruto, CancellationToken ct)
    {
        if (!promocionId.HasValue) return (0, null);

        var promo = await _db.Promociones.FirstOrDefaultAsync(p => p.Id == promocionId.Value, ct)
            ?? throw new ExcepcionNegocio("La promoción indicada no existe.");

        if (!promo.Activa)
            throw new ExcepcionNegocio($"La promoción «{promo.Nombre}» está desactivada.");

        if (!EstaVigente(promo))
            throw new ExcepcionNegocio($"La promoción «{promo.Nombre}» no está vigente hoy.");

        var descuento = CalcularDescuento(promo, lineas, bruto);

        if (descuento <= 0)
            throw new ExcepcionNegocio(
                $"La promoción «{promo.Nombre}» no aplica a esta boleta.");

        return (descuento, promo);
    }

    private static bool EstaVigente(Promocion promo)
       => ReglaPromocion.EstaVigente(promo, DateOnly.FromDateTime(DateTime.Today));


    /// <summary>
    /// Calcula el descuento sobre la base que corresponde al alcance.
    /// Se recalcula siempre: guardar un monto congelado haría que la boleta
    /// siguiera descontando lo mismo aunque el cliente quite la mitad del
    /// carrito.
    /// </summary>
    private static int CalcularDescuento(Promocion promo, List<LineaResuelta> lineas, int bruto)
        => ReglaPromocion.Calcular(
            promo,
            lineas.Select(l => new LineaPromocion(
                l.ProductoId, l.CategoriaId, l.Subtotal, l.EsServicio)),
            bruto);

    /// <summary>
    /// Verifica la autorización del descuento manual.
    ///
    /// En el front esto era una validación de mentira: la pantalla pedía
    /// confirmación y nadie comprobaba nada. Acá se verifican credenciales
    /// reales contra la base, porque los descuentos sin control son la forma
    /// más rentable de vaciar un punto de venta.
    /// </summary>
    private async Task<string?> ValidarDescuentoManualAsync(
        RegistrarVentaRequest peticion, AjustesVenta ajustes, int bruto, CancellationToken ct)
    {
        if (peticion.DescuentoManual <= 0) return null;

        if (peticion.DescuentoManual > bruto)
            throw new ExcepcionNegocio("El descuento no puede superar el total de la boleta.");

        // Quien es administradora se autoriza a sí misma: ya tiene el permiso
        if (_usuarioActual.EsAdmin) return _usuarioActual.Nombre;

        if (peticion.DescuentoManual <= ajustes.DescuentoSinAutorizacion)
            return null;

        if (peticion.Autorizacion is null)
            throw new ExcepcionNegocio(
                $"Un descuento de {peticion.DescuentoManual:N0} supera el máximo sin " +
                $"autorización ({ajustes.DescuentoSinAutorizacion:N0}). " +
                "Pide a una administradora que lo autorice.",
                StatusCodes.Status403Forbidden);

        var email = peticion.Autorizacion.Email.Trim().ToLowerInvariant();
        var autorizante = await _db.Usuarios.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Email.ToLower() == email, ct);

        // Mismo mensaje para cuenta inexistente, contraseña mala y rol
        // insuficiente: un mensaje distinto revelaría qué cuentas son admin.
        const string invalida = "La autorización no es válida.";

        if (autorizante is null ||
            !autorizante.Activo ||
            autorizante.Rol != RolUsuario.admin ||
            !BCrypt.Net.BCrypt.Verify(peticion.Autorizacion.Password, autorizante.PasswordHash))
        {
            _log.LogWarning(
                "Autorización de descuento rechazada. Solicitó {Solicitante}, indicó {Email}",
                _usuarioActual.Email, email);
            throw new ExcepcionNegocio(invalida, StatusCodes.Status403Forbidden);
        }

        _log.LogInformation(
            "Descuento de {Monto} autorizado por {Autorizante} a solicitud de {Solicitante}. Motivo: {Motivo}",
            peticion.DescuentoManual, autorizante.Email, _usuarioActual.Email,
            peticion.MotivoDescuento ?? "sin indicar");

        return autorizante.Nombre;
    }

    private async Task<(int descuento, int puntos, Cliente? cliente)> CalcularCanjeAsync(
        RegistrarVentaRequest peticion, AjustesClub club, int disponible, CancellationToken ct)
    {
        Cliente? cliente = null;

        if (peticion.ClienteId.HasValue)
        {
            cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == peticion.ClienteId.Value, ct)
                ?? throw new ExcepcionNegocio("El cliente indicado no existe.");

            if (!cliente.Activo)
                throw new ExcepcionNegocio($"La ficha de {cliente.Nombre} está desactivada.");
        }

        if (peticion.PuntosACanjear <= 0) return (0, 0, cliente);

        if (!club.Activo)
            throw new ExcepcionNegocio("El club de puntos está desactivado.");

        if (cliente is null)
            throw new ExcepcionNegocio("Para canjear puntos hay que identificar al cliente.");

        if (peticion.PuntosACanjear > cliente.Puntos)
            throw new ExcepcionNegocio(
                $"{cliente.Nombre} tiene {cliente.Puntos} puntos y se intentan canjear " +
                $"{peticion.PuntosACanjear}.");

        if (peticion.PuntosACanjear < club.CanjeMinimo)
            throw new ExcepcionNegocio($"El canje mínimo es de {club.CanjeMinimo} puntos.");

        var descuento = peticion.PuntosACanjear * club.ValorPunto;

        // Canjear más de lo que queda por pagar regalaría puntos: se ajusta a
        // lo que realmente alcanza a descontar.
        if (descuento > disponible)
        {
            var posibles = club.ValorPunto > 0 ? disponible / club.ValorPunto : 0;
            throw new ExcepcionNegocio(
                $"Con {peticion.PuntosACanjear} puntos se descontarían {descuento:N0}, " +
                $"más de lo que queda por pagar. Canjea hasta {posibles} puntos.");
        }

        return (descuento, peticion.PuntosACanjear, cliente);
    }

    /* ==================================================================
       INTERNO · inventario
       ================================================================== */

    /// <summary>
    /// Descuenta lo vendido y guarda el plan de consumo.
    ///
    /// Un ramo tiene dos caminos: si hay unidades armadas en cámara se
    /// descuentan; si no alcanzan, se arma al momento consumiendo tallos por
    /// FIFO. Ambos quedan en venta_consumos, y esa distinción es lo que
    /// permite que la anulación devuelva exactamente lo que salió.
    /// </summary>
    private async Task DescontarInventarioAsync(
        Venta venta, List<LineaResuelta> lineas, CancellationToken ct)
    {
        foreach (var linea in lineas)
        {
            if (linea.EsServicio || linea.ProductoId is null) continue;

            if(linea.Tipo == TipoProducto.simple)
            {
                await ConsumirSimpleAsync(
                    venta, linea.ProductoId.Value, linea.Cantidad,
                    linea.LoteId, linea.LotesAutorizados, ct);
                continue;
            }

            // --- Producto armado ---
            var producto = await _db.Productos.FirstAsync(p => p.Id == linea.ProductoId.Value, ct);
            var desdeListo = Math.Min(linea.Cantidad, producto.StockListo ?? 0);

            if (desdeListo > 0)
            {
                producto.StockListo -= desdeListo;

                // Costo teórico: los tallos de estas unidades se consumieron
                // al armarlas, no ahora. Se usa el costo de la receta más la
                // mano de obra para que el margen de la boleta tenga sentido.
                var costoTeorico = await _db.ProductoCostos.AsNoTracking()
                    .Where(c => c.ProductoId == producto.Id)
                    .Select(c => (decimal)c.CostoUnitario)
                    .FirstOrDefaultAsync(ct);

                _db.VentaConsumos.Add(new VentaConsumo
                {
                    VentaId = venta.Id,
                    ProductoId = producto.Id,
                    Tipo = TipoConsumo.listo,
                    Cantidad = desdeListo,
                    CostoUnitario = costoTeorico
                });

                _db.MovimientosInventario.Add(new MovimientoInventario
                {
                    ProductoId = producto.Id,
                    Tipo = TipoMovimiento.venta,
                    Cantidad = -desdeListo,
                    StockResultante = producto.StockListo,
                    Motivo = $"Venta {venta.Folio}",
                    UsuarioId = venta.UsuarioId,
                    ReferenciaTipo = "venta",
                    ReferenciaId = venta.Id
                });

                await _db.SaveChangesAsync(ct);
            }

            var porArmar = linea.Cantidad - desdeListo;
            if (porArmar <= 0) continue;

            var receta = await _db.Recetas.AsNoTracking()
                .Where(r => r.ProductoId == producto.Id)
                .Select(r => new { r.ComponenteId, r.Cantidad })
                .ToListAsync(ct);

            if (receta.Count == 0)
                throw new ExcepcionNegocio(
                    $"No quedan unidades armadas de {producto.Nombre} y no tiene receta " +
                    "para armarlo al momento.");

            foreach (var ingrediente in receta)
            {
                // Los lotes autorizados de la línea valen para sus
                // ingredientes: es el caso del ramo que se arma al momento
                // usando flor recuperada.
                await ConsumirSimpleAsync(
                    venta, ingrediente.ComponenteId, ingrediente.Cantidad * porArmar,
                    null, linea.LotesAutorizados, ct);
            }
        }
    }

    /// <summary>
    /// Descuenta un producto simple. Si controla lotes, el trabajo lo hace
    /// fn_consumir_lotes: aplica FIFO, respeta el lote escaneado y bloquea las
    /// filas, así dos cajas cobrando a la vez no se llevan la misma vara.
    /// </summary>
    private async Task ConsumirSimpleAsync(
        Venta venta, int productoId, int cantidad, int? lotePreferido,
        IReadOnlyList<int>? lotesAutorizados, CancellationToken ct)
    {
        var producto = await _db.Productos.FirstAsync(p => p.Id == productoId, ct);

        if (producto.ControlaLotes)
        {
            var consumidos = await _consumidor.ConsumirAsync(
                productoId, cantidad, Ubicacion.venta, $"Venta {venta.Folio}", venta.UsuarioId,
                "venta", venta.Id, TipoMovimiento.venta,
                lotePreferido, lotesAutorizados, ct);

            foreach (var fila in consumidos)
            {
                _db.VentaConsumos.Add(new VentaConsumo
                {
                    VentaId = venta.Id,
                    ProductoId = productoId,
                    LoteId = fila.LoteId,
                    Tipo = TipoConsumo.simple,
                    Cantidad = fila.Cantidad,
                    CostoUnitario = fila.CostoUnitario
                });
            }

            await _db.SaveChangesAsync(ct);
            return;
        }

        // Sin lotes: descuento directo
        if ((producto.Stock ?? 0) < cantidad)
            throw new ExcepcionNegocio(
                $"No alcanza el stock de {producto.Nombre}: se piden {cantidad} y hay {producto.Stock}.");

        producto.Stock -= cantidad;

        _db.VentaConsumos.Add(new VentaConsumo
        {
            VentaId = venta.Id,
            ProductoId = productoId,
            Tipo = TipoConsumo.simple,
            Cantidad = cantidad,
            CostoUnitario = producto.Costo
        });

        _db.MovimientosInventario.Add(new MovimientoInventario
        {
            ProductoId = productoId,
            Tipo = TipoMovimiento.venta,
            Cantidad = -cantidad,
            StockResultante = producto.Stock,
            Motivo = $"Venta {venta.Folio}",
            UsuarioId = venta.UsuarioId,
            ReferenciaTipo = "venta",
            ReferenciaId = venta.Id
        });

        await _db.SaveChangesAsync(ct);
    }

    private async Task DevolverInventarioAsync(Venta venta, CancellationToken ct)
    {
        var consumos = await _db.VentaConsumos.AsNoTracking()
            .Where(c => c.VentaId == venta.Id)
            .ToListAsync(ct);

        foreach (var consumo in consumos)
        {
            var producto = await _db.Productos.FirstAsync(p => p.Id == consumo.ProductoId, ct);

            if (consumo.Tipo == TipoConsumo.listo)
            {
                producto.StockListo = (producto.StockListo ?? 0) + consumo.Cantidad;

                _db.MovimientosInventario.Add(new MovimientoInventario
                {
                    ProductoId = producto.Id,
                    Tipo = TipoMovimiento.entrada,
                    Cantidad = consumo.Cantidad,
                    StockResultante = producto.StockListo,
                    Motivo = $"Anulación de {venta.Folio}",
                    UsuarioId = _usuarioActual.Id,
                    ReferenciaTipo = "anulacion",
                    ReferenciaId = venta.Id
                });

                await _db.SaveChangesAsync(ct);
                continue;
            }

            if (consumo.LoteId.HasValue)
            {
                // Las varas vuelven al lote del que salieron, no a uno nuevo:
                // conservan su costo, su procedencia y su vencimiento.
                var loteId = consumo.LoteId.Value;
                var cantidad = consumo.Cantidad;
                var motivo = $"Anulación de {venta.Folio}";
                var usuarioId = _usuarioActual.Id;

                await _db.Database.ExecuteSqlInterpolatedAsync(
                    $"SELECT fn_devolver_lote({loteId}, {cantidad}, {motivo}, {usuarioId})", ct);
                continue;
            }

            producto.Stock = (producto.Stock ?? 0) + consumo.Cantidad;

            _db.MovimientosInventario.Add(new MovimientoInventario
            {
                ProductoId = producto.Id,
                Tipo = TipoMovimiento.entrada,
                Cantidad = consumo.Cantidad,
                StockResultante = producto.Stock,
                Motivo = $"Anulación de {venta.Folio}",
                UsuarioId = _usuarioActual.Id,
                ReferenciaTipo = "anulacion",
                ReferenciaId = venta.Id
            });

            await _db.SaveChangesAsync(ct);
        }
    }

    /* ==================================================================
       INTERNO · puntos
       ================================================================== */

    private async Task RegistrarPuntosAsync(
        Venta venta, Cliente? cliente, int ganados, int canjeados, CancellationToken ct)
    {
        if (cliente is null || (ganados == 0 && canjeados == 0)) return;

        if (canjeados > 0)
        {
            cliente.Puntos -= canjeados;
            _db.PuntosMovimientos.Add(new PuntoMovimiento
            {
                ClienteId = cliente.Id,
                Cantidad = -canjeados,
                SaldoResultante = cliente.Puntos,
                Motivo = $"Canje en boleta {venta.Folio}",
                VentaId = venta.Id,
                UsuarioId = venta.UsuarioId
            });
        }

        if (ganados > 0)
        {
            cliente.Puntos += ganados;
            _db.PuntosMovimientos.Add(new PuntoMovimiento
            {
                ClienteId = cliente.Id,
                Cantidad = ganados,
                SaldoResultante = cliente.Puntos,
                Motivo = $"Compra {venta.Folio}",
                VentaId = venta.Id,
                UsuarioId = venta.UsuarioId
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    private async Task RevertirPuntosAsync(Venta venta, CancellationToken ct)
    {
        if (!venta.ClienteId.HasValue) return;
        if (venta.PuntosGanados == 0 && venta.PuntosCanjeados == 0) return;

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == venta.ClienteId.Value, ct);
        if (cliente is null) return;

        if (venta.PuntosGanados > 0)
        {
            // El cliente pudo haber gastado ya esos puntos. Se quita lo que
            // alcance: dejar el saldo en negativo lo rechazaría la base y
            // haría fallar toda la anulación.
            var aQuitar = Math.Min(venta.PuntosGanados, cliente.Puntos);

            if (aQuitar < venta.PuntosGanados)
            {
                _log.LogWarning(
                    "Al anular {Folio} solo se pudieron revertir {Quitados} de {Ganados} puntos: " +
                    "el cliente ya gastó el resto.",
                    venta.Folio, aQuitar, venta.PuntosGanados);
            }

            if (aQuitar > 0)
            {
                cliente.Puntos -= aQuitar;
                _db.PuntosMovimientos.Add(new PuntoMovimiento
                {
                    ClienteId = cliente.Id,
                    Cantidad = -aQuitar,
                    SaldoResultante = cliente.Puntos,
                    Motivo = $"Reversa por anulación de {venta.Folio}",
                    VentaId = venta.Id,
                    UsuarioId = _usuarioActual.Id
                });
            }
        }

        if (venta.PuntosCanjeados > 0)
        {
            cliente.Puntos += venta.PuntosCanjeados;
            _db.PuntosMovimientos.Add(new PuntoMovimiento
            {
                ClienteId = cliente.Id,
                Cantidad = venta.PuntosCanjeados,
                SaldoResultante = cliente.Puntos,
                Motivo = $"Devolución de canje por anulación de {venta.Folio}",
                VentaId = venta.Id,
                UsuarioId = _usuarioActual.Id
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    /* ==================================================================
       INTERNO · apoyo
       ================================================================== */

    private async Task MarcarCotizacionCobradaAsync(int cotizacionId, int ventaId, CancellationToken ct)
    {
        var cotizacion = await _db.Cotizaciones
            .FirstOrDefaultAsync(c => c.Id == cotizacionId, ct)
            ?? throw new ExcepcionNegocio("La cotización indicada no existe.");

        if (cotizacion.Estado == EstadoCotizacion.cobrada)
            throw new ExcepcionNegocio($"La cotización {cotizacion.Folio} ya fue cobrada.");

        cotizacion.Estado = EstadoCotizacion.cobrada;
        cotizacion.VentaId = ventaId;
        cotizacion.CobradaEn = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Folio y número de atención desde las secuencias de la base: dos cajas
    /// cobrando a la vez no pueden obtener el mismo número.
    /// </summary>
    private async Task<(string folio, string atencion)> SiguientesNumerosAsync(CancellationToken ct)
    {
        var conexion = _db.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
            await conexion.OpenAsync(ct);

        using var comando = conexion.CreateCommand();
        comando.CommandText =
            "SELECT nextval('seq_folio_venta'), nextval('seq_atencion')";
        if (_db.Database.CurrentTransaction is not null)
            comando.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();

        await using var lector = await comando.ExecuteReaderAsync(ct);
        await lector.ReadAsync(ct);

        var folio = lector.GetInt64(0);
        var atencion = lector.GetInt64(1);

        // El número de atención es el que se le canta al cliente en el mesón:
        // se cicla en 999 para que quepa en la pantalla y en el ticket.
        return ($"B-{folio}", $"A-{atencion % 1000:000}");
    }

    private IQueryable<VentaDto> Proyectar()
        => _db.Ventas.AsNoTracking().Select(v => new VentaDto
        {
            Id = v.Id,
            Folio = v.Folio,
            NumeroAtencion = v.NumeroAtencion,
            Fecha = v.CreadoEn,
            CajaId = v.CajaId,
            Vendedor = v.Usuario.Nombre,
            ClienteId = v.ClienteId,
            Cliente = v.Cliente != null ? v.Cliente.Nombre : null,
            Bruto = v.Bruto,
            DescuentoPromo = v.DescuentoPromo,
            DescuentoManual = v.DescuentoManual,
            DescuentoCanje = v.DescuentoCanje,
            DescuentoTotal = v.DescuentoTotal,
            IvaTasa = v.IvaTasa,
            Neto = v.Neto,
            IvaMonto = v.IvaMonto,
            Total = v.Total,
            MedioPago = v.MedioPago.ToString(),
            Recibido = v.Recibido,
            Vuelto = v.Vuelto,
            AutorizadoPor = v.AutorizadoPor,
            Promocion = v.Promocion != null ? v.Promocion.Nombre : null,
            PuntosGanados = v.PuntosGanados,
            PuntosCanjeados = v.PuntosCanjeados,
            Anulada = v.Anulada,
            MotivoAnulacion = v.MotivoAnulacion,
            AnuladaPor = v.UsuarioAnulacion != null ? v.UsuarioAnulacion.Nombre : null,
            AnuladaEn = v.AnuladaEn,
            Lineas = v.Items.Count
        });

    private static MedioPago AMedioPago(string valor)
        => Enum.TryParse<MedioPago>(valor?.Trim().ToLowerInvariant(), out var m)
            ? m
            : throw new ExcepcionNegocio(
                "Medio de pago no válido. Debe ser efectivo, debito, credito o transferencia.");
}