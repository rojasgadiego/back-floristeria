using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Promociones;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Promociones.Dtos;

namespace Colibri.Api.Features.Promociones;

public class PromocionesService : IPromocionesService
{
    private static readonly string[] NombresDia =
    {
        "domingo", "lunes", "martes", "miércoles", "jueves", "viernes", "sábado"
    };

    // Un rango mayor cargaría decenas de miles de boletas en memoria para
    // simular. Medio año es de sobra para decidir si una promoción conviene.
    private const int MaximoDiasSimulacion = 180;

    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<PromocionesService> _log;

    public PromocionesService(
        ColibriDbContext db, IUsuarioActual usuarioActual, ILogger<PromocionesService> log)
    {
        _db = db;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    public async Task<ResultadoPagina<PromocionDto>> ListarAsync(
        PromocionFiltro filtro, CancellationToken ct = default)
    {
        var consulta = _db.Promociones.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(p =>
                p.Nombre.ToLower().Contains(q) ||
                (p.Descripcion != null && p.Descripcion.ToLower().Contains(q)));
        }

        if (filtro.Activa.HasValue)
            consulta = consulta.Where(p => p.Activa == filtro.Activa.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Alcance))
            consulta = consulta.Where(p => p.Alcance == AAlcance(filtro.Alcance));

        var total = await consulta.CountAsync(ct);

        var entidades = await consulta
            .OrderByDescending(p => p.Activa).ThenBy(p => p.Nombre)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        var items = await PresentarAsync(entidades, ct);

        // La vigencia depende del día y del rango de fechas: se filtra
        // después de calcularla, no en SQL.
        if (filtro.SoloVigentes)
            items = items.Where(p => p.VigenteHoy).ToList();

