using System.Text.Json;
using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Ajustes;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Common.Validacion;
using Colibri.Api.Context;
using Colibri.Api.Features.Configuracion.Dtos;

using EntidadConfiguracion = Colibri.Api.Domain.Entities.Configuracion;

namespace Colibri.Api.Features.Configuracion;

public class ConfiguracionService : IConfiguracionService
{
    // camelCase para que coincida con la semilla y con lo que AjustesService
    // espera al leer. Cambiarlo dejaría la configuración existente sin leer.
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly ColibriDbContext _db;
    private readonly IAjustesService _ajustes;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<ConfiguracionService> _log;

    public ConfiguracionService(
        ColibriDbContext db,
        IAjustesService ajustes,
        IUsuarioActual usuarioActual,
        ILogger<ConfiguracionService> log)
    {
        _db = db;
        _ajustes = ajustes;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       LECTURA
       ================================================================== */

    public async Task<ConfiguracionDto> ObtenerAsync(CancellationToken ct = default)
    {
        var filas = await _db.Configuraciones.AsNoTracking()
            .Select(c => new
            {
                c.Clave,
                c.Valor,
                c.ActualizadoEn,
                Autor = c.UsuarioActualizo != null ? c.UsuarioActualizo.Nombre : null
            })
            .ToListAsync(ct);

        var ultima = filas.OrderByDescending(f => f.ActualizadoEn).FirstOrDefault();

        return new ConfiguracionDto
        {
            Local = Leer<AjustesLocalDto>(filas.FirstOrDefault(f => f.Clave == "local")?.Valor),
            Ticket = Leer<AjustesTicketDto>(filas.FirstOrDefault(f => f.Clave == "ticket")?.Valor),
            Venta = Leer<AjustesVentaDto>(filas.FirstOrDefault(f => f.Clave == "venta")?.Valor),
            Club = Leer<AjustesClubDto>(filas.FirstOrDefault(f => f.Clave == "club")?.Valor),
            ActualizadoEn = ultima?.ActualizadoEn,
            ActualizadoPor = ultima?.Autor
        };
    }

    /* ==================================================================
       ESCRITURA
       ================================================================== */

    public async Task<AjustesLocalDto> GuardarLocalAsync(
            AjustesLocalDto peticion, CancellationToken ct = default)
    {
        // El RUT del local aparece en cada ticket: uno mal escrito se imprime
        // cientos de veces antes de que alguien lo note.
        if (!string.IsNullOrWhiteSpace(peticion.Rut))
        {
            if (!Rut.EsValido(peticion.Rut))
            {
                var plano = Rut.Normalizar(peticion.Rut);
                var pista = plano.Length >= 2 && plano[..^1].All(char.IsDigit)
                    ? $" El dígito verificador debería ser {Rut.CalcularDv(plano[..^1])}."
                    : string.Empty;

                throw new ExcepcionNegocio($"El RUT del local no es válido.{pista}");
            }

            peticion.Rut = Rut.Formatear(peticion.Rut);
        }

        peticion.Nombre = peticion.Nombre.Trim();
        await GuardarAsync("local", peticion, ct);

        return peticion;
    }

    public async Task<AjustesTicketDto> GuardarTicketAsync(
        AjustesTicketDto peticion, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(peticion.Leyenda))
        {
            throw new ExcepcionNegocio(
                "La leyenda del ticket no puede quedar vacía: es lo que deja claro " +
                "que el documento no es tributario.");
        }

        await GuardarAsync("ticket", peticion, ct);
        return peticion;
    }

    public async Task<AjustesVentaDto> GuardarVentaAsync(
            AjustesVentaDto peticion, CancellationToken ct = default)
    {
        var anterior = await _ajustes.VentaAsync(ct);

        await GuardarAsync("venta", peticion, ct);

        // Cambiar el IVA no reescribe nada: cada boleta guardó su tasa. Se
        // registra igual porque afecta todas las ventas desde ahora.
        if (anterior.Iva != peticion.Iva)
        {
            _log.LogWarning(
                "IVA cambiado de {Antes}% a {Ahora}% por {Autor}. Las boletas ya " +
                "emitidas conservan su tasa original.",
                anterior.Iva, peticion.Iva, _usuarioActual.Email);
        }

        // Este umbral es el que decide cuándo un descuento necesita
        // autorización. Subirlo es el atajo más rentable para vaciar la caja.
        if (anterior.DescuentoSinAutorizacion != peticion.DescuentoSinAutorizacion)
        {
            _log.LogWarning(
                "Umbral de descuento sin autorización cambiado de {Antes} a {Ahora} " +
                "por {Autor}",
                anterior.DescuentoSinAutorizacion, peticion.DescuentoSinAutorizacion,
                _usuarioActual.Email);
        }

        return peticion;
    }

