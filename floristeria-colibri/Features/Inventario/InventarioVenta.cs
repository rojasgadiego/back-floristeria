// ============================================================================
// Features/InventarioVenta/  ·  el mostrador
// ----------------------------------------------------------------------------
// Un módulo nuevo, chico. Todo el trabajo pesado lo hacen fn_traspasar,
// fn_retornar y fn_conteo: acá solo se valida quién puede hacer qué y se
// traduce el resultado a DTOs.
// ============================================================================

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Inventario;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;

namespace Colibri.Api.Features.InventarioVenta;

/* ==========================================================================
   DTOs
   ========================================================================== */

/// <summary>Una partida que está en el mostrador, lista para vender.</summary>
public class VendibleDto
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int CategoriaId { get; set; }
    public int Disponible { get; set; }

    /// <summary>optima · buena · limitada. Null en flor de primera recién comprada.</summary>
    public string? Calidad { get; set; }

    /// <summary>Lo que se cobra por vara. Ya resuelto según calidad y precio propio.</summary>
    public int Precio { get; set; }
    public decimal CostoPorVara { get; set; }
    public DateOnly? FechaVencimiento { get; set; }

    /// <summary>
    /// Queda fuera del reparto automático: hay que elegirlo a propósito. Si
    /// entrara en la fila normal, una venta cualquiera despacharía flor de
    /// liquidación al precio que le tocara sin que nadie lo decida.
    /// </summary>
    public bool RequiereEscaneo { get; set; }
    public bool Vencido { get; set; }
}

/// <summary>Resumen del mostrador, agrupado por producto.</summary>
public class LineaMostradorDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int Disponible { get; set; }
    public int EnBodega { get; set; }
    public decimal ValorCosto { get; set; }
    public int ValorPrecio { get; set; }
    public int Minimo { get; set; }

    /// <summary>
    /// El mínimo comparado contra el MOSTRADOR, no contra la bodega. Es el
    /// aviso de qué hay que bajar al frente, que es la decisión que este
    /// panel tiene que apoyar.
    /// </summary>
    public bool HayQueReponer { get; set; }
    public DateOnly? VenceAntes { get; set; }
    public IReadOnlyList<VendibleDto> Partidas { get; set; } = Array.Empty<VendibleDto>();
}

public class EstadoMostradorDto
{
    public int Unidades { get; set; }
    public decimal ValorCosto { get; set; }
    public int ValorPrecio { get; set; }

    /// <summary>Lo que se ganaría si se vendiera todo lo que está adelante.</summary>
    public decimal MargenPotencial { get; set; }
    public IReadOnlyList<LineaMostradorDto> Lineas { get; set; } = Array.Empty<LineaMostradorDto>();
}

public class TraspasoDto
{
    public int LoteVentaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class ResultadoConteoDto
{
    public int LoteId { get; set; }
    public int SegunSistema { get; set; }
    public int Contado { get; set; }
    public int Diferencia { get; set; }
}

public class TraspasarRequest
{
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }

    /// <summary>
    /// Lote concreto a bajar. Sin esto se toma por FIFO, que es lo correcto
    /// casi siempre; se indica cuando hay que sacar uno rezagado.
    /// </summary>
    public int? LoteId { get; set; }
}

public class RetornarRequest
{
    public int LoteVentaId { get; set; }
    public int Cantidad { get; set; }
}

public class ConteoRequest
{
    public int LoteId { get; set; }

    /// <summary>Lo que hay en el balde, no la diferencia.</summary>
    public int Contado { get; set; }
    public string? Detalle { get; set; }
}

/* ==========================================================================
   Entidades sin clave para los retornos de las funciones
   ========================================================================== */

