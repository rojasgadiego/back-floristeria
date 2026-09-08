using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de ABASTECIMIENTO: proveedores, presentaciones y
/// compras. La parte de lotes y QR vive en AbastecimientoDAL.Lotes.cs.
///
/// `partial` porque el módulo tiene dos mitades bien distintas: el papeleo
/// de la compra y la generación de etiquetas. Juntas pasarían las 400
/// líneas y nadie encuentra nada.
///
/// Contrato de escritura, el mismo de siempre:
///   "" = salió bien · texto = error de negocio que se muestra tal cual
/// </summary>
public partial class AbastecimientoDAL
{
    private readonly IAccesoDatos _db;

    public AbastecimientoDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Proveedores
    // ============================================================

    public async Task<IEnumerable<Proveedor>> ConsultarProveedores(
        ProveedorFiltro f, CancellationToken ct = default)
        => await _db.ConsultarLista<Proveedor>(
            """
            SELECT * FROM sp_abs_c_proveedores(
                @Buscar::text, @Activo::boolean, @Pagina::int, @Tamano::int)
            """,
            new { f.Buscar, f.Activo, Pagina = f.PaginaReal, Tamano = f.TamanoReal }, ct);

    public async Task<Proveedor?> ConsultarProveedor(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Proveedor>(
            "SELECT * FROM sp_abs_c_proveedor(@id)", new { id }, ct);

    public async Task<(int Id, string Error)> InsertarProveedor(
        ProveedorRequest r, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_abs_i_proveedor(
                    @Nombre, @Rut, @Contacto, @Telefono, @Correo, @Direccion, @Notas)
                """,
                r, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> ActualizarProveedor(
        int id, ProveedorRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_abs_u_proveedor(
                    @id, @Nombre, @Rut, @Contacto, @Telefono, @Correo, @Direccion, @Notas)
                """,
                new { id, r.Nombre, r.Rut, r.Contacto, r.Telefono, r.Correo, r.Direccion, r.Notas }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarEstadoProveedor(
        int id, bool activo, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_abs_u_proveedor_estado(@id, @activo)", new { id, activo }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    // ============================================================
    // Presentaciones
    // ============================================================

    public async Task<IEnumerable<Presentacion>> ConsultarPresentaciones(
        int? productoId, bool? activa, CancellationToken ct = default)
        => await _db.ConsultarLista<Presentacion>(
            "SELECT * FROM sp_abs_c_presentaciones(@productoId::int, @activa::boolean)",
            new { productoId, activa }, ct);

    public async Task<(int Id, string Error)> InsertarPresentacion(
    int productoId, PresentacionRequest r, CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("ProductoId", productoId);
            p.Add("Nombre", r.Nombre);
            // El enum va como texto: el cast explícito del SQL evita que
            // Npgsql tenga que adivinar el tipo.
            p.Add("Tipo", r.Tipo.ToString());
            p.Add("Paquetes", r.Paquetes);
            p.Add("VarasPorPaquete", r.VarasPorPaquete);
            p.Add("Predeterminada", r.Predeterminada);

            var id = await _db.Escalar<int>(
                """
                SELECT sp_abs_i_presentacion(
                    @ProductoId::int, @Nombre::text, @Tipo::tipo_presentacion,
                    @Paquetes::int, @VarasPorPaquete::int, @Predeterminada::boolean)
                """,
                p, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }
    public async Task<string> ActualizarPresentacion(
        int id, PresentacionRequest r, CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("id", id);
            p.Add("Nombre", r.Nombre);
            p.Add("Tipo", r.Tipo.ToString());
            p.Add("Paquetes", r.Paquetes);
            p.Add("VarasPorPaquete", r.VarasPorPaquete);
            p.Add("Predeterminada", r.Predeterminada);

            await _db.Escalar<int>(
                """
                SELECT sp_abs_u_presentacion(
                    @id::int, @Nombre::text, @Tipo::tipo_presentacion,
                    @Paquetes::int, @VarasPorPaquete::int, @Predeterminada::boolean)
                """,
                p, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<string> CambiarEstadoPresentacion(
        int id, bool activa, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_abs_u_presentacion_estado(@id, @activa)", new { id, activa }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    // ============================================================
    // Compras
    // ============================================================

    public async Task<IEnumerable<Compra>> ConsultarCompras(
        CompraFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("ProveedorId", f.ProveedorId);
        // Enum nulable: va como texto o Dapper lo manda como integer y
        // Postgres no convierte integer a estado_compra.
        p.Add("Estado", f.Estado?.ToString());
        p.Add("Desde", f.Desde);
        p.Add("Hasta", f.Hasta);
        p.Add("Pagina", f.PaginaReal);
        p.Add("Tamano", f.TamanoReal);

        return await _db.ConsultarLista<Compra>(
            """
            SELECT * FROM sp_abs_c_compras(
                @Buscar::text, @ProveedorId::int, @Estado::estado_compra,
                @Desde::date, @Hasta::date, @Pagina::int, @Tamano::int)
            """,
            p, ct);
    }

    public async Task<Compra?> ConsultarCompra(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Compra>(
            "SELECT * FROM sp_abs_c_compra(@id)", new { id }, ct);

    public async Task<IEnumerable<CompraItem>> ConsultarCompraItems(
        int compraId, CancellationToken ct = default)
        => await _db.ConsultarLista<CompraItem>(
            "SELECT * FROM sp_abs_c_compra_items(@compraId)", new { compraId }, ct);

    public async Task<(int Id, string Error)> InsertarCompra(
        CrearCompraRequest r, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_abs_i_compra(
                    @ProveedorId::int, @Fecha::date, @Documento::text,
                    @Notas::text, @usuarioId::int)
                """,
                new { r.ProveedorId, r.Fecha, r.Documento, r.Notas, usuarioId }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> ActualizarCompra(
        int id, ActualizarCompraRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_abs_u_compra(
                    @id::int, @ProveedorId::int, @Fecha::date,
                    @Documento::text, @Notas::text)
                """,
                new { id, r.ProveedorId, r.Fecha, r.Documento, r.Notas }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<(int Id, string Error)> InsertarCompraItem(
        int compraId, CompraItemRequest r, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_abs_i_compra_item(
                    @compraId::int, @ProductoId::int, @PresentacionId::int,
                    @Cantidad::int, @CostoUnitario::int)
                """,
                new { compraId, r.ProductoId, r.PresentacionId, r.Cantidad, r.CostoUnitario }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> EliminarCompraItem(int id, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>("SELECT sp_abs_d_compra_item(@id)", new { id }, ct);
            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    /// <summary>
    /// sp_abs_u_compra_recibir — la puerta de entrada al inventario.
    ///
    /// Genera los lotes, sube el stock y deja los movimientos, todo en una
    /// transacción. Devuelve cuántos lotes salieron, que es cuántas
    /// etiquetas hay que imprimir.
    /// </summary>
    public async Task<ResultadoOp<ResultadoRecepcion>> RecibirCompra(
        int compraId, int usuarioId, DateOnly? fechaIngreso, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoRecepcion>(
                """
                SELECT o_compra_id        AS compra_id,
                       o_folio            AS folio,
                       o_lotes_generados  AS lotes_generados,
                       o_varas_ingresadas AS varas_ingresadas,
                       o_productos        AS productos
                FROM sp_abs_u_compra_recibir(@compraId::int, @usuarioId::int, @fechaIngreso::date)
                """,
            new { compraId, usuarioId, fechaIngreso }, ct);

            return r is null
                ? ResultadoOp<ResultadoRecepcion>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoRecepcion>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoRecepcion>.Error(ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> AnularCompra(
        int compraId, int usuarioId, string? motivo, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_abs_u_compra_anular(@compraId, @usuarioId, @motivo)",
                new { compraId, usuarioId, motivo }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }
}
