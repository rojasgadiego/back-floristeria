using System.Text.Json;
using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de COTIZACIONES (sql/05_cot_cotizaciones.sql).
///
/// `usuarioId` en las consultas es el filtro de privacidad: null para el
/// administrador, el id del vendedor para que vea solo lo que él creó. Lo
/// decide el endpoint desde el token.
/// </summary>
public class CotizacionesDAL
{
    private readonly IAccesoDatos _db;

    public CotizacionesDAL(IAccesoDatos db) => _db = db;

    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<IEnumerable<CotizacionResumen>> Listar(
        CotizacionFiltro f, int? usuarioId, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionResumen>(
            """
            SELECT * FROM sp_cot_c_cotizaciones(
                @Buscar::text, @Estado::text, @ClienteId::int, @SoloVencidas::boolean,
                @ProximosDias::int, @Desde::date, @Hasta::date, @UsuarioId::int,
                @Pagina::int, @Tamano::int)
            """,
            new
            {
                f.Buscar, f.Estado, f.ClienteId,
                SoloVencidas = f.SoloVencidas ?? false,
                f.ProximosDias, f.Desde, f.Hasta,
                UsuarioId = usuarioId,
                Pagina = f.PaginaReal, Tamano = f.TamanoReal
            }, ct);

    /// <summary>Null si no existe o si no es de ese vendedor.</summary>
    public async Task<CotizacionDetalle?> Cabecera(
        int id, int? usuarioId, CancellationToken ct = default)
        => await _db.ConsultarUno<CotizacionDetalle>(
            "SELECT * FROM sp_cot_c_cotizacion(@id::int, @usuarioId::int)",
            new { id, usuarioId }, ct);

    public async Task<IEnumerable<CotizacionItem>> Items(int id, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionItem>(
            "SELECT * FROM sp_cot_c_items(@id::int)", new { id }, ct);

    public async Task<IEnumerable<CotizacionFaltante>> Faltantes(int id, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionFaltante>(
            "SELECT * FROM sp_cot_c_faltantes(@id::int)", new { id }, ct);

    public async Task<IEnumerable<CotizacionCuota>> Cuotas(int id, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionCuota>(
            "SELECT * FROM sp_cot_c_cuotas(@id::int)", new { id }, ct);

    public async Task<IEnumerable<CotizacionPago>> Pagos(int id, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionPago>(
            "SELECT * FROM sp_cot_c_pagos(@id::int)", new { id }, ct);

    public async Task<CotizacionResultado?> Resultado(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<CotizacionResultado>(
            "SELECT * FROM sp_cot_c_resultado(@id::int)", new { id }, ct);

    public async Task<IEnumerable<LineaCobro>> LineasCobro(int id, CancellationToken ct = default)
        => await _db.ConsultarLista<LineaCobro>(
            "SELECT * FROM sp_cot_c_preparar_cobro(@id::int)", new { id }, ct);

    public async Task<IEnumerable<CotizacionResumen>> PorCobrar(
        int? usuarioId, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionResumen>(
            "SELECT * FROM sp_cot_c_por_cobrar(@usuarioId::int)", new { usuarioId }, ct);

    public async Task<IEnumerable<CotizacionResumen>> Agenda(
        int dias, int? usuarioId, CancellationToken ct = default)
        => await _db.ConsultarLista<CotizacionResumen>(
            "SELECT * FROM sp_cot_c_agenda(@dias::int, @usuarioId::int)",
            new { dias, usuarioId }, ct);

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<(int Id, string Error)> Crear(
        CotizacionRequest r, int usuarioId, CancellationToken ct = default)
        => await Escalar(
            """
            SELECT sp_cot_i_cotizacion(
                @ClienteId::int, @ClienteNombre::text, @TipoEvento::text, @FechaEvento::date,
                @Contacto::text, @Traslado::int, @Montaje::int, @Notas::text,
                @Items::jsonb, @UsuarioId::int)
            """,
            new
            {
                r.ClienteId, r.ClienteNombre, r.TipoEvento, r.FechaEvento, r.Contacto,
                r.Traslado, r.Montaje, r.Notas,
                Items = JsonSerializer.Serialize(r.Items, JsonCamel),
                UsuarioId = usuarioId
            }, ct);

    public async Task<(int Id, string Error)> Actualizar(
        int id, CotizacionRequest r, CancellationToken ct = default)
        => await Escalar(
            """
            SELECT sp_cot_u_cotizacion(
                @Id::int, @ClienteId::int, @ClienteNombre::text, @TipoEvento::text,
                @FechaEvento::date, @Contacto::text, @Traslado::int, @Montaje::int,
                @Notas::text, @Items::jsonb)
            """,
            new
            {
                Id = id, r.ClienteId, r.ClienteNombre, r.TipoEvento, r.FechaEvento,
                r.Contacto, r.Traslado, r.Montaje, r.Notas,
                Items = JsonSerializer.Serialize(r.Items, JsonCamel)
            }, ct);

    public async Task<(int Id, string Error)> Aprobar(int id, CancellationToken ct = default)
        => await Escalar("SELECT sp_cot_u_aprobar(@id::int)", new { id }, ct);

    public async Task<(int Id, string Error)> Anular(
        int id, string motivo, int usuarioId, CancellationToken ct = default)
        => await Escalar(
            "SELECT sp_cot_u_anular(@id::int, @motivo::text, @usuarioId::int)",
            new { id, motivo, usuarioId }, ct);

    public async Task<(int Id, string Error)> GuardarCuotas(
        int id, CuotasRequest r, CancellationToken ct = default)
        => await Escalar(
            "SELECT sp_cot_u_cuotas(@id::int, @cuotas::jsonb)",
            new { id, cuotas = JsonSerializer.Serialize(r.Cuotas, JsonCamel) }, ct);

    public async Task<(int Id, string Error)> GenerarCuotas(
        int id, GenerarCuotasRequest r, CancellationToken ct = default)
        => await Escalar(
            "SELECT sp_cot_i_generar_cuotas(@id::int, @Cantidad::int, @PrimerVencimiento::date, @CadaDias::int)",
            new { id, r.Cantidad, r.PrimerVencimiento, r.CadaDias }, ct);

    /// <summary>
    /// Los alias son necesarios: el SP devuelve las columnas con prefijo o_ y
    /// Dapper las dejaría todas en cero sin avisar.
    /// </summary>
    public async Task<ResultadoOp<ResultadoPagoCotizacion>> RegistrarPago(
        int id, PagoCotizacionRequest r, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var res = await _db.ConsultarUno<ResultadoPagoCotizacion>(
                """
                SELECT o_pago_id     AS pago_id,
                       o_venta_id    AS venta_id,
                       o_venta_folio AS venta_folio,
                       o_monto       AS monto,
                       o_vuelto      AS vuelto,
                       o_saldo       AS saldo
                FROM sp_cot_i_pago(@id::int, @Monto::int, @MedioPago::text,
                                   @Recibido::int, @Notas::text, @usuarioId::int)
                """,
                new { id, r.Monto, r.MedioPago, r.Recibido, r.Notas, usuarioId }, ct);

            return res is null
                ? ResultadoOp<ResultadoPagoCotizacion>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoPagoCotizacion>.Exito(res);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoPagoCotizacion>.Error(ErroresPg.Mensaje(ex));
        }
    }

    public async Task<ResultadoOp<ResultadoAnulacionPago>> AnularPago(
        int id, int pagoId, string motivo, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var res = await _db.ConsultarUno<ResultadoAnulacionPago>(
                """
                SELECT o_pago_id     AS pago_id,
                       o_venta_folio AS venta_folio,
                       o_monto       AS monto
                FROM sp_cot_u_anular_pago(@id::int, @pagoId::int, @motivo::text, @usuarioId::int)
                """,
                new { id, pagoId, motivo, usuarioId }, ct);

            return res is null
                ? ResultadoOp<ResultadoAnulacionPago>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoAnulacionPago>.Exito(res);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoAnulacionPago>.Error(ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>"" = salió bien; texto = el mensaje del RAISE, tal cual.</summary>
    private async Task<(int Id, string Error)> Escalar(string sql, object p, CancellationToken ct)
    {
        try
        {
            return (await _db.Escalar<int>(sql, p, ct), string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }
}