        return ResultadoPagina<PromocionDto>.Crear(items, total, filtro);
    }

    public async Task<PromocionDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var promo = await _db.Promociones.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La promoción");

        var b = (await PresentarAsync(new[] { promo }, ct)).First();

        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => v.PromocionId == id && !v.Anulada)
            .Select(v => new { v.DescuentoPromo, v.Bruto })
            .ToListAsync(ct);

        var conflictos = await DetectarConflictosAsync(promo, ct);

        return new PromocionDetalleDto
        {
            Id = b.Id,
            Nombre = b.Nombre,
            Descripcion = b.Descripcion,
            Tipo = b.Tipo,
            Valor = b.Valor,
            Alcance = b.Alcance,
            CategoriaId = b.CategoriaId,
            Categoria = b.Categoria,
            ProductoId = b.ProductoId,
            Producto = b.Producto,
            Minimo = b.Minimo,
            Desde = b.Desde,
            Hasta = b.Hasta,
            Dias = b.Dias,
            DiasTexto = b.DiasTexto,
            Activa = b.Activa,
            VigenteHoy = b.VigenteHoy,
            MotivoNoVigente = b.MotivoNoVigente,
            Usos = b.Usos,
            DescuentoAcumulado = b.DescuentoAcumulado,
            UltimoUso = b.UltimoUso,

            DescuentoPromedio = ventas.Count > 0
                ? (int)ventas.Average(v => v.DescuentoPromo)
                : 0,
            VentaAsociada = ventas.Sum(v => (long)v.Bruto),
            Conflictos = conflictos
        };
    }

    /// <summary>
    /// Las que corren hoy: activas, dentro de su vigencia y en un día
    /// habilitado. Es lo que el punto de venta puede ofrecer.
    /// </summary>
    public async Task<IReadOnlyList<PromocionDto>> VigentesAsync(CancellationToken ct = default)
    {
        var activas = await _db.Promociones.AsNoTracking()
            .Where(p => p.Activa)
            .OrderBy(p => p.Nombre)
            .ToListAsync(ct);

        var todas = await PresentarAsync(activas, ct);
        return todas.Where(p => p.VigenteHoy).ToList();
    }

    /* ==================================================================
       ESCRITURA
       ================================================================== */

    public async Task<PromocionDetalleDto> CrearAsync(
        GuardarPromocionRequest peticion, CancellationToken ct = default)
    {
        var promo = new Promocion();
        await AplicarAsync(promo, peticion, ct);

        _db.Promociones.Add(promo);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Promoción creada: {Nombre} por {Autor}",
            promo.Nombre, _usuarioActual.Email);

        return await ObtenerAsync(promo.Id, ct);
    }

    public async Task<PromocionDetalleDto> ActualizarAsync(
        int id, GuardarPromocionRequest peticion, CancellationToken ct = default)
    {
        var promo = await _db.Promociones.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La promoción");

        // Editar una promoción usada NO reescribe las boletas: cada venta
        // guardó su descuento calculado. Lo que cambia es de aquí en adelante.
        if (promo.Usos > 0)
        {
            _log.LogInformation(
                "Se editó la promoción {Nombre}, que ya tiene {Usos} usos. Las boletas " +
                "emitidas conservan el descuento con que se calcularon.",
                promo.Nombre, promo.Usos);
        }

        await AplicarAsync(promo, peticion, ct);
        await _db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, ct);
    }

    public async Task<PromocionDto> CambiarEstadoAsync(
        int id, bool activa, CancellationToken ct = default)
    {
        var promo = await _db.Promociones.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La promoción");

        promo.Activa = activa;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Promoción {Nombre} {Estado} por {Autor}",
            promo.Nombre, activa ? "activada" : "desactivada", _usuarioActual.Email);

        return (await PresentarAsync(new[] { promo }, ct)).First();
    }

    /// <summary>
    /// Borrado definitivo. Solo para promociones creadas por error: si ya se
    /// usó, las boletas la referencian y borrarla dejaría el histórico sin
    /// poder explicar de dónde salió ese descuento.
    /// </summary>
    public async Task EliminarAsync(int id, CancellationToken ct = default)
    {
        var promo = await _db.Promociones.FirstOrDefaultAsync(p => p.Id == id, ct)
            ?? throw new NoEncontradoException("La promoción");

        if (await _db.Ventas.AnyAsync(v => v.PromocionId == id, ct))
            throw new ExcepcionNegocio(
                $"«{promo.Nombre}» ya se aplicó en boletas. Desactívala en vez de " +
                "eliminarla, para no perder el historial de esos descuentos.");

        _db.Promociones.Remove(promo);
        await _db.SaveChangesAsync(ct);

        _log.LogWarning("Promoción {Nombre} eliminada por {Autor}",
            promo.Nombre, _usuarioActual.Email);
    }

    /* ==================================================================
       SIMULACIÓN
       ================================================================== */

    /// <summary>
    /// Evalúa la promoción contra las ventas reales del período.
    ///
    /// Una promoción de 20% sobre tallos los martes suena bien hasta que se
    /// calcula cuánto habría costado el martes pasado. Usa exactamente la
    /// misma regla que el cobro, así lo que dice acá es lo que va a pasar.
    /// </summary>
    public async Task<SimulacionDto> SimularAsync(
        SimularRequest peticion, CancellationToken ct = default)
    {
        var promo = new Promocion();
        await AplicarAsync(promo, peticion, ct, validarConflictos: false);

        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var inicio = peticion.PeriodoDesde ?? hoy.AddDays(-30);
        var fin = peticion.PeriodoHasta ?? hoy;

        if (fin < inicio)
            throw new ExcepcionNegocio("La fecha de término es anterior a la de inicio.");

        if (fin.DayNumber - inicio.DayNumber > MaximoDiasSimulacion)
            throw new ExcepcionNegocio(
                $"El período no puede superar {MaximoDiasSimulacion} días.");

        var desdeDt = new DateTimeOffset(inicio.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
        var hastaDt = new DateTimeOffset(fin.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

        // Se traen las boletas con sus líneas: la regla necesita saber qué
        // producto y qué categoría hay en cada una.
        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => !v.Anulada && v.CreadoEn >= desdeDt && v.CreadoEn < hastaDt)
            .Select(v => new
            {
                v.Id,
                v.Folio,
                v.CreadoEn,
                v.Bruto,
                Lineas = v.Items.Select(i => new
                {
                    i.ProductoId,
                    CategoriaId = i.Producto != null ? i.Producto.CategoriaId : 0,
                    i.Subtotal,
                    i.EsServicio
                }).ToList()
            })
            .ToListAsync(ct);

        var aplicadas = new List<EjemploSimulacionDto>();
        long descuentoTotal = 0;
        long ventaAlcanzada = 0;

        foreach (var venta in ventas)
        {
            var fecha = DateOnly.FromDateTime(venta.CreadoEn.LocalDateTime);
            if (!ReglaPromocion.EstaVigente(promo, fecha)) continue;

            var lineas = venta.Lineas
                .Select(l => new LineaPromocion(
                    l.ProductoId, l.CategoriaId, l.Subtotal, l.EsServicio))
                .ToList();

            var descuento = ReglaPromocion.Calcular(promo, lineas, venta.Bruto);
            if (descuento <= 0) continue;

            descuentoTotal += descuento;
            ventaAlcanzada += venta.Bruto;

            aplicadas.Add(new EjemploSimulacionDto
            {
                Folio = venta.Folio,
                Fecha = venta.CreadoEn,
                Bruto = venta.Bruto,
                DescuentoSimulado = descuento,
                TotalConPromocion = venta.Bruto - descuento
            });
        }

        var brutoPeriodo = ventas.Sum(v => (long)v.Bruto);

        return new SimulacionDto
        {
            Nombre = promo.Nombre,
            Desde = inicio,
            Hasta = fin,
            BoletasEvaluadas = ventas.Count,
            BoletasQueAplican = aplicadas.Count,
            PorcentajeCobertura = ventas.Count > 0
                ? Math.Round(100m * aplicadas.Count / ventas.Count, 1)
                : 0,
            DescuentoTotal = descuentoTotal,
            DescuentoPromedio = aplicadas.Count > 0
                ? (int)(descuentoTotal / aplicadas.Count)
                : 0,
            VentaAlcanzada = ventaAlcanzada,
            // Sobre la venta total del período, no solo sobre las alcanzadas:
            // es lo que mide el costo real de la promoción para el negocio.
            ImpactoSobreVentas = brutoPeriodo > 0
                ? Math.Round(100m * descuentoTotal / brutoPeriodo, 2)
                : 0,
            Ejemplos = aplicadas
                .OrderByDescending(e => e.DescuentoSimulado)
                .Take(5)
                .ToList()
        };
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    /// <summary>
    /// Valida la petición y la vuelca sobre la entidad. La base verifica las
    /// mismas reglas con CHECK; acá los mensajes explican qué se esperaba.
    /// </summary>
    private async Task AplicarAsync(
        Promocion promo, GuardarPromocionRequest peticion,
        CancellationToken ct, bool validarConflictos = true)
    {
        var tipo = ATipo(peticion.Tipo);
        var alcance = AAlcance(peticion.Alcance);

        if (tipo == TipoPromocion.porcentaje && peticion.Valor > 100)
            throw new ExcepcionNegocio("Un descuento por porcentaje no puede superar el 100%.");

        if (peticion.Desde.HasValue && peticion.Hasta.HasValue &&
            peticion.Desde.Value > peticion.Hasta.Value)
        {
            throw new ExcepcionNegocio("La fecha de término es anterior a la de inicio.");
        }

        var dias = (peticion.Dias ?? new List<short>()).Distinct().OrderBy(d => d).ToArray();

        if (dias.Any(d => d is < 0 or > 6))
            throw new ExcepcionNegocio("Los días deben estar entre 0 (domingo) y 6 (sábado).");

        // Siete días marcados es lo mismo que ninguno, y la lista vacía es
        // más barata de evaluar
        if (dias.Length == 7) dias = Array.Empty<short>();

        // El alcance decide qué campo es obligatorio. La base lo verifica con
        // un CHECK, pero un mensaje claro evita el viaje de ida y vuelta.
        switch (alcance)
        {
            case AlcancePromocion.categoria:
                if (peticion.CategoriaId is null)
                    throw new ExcepcionNegocio(
                        "Una promoción por categoría necesita indicar cuál.");

                if (!await _db.Categorias.AnyAsync(c => c.Id == peticion.CategoriaId.Value, ct))
                    throw new ExcepcionNegocio("La categoría indicada no existe.");

                promo.CategoriaId = peticion.CategoriaId;
                promo.ProductoId = null;
                break;

            case AlcancePromocion.producto:
                if (peticion.ProductoId is null)
                    throw new ExcepcionNegocio(
                        "Una promoción por producto necesita indicar cuál.");

                if (!await _db.Productos.AnyAsync(p => p.Id == peticion.ProductoId.Value, ct))
                    throw new ExcepcionNegocio("El producto indicado no existe.");

                promo.ProductoId = peticion.ProductoId;
                promo.CategoriaId = null;
                break;

            default:
                promo.CategoriaId = null;
                promo.ProductoId = null;
                break;
        }

        promo.Nombre = peticion.Nombre.Trim();
        promo.Descripcion = string.IsNullOrWhiteSpace(peticion.Descripcion)
            ? null : peticion.Descripcion.Trim();
        promo.Tipo = tipo;
        promo.Valor = peticion.Valor;
        promo.Alcance = alcance;
        promo.Minimo = peticion.Minimo;
        promo.Desde = peticion.Desde;
        promo.Hasta = peticion.Hasta;
        promo.Dias = dias;

        if (promo.Id == 0) promo.Activa = true;
    }

    /// <summary>
    /// Otras promociones que pueden aplicar a la misma boleta.
    ///
    /// No es un error: el punto de venta ofrece la más conveniente. Pero si
    /// una es siempre peor que otra, nunca se va a usar, y conviene saberlo
    /// antes de anunciarla.
    /// </summary>
    private async Task<List<ConflictoDto>> DetectarConflictosAsync(
        Promocion promo, CancellationToken ct)
    {
        var candidatas = await _db.Promociones.AsNoTracking()
            .Where(p => p.Id != promo.Id && p.Activa && p.Alcance == promo.Alcance)
            .ToListAsync(ct);

        return candidatas
            .Where(otra => ReglaPromocion.SeSolapan(promo, otra))
            .Select(otra => new ConflictoDto
            {
                PromocionId = otra.Id,
                Nombre = otra.Nombre,
                Tipo = otra.Tipo.ToString(),
                Valor = otra.Valor,
                Activa = otra.Activa,
                Detalle = Explicar(promo, otra)
            })
            .ToList();
    }

    private static string Explicar(Promocion a, Promocion b)
    {
        var alcance = a.Alcance switch
        {
            AlcancePromocion.boleta => "sobre el total de la boleta",
            AlcancePromocion.categoria => "sobre la misma categoría",
            _ => "sobre el mismo producto"
        };

        var minimo = a.Minimo == b.Minimo
            ? $"con el mismo mínimo de {a.Minimo:N0}"
            : $"con mínimos de {a.Minimo:N0} y {b.Minimo:N0}";

        return $"Ambas descuentan {alcance} {minimo}. En una boleta que cumpla las " +
               "dos, el punto de venta ofrecerá la que más descuente.";
    }

    /// <summary>
    /// Arma los DTO resolviendo nombres, vigencia y uso acumulado.
    /// La vigencia se calcula en memoria: depende del día de la semana, que
    /// PostgreSQL no tiene por qué evaluar en cada fila.
    /// </summary>
    private async Task<List<PromocionDto>> PresentarAsync(
        IReadOnlyCollection<Promocion> promos, CancellationToken ct)
    {
        if (promos.Count == 0) return new List<PromocionDto>();

        var ids = promos.Select(p => p.Id).ToList();

        var uso = await _db.Ventas.AsNoTracking()
            .Where(v => v.PromocionId != null && ids.Contains(v.PromocionId.Value) && !v.Anulada)
            .GroupBy(v => v.PromocionId!.Value)
            .Select(g => new
            {
                PromocionId = g.Key,
                Descuento = g.Sum(v => (long)v.DescuentoPromo),
                Ultimo = g.Max(v => (DateTimeOffset?)v.CreadoEn)
            })
            .ToDictionaryAsync(x => x.PromocionId, ct);

        var categorias = await _db.Categorias.AsNoTracking()
            .ToDictionaryAsync(c => c.Id, c => c.Nombre, ct);

        var productoIds = promos.Where(p => p.ProductoId.HasValue)
            .Select(p => p.ProductoId!.Value).Distinct().ToList();

        var productos = productoIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Productos.AsNoTracking()
                .Where(p => productoIds.Contains(p.Id))
                .ToDictionaryAsync(p => p.Id, p => p.Nombre, ct);

        var hoy = DateOnly.FromDateTime(DateTime.Today);

        return promos.Select(p =>
        {
            uso.TryGetValue(p.Id, out var u);

            var vigente = p.Activa && ReglaPromocion.EstaVigente(p, hoy);

            return new PromocionDto
            {
                Id = p.Id,
                Nombre = p.Nombre,
                Descripcion = p.Descripcion,
                Tipo = p.Tipo.ToString(),
                Valor = p.Valor,
                Alcance = p.Alcance.ToString(),
                CategoriaId = p.CategoriaId,
                Categoria = p.CategoriaId.HasValue && categorias.ContainsKey(p.CategoriaId.Value)
                    ? categorias[p.CategoriaId.Value] : null,
                ProductoId = p.ProductoId,
                Producto = p.ProductoId.HasValue && productos.ContainsKey(p.ProductoId.Value)
                    ? productos[p.ProductoId.Value] : null,
                Minimo = p.Minimo,
                Desde = p.Desde,
                Hasta = p.Hasta,
                Dias = p.Dias,
                DiasTexto = TextoDias(p.Dias),
                Activa = p.Activa,
                VigenteHoy = vigente,
                MotivoNoVigente = vigente ? null : PorQueNoCorre(p, hoy),
                Usos = p.Usos,
                DescuentoAcumulado = u?.Descuento ?? 0,
                UltimoUso = u?.Ultimo
            };
        }).ToList();
    }

    /// <summary>
    /// Por qué no corre hoy. Sin esto, la interfaz muestra una promoción
    /// "activa" que el punto de venta no ofrece, y nadie entiende por qué.
    /// </summary>
    private static string PorQueNoCorre(Promocion p, DateOnly hoy)
    {
        if (!p.Activa) return "Está desactivada.";

        if (p.Desde.HasValue && hoy < p.Desde.Value)
            return $"Empieza el {p.Desde.Value:dd-MM-yyyy}.";

        if (p.Hasta.HasValue && hoy > p.Hasta.Value)
            return $"Terminó el {p.Hasta.Value:dd-MM-yyyy}.";

        if (p.Dias.Length > 0 && !p.Dias.Contains((short)hoy.DayOfWeek))
            return $"Solo corre {TextoDias(p.Dias)}.";

        return "No está vigente hoy.";
    }

    private static string TextoDias(short[] dias)
    {
        if (dias is null || dias.Length == 0 || dias.Length == 7) return "todos los días";

        var nombres = dias.OrderBy(d => d)
            .Where(d => d is >= 0 and <= 6)
            .Select(d => NombresDia[d])
            .ToList();

        if (nombres.Count == 0) return "todos los días";
        if (nombres.Count == 1) return $"los {nombres[0]}";

        return "los " + string.Join(", ", nombres.Take(nombres.Count - 1)) +
               " y " + nombres[^1];
    }

    private static TipoPromocion ATipo(string valor)
        => Enum.TryParse<TipoPromocion>(valor?.Trim().ToLowerInvariant(), out var t)
            ? t
            : throw new ExcepcionNegocio("Tipo no válido. Debe ser porcentaje o monto.");

    private static AlcancePromocion AAlcance(string valor)
        => Enum.TryParse<AlcancePromocion>(valor?.Trim().ToLowerInvariant(), out var a)
            ? a
            : throw new ExcepcionNegocio(
                "Alcance no válido. Debe ser boleta, categoria o producto.");
}