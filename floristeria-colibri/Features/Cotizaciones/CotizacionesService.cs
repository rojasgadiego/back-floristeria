using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Cotizaciones.Dtos;
using Colibri.Api.Features.Ventas;
using Colibri.Api.Features.Ventas.Dtos;

namespace Colibri.Api.Features.Cotizaciones;

public class CotizacionesService : ICotizacionesService
{
    private readonly ColibriDbContext _db;
    private readonly IVentasService _ventas;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<CotizacionesService> _log;

    public CotizacionesService(
        ColibriDbContext db,
        IVentasService ventas,
        IUsuarioActual usuarioActual,
        ILogger<CotizacionesService> log)
    {
        _db = db;
        _ventas = ventas;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    public async Task<ResultadoPagina<CotizacionDto>> ListarAsync(
        CotizacionFiltro filtro, CancellationToken ct = default)
    {
        var consulta = Proyectar();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(c =>
                c.Folio.ToLower().Contains(q) ||
                c.ClienteNombre.ToLower().Contains(q) ||
                c.TipoEvento.ToLower().Contains(q));
        }

        if (!string.IsNullOrWhiteSpace(filtro.Estado))
            consulta = consulta.Where(c => c.Estado == AEstado(filtro.Estado).ToString());

        if (filtro.ClienteId.HasValue)
            consulta = consulta.Where(c => c.ClienteId == filtro.ClienteId.Value);

        if (filtro.Desde.HasValue)
            consulta = consulta.Where(c => c.FechaEvento >= filtro.Desde.Value);

        if (filtro.Hasta.HasValue)
            consulta = consulta.Where(c => c.FechaEvento <= filtro.Hasta.Value);

        if (filtro.ProximosDias.HasValue)
        {
            var hoy = DateOnly.FromDateTime(DateTime.Today);
            var limite = hoy.AddDays(filtro.ProximosDias.Value);
            consulta = consulta.Where(c =>
                c.FechaEvento != null && c.FechaEvento >= hoy && c.FechaEvento <= limite);
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderBy(c => c.FechaEvento == null).ThenBy(c => c.FechaEvento)
            .ThenByDescending(c => c.Id)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        var completos = await CompletarAsync(items, ct);

        // El vencimiento depende de las cuotas: se filtra después de
        // calcularlo, no en SQL.
        if (filtro.SoloVencidas)
            completos = completos.Where(c => c.Vencido > 0).ToList();

        return ResultadoPagina<CotizacionDto>.Crear(completos, total, filtro);
    }

    public async Task<CotizacionDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var b = await Proyectar().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("La cotización");

        var cabecera = (await CompletarAsync(new[] { b }, ct)).First();

        var items = await (from i in _db.CotizacionItems.AsNoTracking()
                           where i.CotizacionId == id
                           orderby i.Id
                           select new LineaCotizacionDto
                           {
                               Id = i.Id,
                               ProductoId = i.ProductoId,
                               Nombre = i.Nombre,
                               Emoji = i.Producto != null ? i.Producto.Emoji : null,
                               Precio = i.Precio,
                               Cantidad = i.Cantidad,
                               Subtotal = i.Precio * i.Cantidad,
                               AMedida = i.AMedida
                           }).ToListAsync(ct);

        // Disponibilidad de cada línea con producto de catálogo
        var productoIds = items.Where(i => i.ProductoId.HasValue)
            .Select(i => i.ProductoId!.Value).Distinct().ToList();

        if (productoIds.Count > 0)
        {
            var disponibles = await _db.ProductosDisponibles.AsNoTracking()
                .Where(d => productoIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Disponible, ct);

            foreach (var item in items.Where(i => i.ProductoId.HasValue))
            {
                if (disponibles.TryGetValue(item.ProductoId!.Value, out var disp))
                    item.Disponible = disp;
            }
        }

        var pagos = await ConsultarPagos(id).OrderByDescending(p => p.Fecha).ToListAsync(ct);
        var cuotas = await ConsultarCuotas(id, cabecera.Abono, ct);
        var faltantes = await FaltantesAsync(id, ct);

        return new CotizacionDetalleDto
        {
            Id = cabecera.Id,
            Folio = cabecera.Folio,
            ClienteId = cabecera.ClienteId,
            ClienteNombre = cabecera.ClienteNombre,
            TipoEvento = cabecera.TipoEvento,
            FechaEvento = cabecera.FechaEvento,
            Contacto = cabecera.Contacto,
            Estado = cabecera.Estado,
            Traslado = cabecera.Traslado,
            Montaje = cabecera.Montaje,
            Total = cabecera.Total,
            Abono = cabecera.Abono,
            Saldo = cabecera.Saldo,
            PorcentajePagado = cabecera.PorcentajePagado,
            DiasParaEvento = cabecera.DiasParaEvento,
            ExigibleHoy = cabecera.ExigibleHoy,
            Vencido = cabecera.Vencido,
            ProximoVencimiento = cabecera.ProximoVencimiento,
            EstadoPago = cabecera.EstadoPago,
            Lineas = items.Count,
            Pagos = cabecera.Pagos,
            Notas = cabecera.Notas,
            CreadaPor = cabecera.CreadaPor,
            CreadoEn = cabecera.CreadoEn,
            VentaId = cabecera.VentaId,
            VentaFolio = cabecera.VentaFolio,

            Items = items,
            HistorialPagos = pagos,
            PlanCuotas = cuotas,
            Faltantes = faltantes,
            Resultado = await ResultadoAsync(id, ct)
        };
    }

