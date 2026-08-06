using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Context;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Ventas.Dtos;
using Microsoft.EntityFrameworkCore;

namespace Colibri.Api.Features.Ventas;

public class CajaService : ICajaService
{
    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<CajaService> _log;

    public CajaService(ColibriDbContext db, IUsuarioActual usuarioActual, ILogger<CajaService> log)
    {
        _db = db;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    public async Task<ResumenCajaDto?> ActualAsync(CancellationToken ct = default)
    {
        var id = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estado == EstadoCaja.abierta)
            .Select(c => (int?)c.Id)
            .FirstOrDefaultAsync(ct);

        return id.HasValue ? await ResumenAsync(id.Value, ct) : null;
    }

    public async Task<CajaDto> AbrirAsync(
        AbrirCajaRequest peticion, CancellationToken ct = default)
    {
        // Se comprueba acá para dar un mensaje claro; el índice único parcial
        // cajas_una_sola_abierta es el que realmente lo garantiza si dos
        // personas abren al mismo tiempo.
        var abierta = await _db.Cajas.AsNoTracking()
            .Where(c => c.Estado == EstadoCaja.abierta)
            .Select(c => new { c.Id, Quien = c.UsuarioApertura.Nombre, c.AbiertaEn })
            .FirstOrDefaultAsync(ct);

        if (abierta is not null)
            throw new ExcepcionNegocio(
                $"Ya hay una caja abierta por {abierta.Quien} desde las " +
                $"{abierta.AbiertaEn.ToLocalTime():HH:mm}. Ciérrala antes de abrir otra.",
                StatusCodes.Status409Conflict);

        var caja = new Caja
        {
            Estado = EstadoCaja.abierta,
            FondoInicial = peticion.FondoInicial,
            AbiertaPor = _usuarioActual.IdRequerido()
        };

        _db.Cajas.Add(caja);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Caja {Id} abierta con fondo {Fondo} por {Autor}",
            caja.Id, caja.FondoInicial, _usuarioActual.Email);

        return await ProyectarAsync(caja.Id, ct);
    }

    /// <summary>
    /// Cierra el turno. El efectivo esperado se calcula desde las boletas, no
    /// se pide: si se pidiera, la diferencia dejaría de significar nada.
    /// </summary>
    public async Task<ResumenCajaDto> CerrarAsync(
        CerrarCajaRequest peticion, CancellationToken ct = default)
    {
        var caja = await _db.Cajas.FirstOrDefaultAsync(c => c.Estado == EstadoCaja.abierta, ct)
            ?? throw new ExcepcionNegocio("No hay ninguna caja abierta.");

        var efectivo = await _db.Ventas.AsNoTracking()
            .Where(v => v.CajaId == caja.Id && !v.Anulada && v.MedioPago == MedioPago.efectivo)
            .SumAsync(v => (int?)v.Total, ct) ?? 0;

        var esperado = caja.FondoInicial + efectivo;

        caja.Estado = EstadoCaja.cerrada;
        caja.CerradaEn = DateTimeOffset.UtcNow;
        caja.CerradaPor = _usuarioActual.Id;
        caja.EfectivoEsperado = esperado;
        caja.EfectivoContado = peticion.EfectivoContado;
        caja.Diferencia = peticion.EfectivoContado - esperado;
        caja.NotaCierre = string.IsNullOrWhiteSpace(peticion.Nota) ? null : peticion.Nota.Trim();

        await _db.SaveChangesAsync(ct);

        if (caja.Diferencia != 0)
        {
            _log.LogWarning("Caja {Id} cerrada con diferencia de {Diferencia} por {Autor}",
                caja.Id, caja.Diferencia, _usuarioActual.Email);
        }

        return await ResumenAsync(caja.Id, ct);
    }

    public async Task<ResumenCajaDto> ResumenAsync(int id, CancellationToken ct = default)
    {
        var caja = await ProyectarAsync(id, ct);

        var ventas = await _db.Ventas.AsNoTracking()
            .Where(v => v.CajaId == id)
            .Select(v => new
            {
                v.Anulada,
                v.Total,
                v.DescuentoTotal,
                v.MedioPago,
                v.PuntosGanados,
                v.PuntosCanjeados
            })
            .ToListAsync(ct);

        var validas = ventas.Where(v => !v.Anulada).ToList();
        int porMedio(MedioPago m) => validas.Where(v => v.MedioPago == m).Sum(v => v.Total);

        var efectivo = porMedio(MedioPago.efectivo);

        return new ResumenCajaDto
        {
            Id = caja.Id,
            Estado = caja.Estado,
            FondoInicial = caja.FondoInicial,
            AbiertaEn = caja.AbiertaEn,
            AbiertaPor = caja.AbiertaPor,
            CerradaEn = caja.CerradaEn,
            CerradaPor = caja.CerradaPor,
            EfectivoEsperado = caja.EfectivoEsperado,
            EfectivoContado = caja.EfectivoContado,
            Diferencia = caja.Diferencia,
            NotaCierre = caja.NotaCierre,

            Boletas = validas.Count,
            Anuladas = ventas.Count - validas.Count,
            TotalVendido = validas.Sum(v => v.Total),
            TotalDescuentos = validas.Sum(v => v.DescuentoTotal),
            Efectivo = efectivo,
            Debito = porMedio(MedioPago.debito),
            Credito = porMedio(MedioPago.credito),
            Transferencia = porMedio(MedioPago.transferencia),
            EnCajon = caja.FondoInicial + efectivo,
            PuntosOtorgados = validas.Sum(v => v.PuntosGanados),
            PuntosCanjeados = validas.Sum(v => v.PuntosCanjeados)
        };
    }

    public async Task<ResultadoPagina<CajaDto>> HistorialAsync(
        CajaFiltro filtro, CancellationToken ct = default)
    {
        var consulta = Proyectar();

        if (filtro.Desde.HasValue)
        {
            var desde = new DateTimeOffset(
                filtro.Desde.Value.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(c => c.AbiertaEn >= desde);
        }

        if (filtro.Hasta.HasValue)
        {
            var hasta = new DateTimeOffset(
                filtro.Hasta.Value.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);
            consulta = consulta.Where(c => c.AbiertaEn < hasta);
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderByDescending(c => c.AbiertaEn)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<CajaDto>.Crear(items, total, filtro);
    }

    public async Task<int> CajaAbiertaIdAsync(CancellationToken ct = default)
        => await _db.Cajas.AsNoTracking()
               .Where(c => c.Estado == EstadoCaja.abierta)
               .Select(c => (int?)c.Id)
               .FirstOrDefaultAsync(ct)
           ?? throw new ExcepcionNegocio(
               "No hay una caja abierta. Abre el turno antes de vender.");

    private IQueryable<CajaDto> Proyectar()
        => _db.Cajas.AsNoTracking().Select(c => new CajaDto
        {
            Id = c.Id,
            Estado = c.Estado.ToString(),
            FondoInicial = c.FondoInicial,
            AbiertaEn = c.AbiertaEn,
            AbiertaPor = c.UsuarioApertura.Nombre,
            CerradaEn = c.CerradaEn,
            CerradaPor = c.UsuarioCierre != null ? c.UsuarioCierre.Nombre : null,
            EfectivoEsperado = c.EfectivoEsperado,
            EfectivoContado = c.EfectivoContado,
            Diferencia = c.Diferencia,
            NotaCierre = c.NotaCierre
        });

    private async Task<CajaDto> ProyectarAsync(int id, CancellationToken ct)
        => await Proyectar().FirstOrDefaultAsync(c => c.Id == id, ct)
           ?? throw new NoEncontradoException("La caja");
}