public class ResultadoTraspaso
{
    public int LoteVentaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

public class ResultadoConteo
{
    public int SegunSistema { get; set; }
    public int Contado { get; set; }
    public int Diferencia { get; set; }
}

// En ColibriDbContext.OnModelCreating:
//
//     modelBuilder.Entity<ResultadoTraspaso>().HasNoKey().ToView(null);
//     modelBuilder.Entity<ResultadoConteo>().HasNoKey().ToView(null);
//     modelBuilder.Entity<Vendible>().HasNoKey().ToView("vw_vendibles");

/* ==========================================================================
   Servicio
   ========================================================================== */

public interface IInventarioVentaService
{
    Task<EstadoMostradorDto> EstadoAsync(CancellationToken ct = default);
    Task<IReadOnlyList<VendibleDto>> VendiblesAsync(CancellationToken ct = default);
    Task<IReadOnlyList<TraspasoDto>> TraspasarAsync(TraspasarRequest peticion, CancellationToken ct = default);
    Task RetornarAsync(RetornarRequest peticion, CancellationToken ct = default);
    Task<ResultadoConteoDto> ContarAsync(ConteoRequest peticion, CancellationToken ct = default);
}

public class InventarioVentaService : IInventarioVentaService
{
    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _yo;
    private readonly ILogger<InventarioVentaService> _log;

    public InventarioVentaService(
        ColibriDbContext db, IUsuarioActual yo, ILogger<InventarioVentaService> log)
    {
        _db = db;
        _yo = yo;
        _log = log;
    }

    public async Task<IReadOnlyList<VendibleDto>> VendiblesAsync(CancellationToken ct = default)
        => await _db.Vendibles.AsNoTracking()
            .OrderBy(v => v.Producto)
            .ThenBy(v => v.FechaVencimiento)
            .Select(v => new VendibleDto
            {
                LoteId = v.LoteId,
                Codigo = v.Codigo,
                ProductoId = v.ProductoId,
                Producto = v.Producto,
                Emoji = v.Emoji,
                CategoriaId = v.CategoriaId,
                Disponible = v.Disponible,
                Calidad = v.Calidad,
                Precio = v.Precio,
                CostoPorVara = v.CostoPorVara,
                FechaVencimiento = v.FechaVencimiento,
                RequiereEscaneo = v.RequiereEscaneo,
                Vencido = v.Vencido
            })
            .ToListAsync(ct);

    public async Task<EstadoMostradorDto> EstadoAsync(CancellationToken ct = default)
    {
        var partidas = await VendiblesAsync(ct);

        // Cuánto queda atrás de cada producto: es la otra mitad de la decisión
        // de reponer. Saber que faltan rosas adelante no sirve si no se sabe
        // si quedan en cámara.
        var enBodega = await _db.Lotes.AsNoTracking()
            .Where(l => l.Ubicacion == Ubicacion.bodega && l.Estado == EstadoLote.activo)
            .GroupBy(l => l.ProductoId)
            .Select(g => new { ProductoId = g.Key, Varas = g.Sum(x => x.VarasDisponibles) })
            .ToDictionaryAsync(x => x.ProductoId, x => x.Varas, ct);

        var fichas = await _db.Productos.AsNoTracking()
            .Where(p => p.Activo)
            .Select(p => new { p.Id, p.Tipo, p.Minimo })
            .ToDictionaryAsync(p => p.Id, ct);

        var lineas = partidas
            .GroupBy(p => new { p.ProductoId, p.Producto, p.Emoji })
            .Select(g =>
            {
                var ficha = fichas.GetValueOrDefault(g.Key.ProductoId);
                var disponible = g.Sum(x => x.Disponible);
                var minimo = ficha?.Minimo ?? 0;

                return new LineaMostradorDto
                {
                    ProductoId = g.Key.ProductoId,
                    Producto = g.Key.Producto,
                    Emoji = g.Key.Emoji,
                    Tipo = ficha?.Tipo.ToString() ?? "simple",
                    Disponible = disponible,
                    EnBodega = enBodega.GetValueOrDefault(g.Key.ProductoId),
                    ValorCosto = g.Sum(x => x.CostoPorVara * x.Disponible),
                    ValorPrecio = g.Sum(x => x.Precio * x.Disponible),
                    Minimo = minimo,
                    HayQueReponer = disponible <= minimo,
                    VenceAntes = g.Where(x => x.FechaVencimiento.HasValue)
                                  .Select(x => x.FechaVencimiento)
                                  .DefaultIfEmpty(null)
                                  .Min(),
                    Partidas = g.OrderBy(x => x.FechaVencimiento).ToList()
                };
            })
            .OrderBy(l => l.Producto)
            .ToList();

        return new EstadoMostradorDto
        {
            Unidades = lineas.Sum(l => l.Disponible),
            ValorCosto = lineas.Sum(l => l.ValorCosto),
            ValorPrecio = lineas.Sum(l => l.ValorPrecio),
            MargenPotencial = lineas.Sum(l => l.ValorPrecio) - lineas.Sum(l => l.ValorCosto),
            Lineas = lineas
        };
    }

