using System.Text.Json;
using Npgsql;

namespace Colibri.Api.Common;

/// <summary>
/// Captura las excepciones y devuelve siempre la misma forma de respuesta.
///
/// Traduce además los errores de PostgreSQL: como las reglas de negocio viven
/// en la base (CHECK, triggers, únicos), muchas validaciones llegan acá como
/// excepción de Npgsql y hay que convertirlas en un mensaje entendible en vez
/// de un 500 genérico.
/// </summary>
public class ManejadorExcepciones
{
    private readonly RequestDelegate _siguiente;
    private readonly ILogger<ManejadorExcepciones> _log;
    private readonly IHostEnvironment _entorno;

    public ManejadorExcepciones(RequestDelegate siguiente,
                                ILogger<ManejadorExcepciones> log,
                                IHostEnvironment entorno)
    {
        _siguiente = siguiente;
        _log = log;
        _entorno = entorno;
    }

    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await _siguiente(ctx);
        }
        catch (Exception ex)
        {
            await Responder(ctx, ex);
        }
    }

    private async Task Responder(HttpContext ctx, Exception ex)
    {
        var (codigo, mensaje) = Traducir(ex);

        if (codigo >= 500)
            _log.LogError(ex, "Error no controlado en {Ruta}", ctx.Request.Path);
        else
            _log.LogWarning("{Ruta}: {Mensaje}", ctx.Request.Path, mensaje);

        ctx.Response.StatusCode = codigo;
        ctx.Response.ContentType = "application/json";

        var cuerpo = ApiResponse<object>.Falla(
            mensaje,
            _entorno.IsDevelopment() && codigo >= 500 ? new[] { ex.ToString() } : null);

        await ctx.Response.WriteAsync(JsonSerializer.Serialize(cuerpo,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }

    private static (int, string) Traducir(Exception ex) => ex switch
    {
        ExcepcionNegocio n => (n.CodigoHttp, n.Message),
        UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "No autorizado."),
        PostgresException pg => TraducirPostgres(pg),
        _ => (StatusCodes.Status500InternalServerError, "Ocurrió un error inesperado.")
    };

    /// <summary>
    /// Convierte los códigos SQLSTATE en mensajes de negocio. Los nombres de
    /// restricción del esquema son descriptivos justamente para poder hacer esto.
    /// </summary>
    private static (int, string) TraducirPostgres(PostgresException pg) => pg.SqlState switch
    {
        // 23505 clave duplicada
        "23505" => (StatusCodes.Status409Conflict, pg.ConstraintName switch
        {
            "productos_codigo_key"    => "Ya existe un producto con ese código.",
            "clientes_rut_unico"      => "Ya hay un cliente registrado con ese RUT.",
            "usuarios_email_unico"    => "Ya existe una cuenta con ese correo.",
            "cajas_una_sola_abierta"  => "Ya hay una caja abierta. Ciérrala antes de abrir otra.",
            "ventas_folio_key"        => "El folio ya fue usado.",
            _ => "El registro ya existe."
        }),

        // 23514 violación de CHECK
        "23514" => (StatusCodes.Status400BadRequest, pg.ConstraintName switch
        {
            "productos_forma_simple" or "productos_forma_armado" =>
                "Un producto simple lleva costo y stock; uno armado lleva receta y unidades listas. No se pueden mezclar.",
            "ventas_total_cuadra" or "ventas_desglose_cuadra" or "ventas_descuento_cuadra" =>
                "Los totales de la boleta no cuadran.",
            "ventas_descuento_no_supera_bruto" =>
                "El descuento no puede superar el total de la boleta.",
            "ventas_efectivo_alcanza" =>
                "El efectivo recibido no alcanza para cubrir el total.",
            "clientes_puntos_no_negativos" =>
                "El cliente no tiene tantos puntos.",
            "cotizaciones_abono_no_supera_total" =>
                "El abono no puede superar el total del presupuesto.",
            "ventas_anulacion_completa" =>
                "Para anular una boleta hay que indicar el motivo.",
            _ => "Los datos no cumplen una regla del sistema."
        }),

        // 23503 clave foránea
        "23503" => (StatusCodes.Status400BadRequest,
            "El registro está referenciado por otro y no se puede modificar o eliminar."),

        // P0001 RAISE EXCEPTION de los triggers: el mensaje ya viene redactado
        "P0001" => (StatusCodes.Status400BadRequest, pg.MessageText),

        _ => (StatusCodes.Status500InternalServerError, "Error de base de datos.")
    };
}

public static class ManejadorExcepcionesExtensiones
{
    public static IApplicationBuilder UsarManejadorDeExcepciones(this IApplicationBuilder app) =>
        app.UseMiddleware<ManejadorExcepciones>();
}