    /// <summary>
    /// Eventos con saldo vencido. Es la lista de a quiénes llamar, y por eso
    /// va ordenada por lo más atrasado primero.
    /// </summary>
    public async Task<IReadOnlyList<CotizacionDto>> PorCobrarAsync(CancellationToken ct = default)
    {
        var base_ = await Proyectar()
            .Where(c => c.Estado == EstadoCotizacion.aprobada.ToString() ||
                        c.Estado == EstadoCotizacion.borrador.ToString())
            .ToListAsync(ct);

        var completos = await CompletarAsync(base_, ct);

        return completos
            .Where(c => c.Vencido > 0)
            .OrderByDescending(c => c.Vencido)
            .ToList();
    }

    public async Task<IReadOnlyList<CotizacionDto>> AgendaAsync(
        int dias, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var limite = hoy.AddDays(Math.Max(1, dias));

        var base_ = await Proyectar()
            .Where(c => c.Estado == EstadoCotizacion.aprobada.ToString()
                     && c.FechaEvento != null
                     && c.FechaEvento >= hoy && c.FechaEvento <= limite)
            .OrderBy(c => c.FechaEvento)
            .ToListAsync(ct);

        return await CompletarAsync(base_, ct);
    }

    /* ==================================================================
       ESCRITURA
       ================================================================== */

    public async Task<CotizacionDetalleDto> CrearAsync(
        GuardarCotizacionRequest peticion, CancellationToken ct = default)
    {
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var numero = await SiguienteFolioAsync(ct);

        var cotizacion = new Cotizacion
        {
            Folio = $"COT-{numero}",
            Estado = EstadoCotizacion.borrador,
            CreadaPor = _usuarioActual.Id
        };

        await AplicarAsync(cotizacion, peticion, ct);

        _db.Cotizaciones.Add(cotizacion);
        await _db.SaveChangesAsync(ct);

        await ReemplazarItemsAsync(cotizacion, peticion.Items, ct);
        await tx.CommitAsync(ct);

        _log.LogInformation("Cotización {Folio} creada para {Cliente} por {Autor}",
            cotizacion.Folio, cotizacion.ClienteNombre, _usuarioActual.Email);

        return await ObtenerAsync(cotizacion.Id, ct);
    }

    public async Task<CotizacionDetalleDto> ActualizarAsync(
        int id, GuardarCotizacionRequest peticion, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);

        if (cotizacion.Estado is EstadoCotizacion.cobrada or EstadoCotizacion.anulada)
            throw new ExcepcionNegocio(
                $"La cotización {cotizacion.Folio} está {cotizacion.Estado}: ya no se edita.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        await AplicarAsync(cotizacion, peticion, ct);
        await ReemplazarItemsAsync(cotizacion, peticion.Items, ct);

        // Bajar el total por debajo de lo ya abonado dejaría al cliente con
        // saldo a favor sin que nadie lo note.
        if (cotizacion.Total < cotizacion.Abono)
        {
            throw new ExcepcionNegocio(
                $"El nuevo total ({cotizacion.Total:N0}) es menor que lo ya abonado " +
                $"({cotizacion.Abono:N0}). Si el evento se redujo, anula primero los " +
                "pagos que correspondan.");
        }

        await tx.CommitAsync(ct);
        return await ObtenerAsync(id, ct);
    }

    public async Task<CotizacionDto> AprobarAsync(int id, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);

        if (cotizacion.Estado == EstadoCotizacion.aprobada)
            throw new ExcepcionNegocio($"La cotización {cotizacion.Folio} ya está aprobada.");

        if (cotizacion.Estado != EstadoCotizacion.borrador)
            throw new ExcepcionNegocio(
                $"Solo se aprueban los borradores. {cotizacion.Folio} está {cotizacion.Estado}.");