    /// <summary>
    /// Baja mercadería de la cámara al mostrador. Es el único camino por el
    /// que algo se vuelve vendible, y por eso es exclusivo de administración:
    /// define cuánto puede vender el equipo en el día.
    /// </summary>
    public async Task<IReadOnlyList<TraspasoDto>> TraspasarAsync(
        TraspasarRequest peticion, CancellationToken ct = default)
    {
        var producto = await _db.Productos.AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == peticion.ProductoId, ct)
            ?? throw new NoEncontradoException("El producto");

        if (!producto.Activo)
            throw new ExcepcionNegocio($"{producto.Nombre} está desactivado.");

        if (peticion.Cantidad < 1)
            throw new ExcepcionNegocio("Indica cuántas unidades vas a pasar al mostrador.");

        // Los productos sin control por lote no viven en lotes: están en el
        // estante y se venden de ahí. Traspasarlos no significa nada.
        if (!producto.ControlaLotes)
            throw new ExcepcionNegocio(
                $"{producto.Nombre} no se controla por lote: está siempre disponible " +
                "para vender y no hace falta pasarlo al mostrador.");

        var usuarioId = _yo.IdRequerido();
        var loteId = peticion.LoteId;

        var filas = await _db.ResultadosTraspaso
            .FromSqlInterpolated($@"
                SELECT * FROM fn_traspasar(
                    {peticion.ProductoId}, {peticion.Cantidad}, {usuarioId}, {loteId})")
            .ToListAsync(ct);

        _log.LogInformation(
            "Traspaso al mostrador: {Cantidad} de {Producto} en {Partidas} partida(s) por {Autor}",
            peticion.Cantidad, producto.Nombre, filas.Count, _yo.Email);

        return filas.Select(f => new TraspasoDto
        {
            LoteVentaId = f.LoteVentaId,
            Codigo = f.Codigo,
            Cantidad = f.Cantidad
        }).ToList();
    }

    /// <summary>
    /// Devuelve al fondo lo que se bajó de más. Las varas vuelven a su lote de
    /// origen: conservan costo, procedencia y vencimiento, porque el lote
    /// nunca cambió de fecha.
    /// </summary>
    public async Task RetornarAsync(RetornarRequest peticion, CancellationToken ct = default)
    {
        var lote = await _db.Lotes.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == peticion.LoteVentaId, ct)
            ?? throw new NoEncontradoException("La partida");

        if (lote.Ubicacion != Ubicacion.venta)
            throw new ExcepcionNegocio("Esa partida no está en el mostrador.");

        var usuarioId = _yo.IdRequerido();

        await _db.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT fn_retornar({peticion.LoteVentaId}, {peticion.Cantidad}, {usuarioId})", ct);

        _log.LogInformation("Retorno a bodega: {Cantidad} del lote {Lote} por {Autor}",
            peticion.Cantidad, lote.Codigo, _yo.Email);
    }