    /// <summary>
    /// Guarda el club de puntos.
    ///
    /// Cambiar el valor del punto no es ajustar una preferencia: revalúa de
    /// inmediato todos los saldos vigentes, que son un compromiso con los
    /// clientes. Por eso exige confirmación explícita cuando hay puntos en
    /// circulación.
    /// </summary>
    public async Task<AjustesClubDto> GuardarClubAsync(GuardarClubRequest peticion, CancellationToken ct = default)
    {
        if (peticion.CanjeMinimo > 0 && peticion.ValorPunto > 0)
        {
            // Un canje mínimo mayor a lo que la gente acumula en una compra
            // hace que el club se vea como una promesa que nunca se cumple
            var minimoEnPesos = peticion.CanjeMinimo * peticion.ValorPunto;
            if (minimoEnPesos > 50000)
            {
                _log.LogWarning(
                    "El canje mínimo quedó en {Puntos} puntos ({Pesos} en pesos): " +
                    "puede ser inalcanzable para el cliente típico.",
                    peticion.CanjeMinimo, minimoEnPesos);
            }
        }

        var anterior = await _ajustes.ClubAsync(ct);

        if (anterior.ValorPunto != peticion.ValorPunto)
        {
            var impacto = await ImpactoClubAsync(peticion.ValorPunto, ct);

            if (impacto.PuntosEnCirculacion > 0 && !peticion.ConfirmaRevaluacion)
            {
                throw new ExcepcionNegocio(
                    $"{impacto.Advertencia} Si estás de acuerdo, vuelve a enviar la " +
                    "petición con confirmaRevaluacion en true.");
            }

            if (impacto.PuntosEnCirculacion > 0)
            {
                _log.LogWarning(
                    "Valor del punto cambiado de {Antes} a {Ahora} por {Autor}. " +
                    "Revalúa {Puntos} puntos: el compromiso pasa de {Actual} a {Nuevo}.",
                    anterior.ValorPunto, peticion.ValorPunto, _usuarioActual.Email,
                    impacto.PuntosEnCirculacion, impacto.CompromisoActual,
                    impacto.CompromisoNuevo);
            }
        }

        if (anterior.Activo && !peticion.Activo)
        {
            var pendientes = await _db.Clientes.AsNoTracking()
                .Where(c => c.Activo && c.Puntos > 0)
                .SumAsync(c => (long?)c.Puntos, ct) ?? 0;

            if (pendientes > 0)
            {
                _log.LogWarning(
                    "Club desactivado por {Autor} con {Puntos} puntos sin canjear. " +
                    "Los clientes no podrán usarlos mientras siga apagado.",
                    _usuarioActual.Email, pendientes);
            }
        }

        // El DTO base es lo que se guarda: ConfirmaRevaluacion es de la
        // petición, no de la configuración.
        var club = new AjustesClubDto
        {
            Activo = peticion.Activo,
            PuntosPorPeso = peticion.PuntosPorPeso,
            ValorPunto = peticion.ValorPunto,
            CanjeMinimo = peticion.CanjeMinimo
        };

        await GuardarAsync("club", club, ct);
        return club;
    }

    public async Task<ImpactoClubDto> ImpactoClubAsync(
        int valorPuntoNuevo, CancellationToken ct = default)
    {
        var club = await _ajustes.ClubAsync(ct);

        var saldos = await _db.Clientes.AsNoTracking()
            .Where(c => c.Activo && c.Puntos > 0)
            .Select(c => c.Puntos)
            .ToListAsync(ct);

        var puntos = saldos.Sum(p => (long)p);
        var actual = puntos * club.ValorPunto;
        var nuevo = puntos * valorPuntoNuevo;
        var diferencia = nuevo - actual;

        string? advertencia = null;

        if (puntos > 0 && diferencia != 0)
        {
            advertencia = diferencia < 0
                ? $"Hay {puntos:N0} puntos en circulación de {saldos.Count} cliente(s). " +
                  $"Bajar el valor de ${club.ValorPunto} a ${valorPuntoNuevo} reduce lo que " +
                  $"valen de ${actual:N0} a ${nuevo:N0}: los clientes recibirán " +
                  $"${Math.Abs(diferencia):N0} menos de lo que esperaban."
                : $"Hay {puntos:N0} puntos en circulación de {saldos.Count} cliente(s). " +
                  $"Subir el valor de ${club.ValorPunto} a ${valorPuntoNuevo} aumenta el " +
                  $"compromiso de ${actual:N0} a ${nuevo:N0}: son ${diferencia:N0} más " +
                  "en descuentos por entregar.";
        }

        return new ImpactoClubDto
        {
            ClientesConPuntos = saldos.Count,
            PuntosEnCirculacion = puntos,
            CompromisoActual = actual,
            CompromisoNuevo = nuevo,
            Diferencia = diferencia,
            Advertencia = advertencia
        };
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    private async Task GuardarAsync<T>(string clave, T valor, CancellationToken ct)
    {
        var fila = await _db.Configuraciones.FirstOrDefaultAsync(c => c.Clave == clave, ct);
        var json = JsonSerializer.Serialize(valor, Json);

        if (fila is null)
        {
            _db.Configuraciones.Add(new EntidadConfiguracion
            {
                Clave = clave,
                Valor = json,
                ActualizadoPor = _usuarioActual.Id
            });
        }
        else
        {
            fila.Valor = json;
            fila.ActualizadoPor = _usuarioActual.Id;
        }

        await _db.SaveChangesAsync(ct);

        // Sin esto, el cambio no se vería hasta que expire la caché de un
        // minuto: la siguiente venta seguiría usando el IVA anterior.
        _ajustes.Invalidar();

        _log.LogInformation("Configuración «{Clave}» guardada por {Autor}",
            clave, _usuarioActual.Email);
    }

    /// <summary>
    /// Lee una sección. Si falta o tiene JSON inválido devuelve los valores
    /// por defecto: una configuración incompleta no debe impedir operar.
    /// </summary>
    private T Leer<T>(string? json) where T : new()
    {
        if (string.IsNullOrWhiteSpace(json)) return new T();

        try
        {
            return JsonSerializer.Deserialize<T>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new T();
        }
        catch (JsonException ex)
        {
            _log.LogError(ex, "La configuración de tipo {Tipo} tiene un JSON inválido",
                typeof(T).Name);
            return new T();
        }
    }
}