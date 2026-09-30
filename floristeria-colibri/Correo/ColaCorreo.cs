using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace Colibri.Api.Correo;

/// <summary>
/// Configuración del envío. En el VPS va por Environment:
///
///   Correo__ApiKey=re_...                       ← sin esto no se envía nada
///   Correo__Remitente=Floristería Colibrí &lt;avisos@floristeriacolibri.cl&gt;
///   Correo__ResponderA=contacto@floristeriacolibri.cl
///   Correo__Avisos__0=contacto@floristeriacolibri.cl
///
/// Sin ApiKey la API funciona igual: los avisos se descartan con un log.
/// Así se puede desplegar el código antes de tener la clave.
/// </summary>
public class CorreoOpciones
{
    public string? ApiKey { get; set; }
    public string Remitente { get; set; } = "Floristería Colibrí <avisos@floristeriacolibri.cl>";
    public string? ResponderA { get; set; } = "contacto@floristeriacolibri.cl";

    /// <summary>Quién recibe los avisos internos: compras, cierres, alertas.</summary>
    public string[] Avisos { get; set; } = [];

    public bool Activo => !string.IsNullOrWhiteSpace(ApiKey);
}

public sealed record MensajeCorreo(string[] Para, string Asunto, string Html, string? ResponderA = null);

/// <summary>
/// Un aviso pendiente. No lleva el correo armado sino la receta para armarlo:
/// así la venta solo encola un id y sigue, y las consultas para el detalle
/// (boleta, cliente, datos del local) corren después, fuera del request.
/// </summary>
public sealed record TrabajoCorreo(
    string Nombre,
    Func<IServiceProvider, CancellationToken, Task<IEnumerable<MensajeCorreo>>> Armar);

/// <summary>
/// La fila de avisos. Encolar nunca espera ni falla: si por algo se
/// acumularan 500 pendientes, se pierden los más viejos antes que frenar
/// una venta.
/// </summary>
public sealed class ColaCorreo
{
    private readonly Channel<TrabajoCorreo> _canal = Channel.CreateBounded<TrabajoCorreo>(
        new BoundedChannelOptions(500)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true
        });

    public void Encolar(
        string nombre,
        Func<IServiceProvider, CancellationToken, Task<IEnumerable<MensajeCorreo>>> armar)
        => _canal.Writer.TryWrite(new TrabajoCorreo(nombre, armar));

    internal ChannelReader<TrabajoCorreo> Lector => _canal.Reader;
}

/// <summary>
/// Saca los avisos de la fila de a uno, los arma en su propio scope y los
/// manda por la API de Resend. Un error acá queda en el log y nada más: el
/// correo es un extra, la venta ya está guardada.
/// </summary>
public sealed class EnvioCorreoWorker : BackgroundService
{
    private static readonly TimeSpan[] Esperas = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(15)];

    private readonly ColaCorreo _cola;
    private readonly IServiceProvider _sp;
    private readonly IHttpClientFactory _http;
    private readonly CorreoOpciones _op;
    private readonly ILogger<EnvioCorreoWorker> _log;

    public EnvioCorreoWorker(
        ColaCorreo cola, IServiceProvider sp, IHttpClientFactory http,
        IOptions<CorreoOpciones> op, ILogger<EnvioCorreoWorker> log)
    {
        _cola = cola;
        _sp = sp;
        _http = http;
        _op = op.Value;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        if (!_op.Activo)
            _log.LogWarning("Correo desactivado: falta Correo__ApiKey. Los avisos se descartan.");

        await foreach (var trabajo in _cola.Lector.ReadAllAsync(ct))
        {
            if (!_op.Activo) continue;

            List<MensajeCorreo> mensajes;
            try
            {
                using var scope = _sp.CreateScope();
                mensajes = (await trabajo.Armar(scope.ServiceProvider, ct)).ToList();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo armar el aviso {Aviso}", trabajo.Nombre);
                continue;
            }

            foreach (var m in mensajes.Where(m => m.Para.Length > 0))
            {
                await Enviar(m, trabajo.Nombre, ct);

                // Resend acepta 2 envíos por segundo en el plan gratis.
                await Task.Delay(600, ct);
            }
        }
    }

    private async Task Enviar(MensajeCorreo m, string aviso, CancellationToken ct)
    {
        var cuerpo = new Dictionary<string, object>
        {
            ["from"] = _op.Remitente,
            ["to"] = m.Para,
            ["subject"] = m.Asunto,
            ["html"] = m.Html
        };
        var responder = m.ResponderA ?? _op.ResponderA;
        if (!string.IsNullOrWhiteSpace(responder)) cuerpo["reply_to"] = responder;

        for (var intento = 0; ; intento++)
        {
            try
            {
                var cliente = _http.CreateClient("resend");
                using var req = new HttpRequestMessage(HttpMethod.Post, "emails")
                {
                    Content = JsonContent.Create(cuerpo)
                };
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _op.ApiKey);

                using var res = await cliente.SendAsync(req, ct);
                if (res.IsSuccessStatusCode)
                {
                    _log.LogInformation("Aviso {Aviso} enviado: {Asunto}", aviso, m.Asunto);
                    return;
                }

                var detalle = await res.Content.ReadAsStringAsync(ct);

                // Un 4xx (clave mala, dominio sin verificar, correo inválido)
                // no se arregla reintentando. Solo el 429 y los 5xx.
                var reintentable = (int)res.StatusCode == 429 || (int)res.StatusCode >= 500;
                if (!reintentable || intento >= Esperas.Length)
                {
                    _log.LogError("Resend rechazó el aviso {Aviso} ({Status}): {Detalle}",
                        aviso, (int)res.StatusCode, detalle);
                    return;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException && intento < Esperas.Length)
            {
                _log.LogWarning("Falló el envío del aviso {Aviso}, se reintenta: {Msg}", aviso, ex.Message);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "No se pudo enviar el aviso {Aviso}", aviso);
                return;
            }

            await Task.Delay(Esperas[intento], ct);
        }
    }
}
