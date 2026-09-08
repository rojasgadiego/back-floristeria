using System.Text.Json;
using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de VENTAS.
///
/// El cobro entero ocurre dentro de sp_ven_i_venta: descuenta partidas,
/// escribe líneas y consumos, calcula descuentos y mueve puntos en una sola
/// transacción. Partirlo en varias llamadas desde C# abriría la puerta a una
/// boleta cobrada con el stock sin descontar.
/// </summary>
public class VentasDAL
{
    private readonly IAccesoDatos _db;

    public VentasDAL(IAccesoDatos db) => _db = db;

    /// <summary>
    /// camelCase a propósito: el SP lee 'productoId' y 'partida' del JSONB.
    /// </summary>
    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ============================================================
    // Cobro
    // ============================================================

        /// <summary>
    /// El cobro entero ocurre dentro de sp_ven_i_venta: descuenta partidas,
    /// escribe líneas y consumos, calcula descuentos y mueve puntos en una
    /// sola transacción.
    ///
    /// `autorizadoPor` es el NOMBRE de quien autorizó el descuento, no su id:
    /// la columna es text y guarda el nombre para que quede congelado en la
    /// boleta aunque esa persona después se renombre o se desactive.
    /// </summary>
    public async Task<(int Id, string Error)> Registrar(
        RegistrarVentaRequest r, int usuarioId, string? autorizadoPor,
        CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("Items", JsonSerializer.Serialize(r.Items, JsonCamel));
            p.Add("MedioPago", r.MedioPago.ToString());
            p.Add("UsuarioId", usuarioId);
            p.Add("ClienteId", r.ClienteId);
            p.Add("PromocionId", r.PromocionId);
            p.Add("DescuentoManual", r.DescuentoManual);
            p.Add("PuntosCanjeados", r.PuntosCanjeados);
            p.Add("Recibido", r.Recibido);
            p.Add("AutorizadoPor", autorizadoPor);
            p.Add("CotizacionId", r.CotizacionId);

            var id = await _db.Escalar<int>(
                """
                SELECT sp_ven_i_venta(
                    @Items::jsonb, @MedioPago::medio_pago, @UsuarioId::int,
                    @ClienteId::int, @PromocionId::int, @DescuentoManual::int,
                    @PuntosCanjeados::int, @Recibido::int, @AutorizadoPor::text,
                    @CotizacionId::int)
                """,
                p, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<ResultadoOp<ResultadoAnulacion>> Anular(
        int ventaId, string motivo, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoAnulacion>(
                """
                SELECT o_venta_id AS venta_id,
                       o_folio    AS folio,
                       o_devuelto AS devuelto,
                       o_puntos   AS puntos
                FROM sp_ven_u_venta_anular(@ventaId, @motivo, @usuarioId)
                """,
                new { ventaId, motivo, usuarioId }, ct);

            return r is null
                ? ResultadoOp<ResultadoAnulacion>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoAnulacion>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoAnulacion>.Error(ErroresPg.Mensaje(ex));
        }
    }

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<IEnumerable<Venta>> ConsultarVentas(
        VentaFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("UsuarioId", f.UsuarioId);
        p.Add("ClienteId", f.ClienteId);
        // Enum nulable: va como texto o Dapper lo manda como integer y
        // Postgres no convierte integer a medio_pago.
        p.Add("MedioPago", f.MedioPago?.ToString());
        p.Add("Desde", f.Desde);
        p.Add("Hasta", f.Hasta);
        p.Add("Anuladas", f.IncluirAnuladas ?? false);
        p.Add("Pagina", f.PaginaReal);
        p.Add("Tamano", f.TamanoReal);

        return await _db.ConsultarLista<Venta>(
            """
            SELECT * FROM sp_ven_c_ventas(
                @Buscar::text, @UsuarioId::int, @ClienteId::int,
                @MedioPago::medio_pago, @Desde::date, @Hasta::date,
                @Anuladas::boolean, @Pagina::int, @Tamano::int)
            """,
            p, ct);
    }

    public async Task<VentaDetalle?> ConsultarVenta(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<VentaDetalle>(
            "SELECT * FROM sp_ven_c_venta(@id)", new { id }, ct);

    public async Task<IEnumerable<VentaItem>> ConsultarItems(
        int ventaId, CancellationToken ct = default)
        => await _db.ConsultarLista<VentaItem>(
            "SELECT * FROM sp_ven_c_venta_items(@ventaId)", new { ventaId }, ct);

    public async Task<IEnumerable<VentaConsumo>> ConsultarConsumos(
        int ventaId, CancellationToken ct = default)
        => await _db.ConsultarLista<VentaConsumo>(
            "SELECT * FROM sp_ven_c_venta_consumos(@ventaId)", new { ventaId }, ct);

    public async Task<IEnumerable<PromocionAplicable>> PromocionesAplicables(
        IEnumerable<object> items, CancellationToken ct = default)
        => await _db.ConsultarLista<PromocionAplicable>(
            "SELECT * FROM sp_ven_c_promociones_aplicables(@items::jsonb)",
            new { items = JsonSerializer.Serialize(items, JsonCamel) }, ct);

    /// <summary>
    /// Para el ticket: los datos del local y la leyenda salen de
    /// `configuracion`, no de constantes en el código. Cambiar el teléfono
    /// del local no debería requerir un despliegue.
    /// </summary>
    public async Task<string?> ConsultarConfig(string clave, CancellationToken ct = default)
        => await _db.Escalar<string>(
            "SELECT valor::text FROM configuracion WHERE clave = @clave", new { clave }, ct);
}
