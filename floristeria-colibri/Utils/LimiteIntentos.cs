using System.Collections.Concurrent;

namespace Colibri.Api.Utils;

/// <summary>
/// Cuenta los intentos fallidos por clave ("login:correo", "codigo-merma:7")
/// y bloquea la clave un rato cuando son demasiados.
///
/// Sin esto, una contraseña o un código de 6 dígitos se podían probar sin
/// freno. Por clave y no por IP: detrás de Traefik todas las peticiones
/// llegan desde la misma IP, y bloquearla dejaría afuera a todo el local.
///
/// Vive en memoria: hay una sola instancia de la API, y si se reinicia, lo
/// peor que pasa es que alguien recupera sus cinco intentos.
/// </summary>
public class LimiteIntentos
{
    public const int MaxFallos = 5;
    public static readonly TimeSpan Ventana = TimeSpan.FromMinutes(15);

    private sealed record Estado(int Fallos, DateTime Desde, DateTime? BloqueadoHasta);

    private readonly ConcurrentDictionary<string, Estado> _estados = new();

    /// <summary>Null si puede intentar; si no, cuánto le falta.</summary>
    public TimeSpan? Bloqueado(string clave)
    {
        if (!_estados.TryGetValue(clave, out var e) || e.BloqueadoHasta is not { } hasta)
            return null;

        var resta = hasta - DateTime.UtcNow;
        if (resta > TimeSpan.Zero) return resta;

        _estados.TryRemove(clave, out _);
        return null;
    }

    public void Fallo(string clave)
    {
        var ahora = DateTime.UtcNow;

        _estados.AddOrUpdate(clave,
            _ => new Estado(1, ahora, null),
            (_, e) =>
            {
                // Pasada la ventana, los fallos viejos ya no cuentan.
                var fallos = ahora - e.Desde > Ventana ? 1 : e.Fallos + 1;
                var desde = fallos == 1 ? ahora : e.Desde;
                return new Estado(fallos, desde, fallos >= MaxFallos ? ahora + Ventana : null);
            });

        // Que el diccionario no crezca para siempre con claves olvidadas.
        if (_estados.Count > 5000)
            foreach (var (k, e) in _estados)
                if (ahora - e.Desde > Ventana && (e.BloqueadoHasta ?? DateTime.MinValue) < ahora)
                    _estados.TryRemove(k, out _);
    }

    public void Exito(string clave) => _estados.TryRemove(clave, out _);

    /// <summary>"Espera 12 minutos", redondeado hacia arriba.</summary>
    public static string Mensaje(TimeSpan resta)
        => $"Demasiados intentos fallidos. Espera {Math.Max(1, (int)Math.Ceiling(resta.TotalMinutes))} minuto(s) y vuelve a intentar.";
}