    /// <summary>
    /// Conteo físico. Reemplaza al ajuste manual.
    ///
    /// No dice «suma 7»: dice «conté 113». El sistema calcula la diferencia y
    /// deja registrado lo que él creía. Es una auditoría, no una corrección
    /// anónima, y es la única operación que puede hacer aparecer existencias
    /// sin causa física: por eso queda reservada a administración.
    /// </summary>
    public async Task<ResultadoConteoDto> ContarAsync(
        ConteoRequest peticion, CancellationToken ct = default)
    {
        if (peticion.Contado < 0)
            throw new ExcepcionNegocio("Lo contado no puede ser negativo.");

        var lote = await _db.Lotes.AsNoTracking()
            .FirstOrDefaultAsync(l => l.Id == peticion.LoteId, ct)
            ?? throw new NoEncontradoException("El lote");

        var usuarioId = _yo.IdRequerido();
        var detalle = peticion.Detalle;

        var fila = await _db.ResultadosConteo
            .FromSqlInterpolated($@"
                SELECT * FROM fn_conteo(
                    {peticion.LoteId}, {peticion.Contado}, {usuarioId}, {detalle})")
            .FirstAsync(ct);

        if (fila.Diferencia != 0)
        {
            // Un conteo que no cuadra es información, no un trámite: queda en
            // el log aunque el movimiento ya esté en el libro.
            _log.LogWarning(
                "Conteo del lote {Lote}: el sistema decía {Sistema}, se contaron {Contado} ({Dif}) · {Autor}",
                lote.Codigo, fila.SegunSistema, fila.Contado, fila.Diferencia, _yo.Email);
        }

        return new ResultadoConteoDto
        {
            LoteId = peticion.LoteId,
            SegunSistema = fila.SegunSistema,
            Contado = fila.Contado,
            Diferencia = fila.Diferencia
        };
    }
}

/* ==========================================================================
   Controlador
   ========================================================================== */

/// <summary>El mostrador: lo que está adelante y se puede vender.</summary>
[Authorize(Policy = Politicas.VerInventario)]
[Route("api/inventario-venta")]
public class InventarioVentaController : ControladorBase
{
    private readonly IInventarioVentaService _mostrador;

    public InventarioVentaController(IInventarioVentaService mostrador)
        => _mostrador = mostrador;

    /// <summary>Resumen del mostrador con valores y avisos de reposición.</summary>
    [HttpGet]
    public async Task<IActionResult> Estado(CancellationToken ct)
        => Exito(await _mostrador.EstadoAsync(ct));

    /// <summary>El catálogo del vendedor: solo lo que hay adelante.</summary>
    [HttpGet("vendibles")]
    public async Task<IActionResult> Vendibles(CancellationToken ct)
        => Exito(await _mostrador.VendiblesAsync(ct));

    /// <summary>Baja mercadería de la cámara al mostrador.</summary>
    [HttpPost("traspasar")]
    [Authorize(Policy = Politicas.Mostrador)]
    public async Task<IActionResult> Traspasar(
        [FromBody] TraspasarRequest peticion, CancellationToken ct)
        => Exito(await _mostrador.TraspasarAsync(peticion, ct));

    /// <summary>Devuelve a la cámara lo que se bajó de más.</summary>
    [HttpPost("retornar")]
    [Authorize(Policy = Politicas.Mostrador)]
    public async Task<IActionResult> Retornar(
        [FromBody] RetornarRequest peticion, CancellationToken ct)
    {
        await _mostrador.RetornarAsync(peticion, ct);
        return Exito(await _mostrador.EstadoAsync(ct));
    }

    /// <summary>Conteo físico de un lote. Reemplaza al ajuste manual.</summary>
    [HttpPost("conteo")]
    [Authorize(Policy = Politicas.Conteo)]
    public async Task<IActionResult> Conteo(
        [FromBody] ConteoRequest peticion, CancellationToken ct)
        => Exito(await _mostrador.ContarAsync(peticion, ct));
}

/* ==========================================================================
   Registro · Startup/AgregarInventarioVenta.cs
   ========================================================================== */

public static class InventarioVentaExtensiones
{
    public static IServiceCollection AgregarInventarioVenta(this IServiceCollection servicios)
    {
        servicios.AddScoped<IInventarioVentaService, InventarioVentaService>();
        return servicios;
    }
}

// En Politicas:
//     public const string Mostrador = "Mostrador";   // traspasar y retornar
//     public const string Conteo    = "Conteo";      // conteo físico
//
// En AddAuthorization:
//     o.AddPolicy(Politicas.Mostrador, p => p.RequireRole(nameof(RolUsuario.admin)));
//     o.AddPolicy(Politicas.Conteo,    p => p.RequireRole(nameof(RolUsuario.admin)));