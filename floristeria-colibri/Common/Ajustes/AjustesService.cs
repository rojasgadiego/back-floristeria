using Colibri.Api.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Text.Json;

namespace Colibri.Api.Common.Ajustes;

public class AjustesService : IAjustesService
{
    // Un minuto: suficiente para no consultar la base en cada venta, y corto
    // como para que un cambio de configuración se note casi de inmediato.
    private static readonly TimeSpan Vigencia = TimeSpan.FromMinutes(1);
    private const string Prefijo = "ajustes:";

    private static readonly JsonSerializerOptions Opciones = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly ColibriDbContext _db;
    private readonly IMemoryCache _cache;
    private readonly ILogger<AjustesService> _log;

    public AjustesService(ColibriDbContext db, IMemoryCache cache, ILogger<AjustesService> log)
    {
        _db = db;
        _cache = cache;
        _log = log;
    }

    public Task<AjustesLocal> LocalAsync(CancellationToken ct = default)
        => LeerAsync<AjustesLocal>("local", ct);

    public Task<AjustesTicket> TicketAsync(CancellationToken ct = default)
        => LeerAsync<AjustesTicket>("ticket", ct);

    public Task<AjustesVenta> VentaAsync(CancellationToken ct = default)
        => LeerAsync<AjustesVenta>("venta", ct);

    public Task<AjustesClub> ClubAsync(CancellationToken ct = default)
        => LeerAsync<AjustesClub>("club", ct);

    public void Invalidar()
    {
        foreach (var clave in new[] { "local", "ticket", "venta", "club" })
            _cache.Remove(Prefijo + clave);
    }

    private async Task<T> LeerAsync<T>(string clave, CancellationToken ct) where T : new()
    {
        if (_cache.TryGetValue<T>(Prefijo + clave, out var guardado) && guardado is not null)
            return guardado;

        var json = await _db.Configuraciones.AsNoTracking()
            .Where(c => c.Clave == clave)
            .Select(c => c.Valor)
            .FirstOrDefaultAsync(ct);

        T valor;
        if (string.IsNullOrWhiteSpace(json))
        {
            // Falta la fila: se usan los valores por defecto en vez de fallar.
            // Una configuración incompleta no debe impedir vender.
            valor = new T();
        }
        else
        {
            try
            {
                valor = JsonSerializer.Deserialize<T>(json, Opciones) ?? new T();
            }
            catch (JsonException ex)
            {
                _log.LogError(ex, "La configuración «{Clave}» tiene un JSON inválido", clave);
                valor = new T();
            }
        }

        _cache.Set(Prefijo + clave, valor, Vigencia);
        return valor;
    }
}