        if (!await _db.CotizacionItems.AnyAsync(i => i.CotizacionId == id, ct))
            throw new ExcepcionNegocio("El presupuesto no tiene líneas: no hay qué aprobar.");

        cotizacion.Estado = EstadoCotizacion.aprobada;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Cotización {Folio} aprobada por {Autor}",
            cotizacion.Folio, _usuarioActual.Email);

        return (await CompletarAsync(new[]
            { await Proyectar().FirstAsync(c => c.Id == id, ct) }, ct)).First();
    }

    /// <summary>
    /// Anula el presupuesto.
    ///
    /// NO toca las ventas de los abonos: esa plata entró en turnos que ya se
    /// cerraron y borrarla descuadraría arqueos firmados. Si hay que devolver
    /// el dinero, se anulan esas boletas una por una, con su motivo — y ahí
    /// sí el arqueo de ese día cambia, porque efectivamente salió plata.
    /// </summary>
    public async Task<CotizacionDto> AnularAsync(
        int id, AnularCotizacionRequest peticion, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);

        if (cotizacion.Estado == EstadoCotizacion.cobrada)
            throw new ExcepcionNegocio(
                $"La cotización {cotizacion.Folio} ya se cobró. Para revertirla, anula " +
                "la boleta final desde Ventas.");

        if (cotizacion.Estado == EstadoCotizacion.anulada)
            throw new ExcepcionNegocio("Esta cotización ya está anulada.");

        cotizacion.Estado = EstadoCotizacion.anulada;
        cotizacion.Notas = string.IsNullOrWhiteSpace(cotizacion.Notas)
            ? $"Anulada: {peticion.Motivo.Trim()}"
            : $"{cotizacion.Notas}\n\nAnulada: {peticion.Motivo.Trim()}";

        await _db.SaveChangesAsync(ct);

        if (cotizacion.Abono > 0)
        {
            _log.LogWarning(
                "Cotización {Folio} anulada con {Abono} ya abonados. Ese dinero sigue " +
                "registrado en sus boletas: queda como saldo a favor del cliente hasta " +
                "que se devuelva o se use. Autor: {Autor}",
                cotizacion.Folio, cotizacion.Abono, _usuarioActual.Email);
        }

        return (await CompletarAsync(new[]
            { await Proyectar().FirstAsync(c => c.Id == id, ct) }, ct)).First();
    }

    /* ==================================================================
       PAGOS
       ================================================================== */

    /// <summary>
    /// Registra un abono.
    ///
    /// Genera una venta real con una línea de servicio: entra al cajón, tiene
    /// folio y medio de pago, y el cierre de turno la ve. Sin eso, el arqueo
    /// del día del abono mostraría plata que el sistema no explica.
    /// </summary>
    public async Task<PagoDto> RegistrarPagoAsync(
        int id, RegistrarPagoRequest peticion, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);

        if (cotizacion.Estado is EstadoCotizacion.cobrada or EstadoCotizacion.anulada)
            throw new ExcepcionNegocio(
                $"La cotización {cotizacion.Folio} está {cotizacion.Estado}: no admite abonos.");

        var saldo = cotizacion.Total - cotizacion.Abono;

        if (peticion.Monto > saldo)
            throw new ExcepcionNegocio(
                $"El abono de {peticion.Monto:N0} supera el saldo pendiente de {saldo:N0}.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // La venta va SIN cotizacionId: ese campo marca la boleta final, la
        // que consume inventario y cierra el evento. Un abono no cierra nada.
        var venta = await _ventas.RegistrarAsync(new RegistrarVentaRequest
        {
            ClienteId = cotizacion.ClienteId,
            MedioPago = peticion.MedioPago,
            Recibido = peticion.Recibido,
            Items = new List<LineaVentaRequest>
            {
                new()
                {
                    EsServicio = true,
                    Nombre = $"Abono · {cotizacion.Folio} · {cotizacion.TipoEvento}",
                    Precio = peticion.Monto,
                    Cantidad = 1
                }
            }
        }, ct);

        var pago = new CotizacionPago
        {
            CotizacionId = id,
            VentaId = venta.Id,
            Monto = peticion.Monto,
            MedioPago = AMedioPago(peticion.MedioPago),
            UsuarioId = _usuarioActual.Id,
            Notas = Limpiar(peticion.Notas)
        };

        _db.CotizacionPagos.Add(pago);

        // El trigger sincroniza cotizaciones.abono al guardar
        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogInformation(
            "Abono de {Monto} ({Medio}) a {Folio} · boleta {Boleta} · registró {Autor}",
            peticion.Monto, pago.MedioPago, cotizacion.Folio, venta.Folio,
            _usuarioActual.Email);

        return await ConsultarPagos(id).FirstAsync(p => p.Id == pago.Id, ct);
    }

    /// <summary>
    /// Anula un abono y su boleta.
    ///
    /// Las dos cosas van juntas: si la plata se devuelve, la venta deja de
    /// existir y el arqueo de ese día cambia. Anular solo el pago dejaría
    /// dinero en la caja sin dueño.
    /// </summary>
    public async Task<PagoDto> AnularPagoAsync(
        int id, int pagoId, AnularPagoRequest peticion, CancellationToken ct = default)
    {
        var pago = await _db.CotizacionPagos
            .FirstOrDefaultAsync(p => p.Id == pagoId && p.CotizacionId == id, ct)
            ?? throw new NoEncontradoException("El pago");

        if (pago.Anulado)
            throw new ExcepcionNegocio("Este pago ya fue anulado.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        if (pago.VentaId.HasValue)
        {
            await _ventas.AnularAsync(pago.VentaId.Value, new AnularVentaRequest
            {
                Motivo = $"Anulación de abono · {peticion.Motivo.Trim()}"
            }, ct);
        }

        pago.Anulado = true;
        pago.AnuladoEn = DateTimeOffset.UtcNow;
        pago.MotivoAnulacion = peticion.Motivo.Trim();

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogWarning("Abono #{Id} de {Monto} anulado por {Autor}: {Motivo}",
            pago.Id, pago.Monto, _usuarioActual.Email, peticion.Motivo);

        return await ConsultarPagos(id).FirstAsync(p => p.Id == pagoId, ct);
    }

    /* ==================================================================
       CUOTAS
       ================================================================== */

    public async Task<IReadOnlyList<CuotaDto>> GuardarCuotasAsync(
        int id, GuardarCuotasRequest peticion, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);
        var saldo = cotizacion.Total - cotizacion.Abono;

        if (peticion.Cuotas.Count == 0)
        {
            // Sin plan, el saldo completo vence el día del evento: es lo que
            // se entiende cuando nadie pacta nada.
            var previas = await _db.CotizacionCuotas.Where(q => q.CotizacionId == id)
                .ToListAsync(ct);
            _db.CotizacionCuotas.RemoveRange(previas);
            await _db.SaveChangesAsync(ct);
            return Array.Empty<CuotaDto>();
        }

        var suma = peticion.Cuotas.Sum(c => c.Monto);

        // Un plan que no cubre el saldo significa que el último pago va a ser
        // una sorpresa para el cliente y para quien cobra.
        if (suma != saldo)
        {
            throw new ExcepcionNegocio(
                $"Las cuotas suman {suma:N0} y el saldo pendiente es {saldo:N0}. " +
                $"Ajusta el plan por {(suma < saldo ? "los" : "el exceso de")} " +
                $"{Math.Abs(suma - saldo):N0} de diferencia.");
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        var actuales = await _db.CotizacionCuotas.Where(q => q.CotizacionId == id)
            .ToListAsync(ct);
        _db.CotizacionCuotas.RemoveRange(actuales);
        await _db.SaveChangesAsync(ct);

        var numero = 1;
        foreach (var cuota in peticion.Cuotas.OrderBy(c => c.Vence))
        {
            _db.CotizacionCuotas.Add(new CotizacionCuota
            {
                CotizacionId = id,
                Numero = numero++,
                Monto = cuota.Monto,
                Vence = cuota.Vence,
                Notas = Limpiar(cuota.Notas)
            });
        }

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        return await ConsultarCuotas(id, cotizacion.Abono, ct);
    }

    /// <summary>
    /// Genera el plan repartiendo el saldo en cuotas iguales.
    ///
    /// La diferencia por redondeo va en la primera: es mejor cobrar el peso
    /// de más al principio que descubrirlo al final.
    /// </summary>
    public async Task<IReadOnlyList<CuotaDto>> GenerarCuotasAsync(
        int id, GenerarCuotasRequest peticion, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);
        var saldo = cotizacion.Total - cotizacion.Abono;

        if (saldo <= 0)
            throw new ExcepcionNegocio("No hay saldo pendiente: nada que repartir en cuotas.");

        if (peticion.Cantidad > saldo)
            throw new ExcepcionNegocio(
                $"No se puede repartir {saldo:N0} en {peticion.Cantidad} cuotas.");

        var primera = peticion.PrimerVencimiento
            ?? DateOnly.FromDateTime(DateTime.Today).AddDays(peticion.CadaDias);

        var basica = saldo / peticion.Cantidad;
        var resto = saldo - basica * peticion.Cantidad;

        var cuotas = new List<CuotaRequest>();
        for (var i = 0; i < peticion.Cantidad; i++)
        {
            cuotas.Add(new CuotaRequest
            {
                Monto = i == 0 ? basica + resto : basica,
                Vence = primera.AddDays(i * peticion.CadaDias)
            });
        }

        return await GuardarCuotasAsync(id, new GuardarCuotasRequest { Cuotas = cuotas }, ct);
    }

    /* ==================================================================
       COBRO
       ================================================================== */

    /// <summary>
    /// Arma las líneas sugeridas para la boleta final.
    ///
    /// Es una sugerencia editable a propósito: si el arco quedó chico y se
    /// usaron 72 rosas en vez de 60, hay que cobrar y descontar lo que
    /// realmente salió. Cobrar a ciegas lo cotizado dejaría el inventario
    /// diciendo que salieron 60.
    /// </summary>
    public async Task<PreparacionCobroDto> PrepararCobroAsync(
        int id, CancellationToken ct = default)
    {
        var cotizacion = await BuscarAsync(id, ct);

        if (cotizacion.Estado == EstadoCotizacion.cobrada)
            throw new ExcepcionNegocio($"La cotización {cotizacion.Folio} ya fue cobrada.");

        if (cotizacion.Estado == EstadoCotizacion.anulada)
            throw new ExcepcionNegocio($"La cotización {cotizacion.Folio} está anulada.");

        var items = await _db.CotizacionItems.AsNoTracking()
            .Where(i => i.CotizacionId == id)
            .OrderBy(i => i.Id)
            .Select(i => new { i.ProductoId, i.Nombre, i.Precio, i.Cantidad, i.AMedida })
            .ToListAsync(ct);

        if (items.Count == 0)
            throw new ExcepcionNegocio("El presupuesto no tiene líneas: no hay qué cobrar.");

        var productoIds = items.Where(i => i.ProductoId.HasValue)
            .Select(i => i.ProductoId!.Value).Distinct().ToList();

        var disponibles = productoIds.Count == 0
            ? new Dictionary<int, int>()
            : await _db.ProductosDisponibles.AsNoTracking()
                .Where(d => productoIds.Contains(d.Id))
                .ToDictionaryAsync(d => d.Id, d => d.Disponible, ct);

        var lineas = new List<LineaCobroDto>();
        var advertencias = new List<string>();

        foreach (var item in items)
        {
            int? disponible = null;

            if (item.ProductoId.HasValue && disponibles.TryGetValue(item.ProductoId.Value, out var d))
            {
                disponible = d;

                if (d < item.Cantidad)
                {
                    advertencias.Add(
                        $"{item.Nombre}: el presupuesto pide {item.Cantidad} y hay {d} " +
                        "disponibles. Recibe una compra o ajusta la cantidad antes de cobrar.");
                }
            }

            lineas.Add(new LineaCobroDto
            {
                ProductoId = item.AMedida ? null : item.ProductoId,
                Nombre = item.Nombre,
                Precio = item.Precio,
                Cantidad = item.Cantidad,
                // Las líneas a medida no tocan inventario: un arco floral no
                // está en el catálogo
                EsServicio = item.AMedida || item.ProductoId is null,
                Disponible = disponible
            });
        }

        if (cotizacion.Traslado > 0)
        {
            lineas.Add(new LineaCobroDto
            {
                Nombre = "Traslado",
                Precio = cotizacion.Traslado,
                Cantidad = 1,
                EsServicio = true
            });
        }

        if (cotizacion.Montaje > 0)
        {
            lineas.Add(new LineaCobroDto
            {
                Nombre = "Montaje",
                Precio = cotizacion.Montaje,
                Cantidad = 1,
                EsServicio = true
            });
        }

        var bruto = lineas.Sum(l => l.Precio * l.Cantidad);

        if (bruto != cotizacion.Total)
        {
            advertencias.Add(
                $"Las líneas suman {bruto:N0} y el presupuesto dice {cotizacion.Total:N0}. " +
                "Revisa antes de cobrar.");
        }

        return new PreparacionCobroDto
        {
            CotizacionId = id,
            Folio = cotizacion.Folio,
            ClienteNombre = cotizacion.ClienteNombre,
            ClienteId = cotizacion.ClienteId,
            TotalCotizado = cotizacion.Total,
            AbonoPrevio = cotizacion.Abono,
            SaldoACobrar = Math.Max(0, bruto - cotizacion.Abono),
            Lineas = lineas,
            Advertencias = advertencias
        };
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    private async Task AplicarAsync(
        Cotizacion cotizacion, GuardarCotizacionRequest peticion, CancellationToken ct)
    {
        if (peticion.ClienteId.HasValue &&
            !await _db.Clientes.AnyAsync(c => c.Id == peticion.ClienteId.Value, ct))
        {
            throw new ExcepcionNegocio("El cliente indicado no existe.");
        }

        var hoy = DateOnly.FromDateTime(DateTime.Today);

        // Un evento en el pasado casi siempre es un error de tipeo. Se avisa
        // pero no se bloquea: a veces se registra algo ya ocurrido.
        if (peticion.FechaEvento.HasValue && peticion.FechaEvento.Value < hoy)
        {
            _log.LogWarning("Cotización con fecha de evento pasada: {Fecha}",
                peticion.FechaEvento.Value);
        }

        cotizacion.ClienteId = peticion.ClienteId;
        cotizacion.ClienteNombre = peticion.ClienteNombre.Trim();
        cotizacion.TipoEvento = peticion.TipoEvento.Trim();
        cotizacion.FechaEvento = peticion.FechaEvento;
        cotizacion.Contacto = Limpiar(peticion.Contacto);
        cotizacion.Traslado = peticion.Traslado;
        cotizacion.Montaje = peticion.Montaje;
        cotizacion.Notas = Limpiar(peticion.Notas);
    }

    /// <summary>
    /// Reemplaza las líneas y recalcula el total.
    /// El precio de un producto de catálogo sale de la base, no de la
    /// pantalla: un presupuesto con precios inventados es una promesa que
    /// después no se puede cumplir.
    /// </summary>
    private async Task ReemplazarItemsAsync(
        Cotizacion cotizacion, List<LineaCotizacionRequest> lineas, CancellationToken ct)
    {
        if (lineas.Count == 0)
            throw new ExcepcionNegocio("El presupuesto necesita al menos una línea.");

        var ids = lineas.Where(l => l.ProductoId.HasValue && !l.AMedida)
            .Select(l => l.ProductoId!.Value).Distinct().ToList();

        var productos = await _db.Productos.AsNoTracking()
            .Where(p => ids.Contains(p.Id))
            .Select(p => new { p.Id, p.Nombre, p.Precio, p.Activo })
            .ToListAsync(ct);

        var actuales = await _db.CotizacionItems
            .Where(i => i.CotizacionId == cotizacion.Id).ToListAsync(ct);
        _db.CotizacionItems.RemoveRange(actuales);
        await _db.SaveChangesAsync(ct);

        var total = 0;

        foreach (var linea in lineas)
        {
            if (linea.AMedida || !linea.ProductoId.HasValue)
            {
                if (string.IsNullOrWhiteSpace(linea.Nombre) || linea.Precio is null)
                    throw new ExcepcionNegocio(
                        "Una línea a medida necesita nombre y precio " +
                        "(por ejemplo: Arco floral, $180.000).");

                var subtotal = linea.Precio.Value * linea.Cantidad;
                total += subtotal;

                _db.CotizacionItems.Add(new CotizacionItem
                {
                    CotizacionId = cotizacion.Id,
                    ProductoId = null,
                    Nombre = linea.Nombre.Trim(),
                    Precio = linea.Precio.Value,
                    Cantidad = linea.Cantidad,
                    AMedida = true
                });
                continue;
            }

            var producto = productos.FirstOrDefault(p => p.Id == linea.ProductoId.Value)
                ?? throw new ExcepcionNegocio($"El producto {linea.ProductoId} no existe.");

            if (!producto.Activo)
                throw new ExcepcionNegocio($"{producto.Nombre} está desactivado.");

            var sub = producto.Precio * linea.Cantidad;
            total += sub;

            _db.CotizacionItems.Add(new CotizacionItem
            {
                CotizacionId = cotizacion.Id,
                ProductoId = producto.Id,
                Nombre = producto.Nombre,
                Precio = producto.Precio,   // ← del catálogo, no de la pantalla
                Cantidad = linea.Cantidad,
                AMedida = false
            });
        }

        cotizacion.Total = total + cotizacion.Traslado + cotizacion.Montaje;
        await _db.SaveChangesAsync(ct);
    }

    private async Task<List<CotizacionDto>> CompletarAsync(
        IReadOnlyCollection<CotizacionDto> items, CancellationToken ct)
    {
        if (items.Count == 0) return new List<CotizacionDto>();

        var ids = items.Select(c => c.Id).ToList();

        var saldos = await _db.CotizacionesSaldo.AsNoTracking()
            .Where(s => ids.Contains(s.Id))
            .ToDictionaryAsync(s => s.Id, ct);

        // La cuota más antigua sin cubrir: es la fecha desde la que se debe
        var cuotas = await _db.CotizacionCuotas.AsNoTracking()
            .Where(q => ids.Contains(q.CotizacionId))
            .OrderBy(q => q.Vence)
            .Select(q => new { q.CotizacionId, q.Monto, q.Vence })
            .ToListAsync(ct);

        foreach (var item in items)
        {
            if (!saldos.TryGetValue(item.Id, out var s))
            {
                // Las cobradas y anuladas no están en la vista
                item.Saldo = item.Total - item.Abono;
                item.PorcentajePagado = item.Total > 0
                    ? Math.Round(100m * item.Abono / item.Total, 1) : 0;
                item.EstadoPago = item.Estado == nameof(EstadoCotizacion.cobrada)
                    ? "cobrada" : "anulada";
                continue;
            }

            item.Saldo = s.Saldo;
            item.PorcentajePagado = s.PorcentajePagado;
            item.DiasParaEvento = s.DiasParaEvento;
            item.ExigibleHoy = s.ExigibleHoy;
            item.Vencido = s.Vencido;
            item.ProximoVencimiento = s.ProximoVencimiento;
            item.Pagos = (int)s.Pagos;

            item.EstadoPago = TextoEstadoPago(item, cuotas
                .Where(q => q.CotizacionId == item.Id)
                .Select(q => (q.Monto, q.Vence))
                .ToList());
        }

        return items.ToList();
    }

    /// <summary>
    /// Resume el estado de cobro en una frase. Es lo que se lee de un vistazo
    /// en la lista, y lo que decide a quién llamar.
    /// </summary>
    private static string TextoEstadoPago(
        CotizacionDto c, List<(int Monto, DateOnly Vence)> cuotas)
    {
        if (c.Saldo <= 0) return "pagada";

        if (c.Vencido <= 0)
        {
            if (c.ProximoVencimiento.HasValue)
                return $"al día · próxima cuota el {c.ProximoVencimiento.Value:dd-MM}";

            return c.FechaEvento.HasValue
                ? $"al día · saldo al {c.FechaEvento.Value:dd-MM}"
                : "al día";
        }

        // Desde cuándo se debe: la primera cuota que lo abonado no alcanza a
        // cubrir, acumulando de la más antigua a la más nueva.
        var acumulado = 0;
        DateOnly? desde = null;

        foreach (var cuota in cuotas.OrderBy(q => q.Vence))
        {
            acumulado += cuota.Monto;
            if (acumulado > c.Abono) { desde = cuota.Vence; break; }
        }

        return desde.HasValue
            ? $"debe {c.Vencido:N0} desde el {desde.Value:dd-MM}"
            : $"debe {c.Vencido:N0}";
    }

    private async Task<List<CuotaDto>> ConsultarCuotas(
        int id, int abonado, CancellationToken ct)
    {
        var cuotas = await _db.CotizacionCuotas.AsNoTracking()
            .Where(q => q.CotizacionId == id)
            .OrderBy(q => q.Numero)
            .ToListAsync(ct);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var acumulado = 0;

        return cuotas.Select(q =>
        {
            acumulado += q.Monto;

            return new CuotaDto
            {
                Id = q.Id,
                Numero = q.Numero,
                Monto = q.Monto,
                Vence = q.Vence,
                Notas = q.Notas,
                DiasParaVencer = q.Vence.DayNumber - hoy.DayNumber,
                // Cubierta si lo abonado alcanza para esta y todas las
                // anteriores. No se marca una por una: en un acuerdo de
                // palabra nadie paga los montos exactos.
                Cubierta = abonado >= acumulado
            };
        }).ToList();
    }

    private async Task<List<FaltanteEventoDto>> FaltantesAsync(int id, CancellationToken ct)
        => await (from i in _db.CotizacionItems.AsNoTracking()
                  join d in _db.ProductosDisponibles on i.ProductoId equals d.Id
                  join p in _db.Productos on i.ProductoId equals p.Id
                  where i.CotizacionId == id && i.ProductoId != null
                        && i.Cantidad > d.Disponible
                  select new FaltanteEventoDto
                  {
                      ProductoId = d.Id,
                      Producto = d.Nombre,
                      Emoji = p.Emoji,
                      Comprometido = i.Cantidad,
                      Disponible = d.Disponible,
                      Faltante = i.Cantidad - d.Disponible
                  }).ToListAsync(ct);

    /// <summary>
    /// Junta las dos boletas del evento —los abonos y el cobro final— porque
    /// ninguna de las dos, mirada sola, dice la verdad: la del abono se ve
    /// como puro margen, y la final carga con todo el costo de la flor.
    /// </summary>
    private async Task<ResultadoEventoDto?> ResultadoAsync(int id, CancellationToken ct)
    {
        var cotizacion = await _db.Cotizaciones.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Estado, c.VentaId })
            .FirstAsync(ct);

        if (cotizacion.Estado != EstadoCotizacion.cobrada) return null;

        var ventaIds = await _db.CotizacionPagos.AsNoTracking()
            .Where(p => p.CotizacionId == id && !p.Anulado && p.VentaId != null)
            .Select(p => p.VentaId!.Value)
            .ToListAsync(ct);

        if (cotizacion.VentaId.HasValue) ventaIds.Add(cotizacion.VentaId.Value);

        if (ventaIds.Count == 0) return null;

        var cobrado = await _db.Ventas.AsNoTracking()
            .Where(v => ventaIds.Contains(v.Id) && !v.Anulada)
            .SumAsync(v => (int?)v.Total, ct) ?? 0;

        var costo = await _db.VentaConsumos.AsNoTracking()
            .Where(c => ventaIds.Contains(c.VentaId))
            .SumAsync(c => (decimal?)(c.CostoUnitario ?? 0) * c.Cantidad, ct) ?? 0;

        var costoEntero = (int)Math.Round(costo);

        return new ResultadoEventoDto
        {
            Cobrado = cobrado,
            CostoFlor = costoEntero,
            Margen = cobrado - costoEntero,
            MargenPorcentaje = cobrado > 0
                ? Math.Round(100m * (cobrado - costoEntero) / cobrado, 1) : 0,
            Boletas = ventaIds.Count
        };
    }

    private IQueryable<PagoDto> ConsultarPagos(int cotizacionId)
        => _db.CotizacionPagos.AsNoTracking()
            .Where(p => p.CotizacionId == cotizacionId)
            .Select(p => new PagoDto
            {
                Id = p.Id,
                Fecha = p.Fecha,
                Monto = p.Monto,
                MedioPago = p.MedioPago.ToString(),
                VentaId = p.VentaId,
                VentaFolio = p.Venta != null ? p.Venta.Folio : null,
                Usuario = p.Usuario != null ? p.Usuario.Nombre : null,
                Notas = p.Notas,
                Anulado = p.Anulado,
                MotivoAnulacion = p.MotivoAnulacion
            });

    private IQueryable<CotizacionDto> Proyectar()
        => _db.Cotizaciones.AsNoTracking().Select(c => new CotizacionDto
        {
            Id = c.Id,
            Folio = c.Folio,
            ClienteId = c.ClienteId,
            ClienteNombre = c.ClienteNombre,
            TipoEvento = c.TipoEvento,
            FechaEvento = c.FechaEvento,
            Contacto = c.Contacto,
            Estado = c.Estado.ToString(),
            Traslado = c.Traslado,
            Montaje = c.Montaje,
            Total = c.Total,
            Abono = c.Abono,
            Notas = c.Notas,
            CreadaPor = c.UsuarioCreador != null ? c.UsuarioCreador.Nombre : null,
            CreadoEn = c.CreadoEn,
            VentaId = c.VentaId,
            VentaFolio = c.Venta != null ? c.Venta.Folio : null,
            Lineas = c.Items.Count
        });

    private async Task<Cotizacion> BuscarAsync(int id, CancellationToken ct)
        => await _db.Cotizaciones.FirstOrDefaultAsync(c => c.Id == id, ct)
           ?? throw new NoEncontradoException("La cotización");

    private async Task<long> SiguienteFolioAsync(CancellationToken ct)
    {
        var conexion = _db.Database.GetDbConnection();
        if (conexion.State != System.Data.ConnectionState.Open)
            await conexion.OpenAsync(ct);

        using var comando = conexion.CreateCommand();
        comando.CommandText = "SELECT nextval('seq_folio_cotizacion')";
        if (_db.Database.CurrentTransaction is not null)
            comando.Transaction = _db.Database.CurrentTransaction.GetDbTransaction();

        return Convert.ToInt64(await comando.ExecuteScalarAsync(ct));
    }

    private static string? Limpiar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();

    private static EstadoCotizacion AEstado(string valor)
        => Enum.TryParse<EstadoCotizacion>(valor?.Trim().ToLowerInvariant(), out var e)
            ? e
            : throw new ExcepcionNegocio(
                "Estado no válido. Debe ser borrador, aprobada, cobrada o anulada.");

    private static MedioPago AMedioPago(string valor)
        => Enum.TryParse<MedioPago>(valor?.Trim().ToLowerInvariant(), out var m)
            ? m
            : throw new ExcepcionNegocio(
                "Medio de pago no válido. Debe ser efectivo, debito, credito o transferencia.");
}