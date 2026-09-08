using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de INVENTARIO.
///
/// Acá vive TODO el SQL del módulo y en ningún otro lado. Es lo que hace que
/// cambiar de motor algún día sea reescribir strings y no la aplicación.
///
/// Las funciones se llaman con SELECT, no con CommandType.StoredProcedure:
/// Npgsql traduce eso a CALL, que solo sirve con PROCEDURE de verdad y falla
/// contra una función con un error que no dice por qué.
///
/// Contrato de escritura, el mismo @dg_resultado del legacy:
///   "" = salió bien   ·   texto = error de negocio que se muestra tal cual
/// </summary>
public class InventarioDAL
{
    private readonly IAccesoDatos _db;

    public InventarioDAL(IAccesoDatos db) => _db = db;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResumenInventario?> ConsultarResumen(
    ProductoFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("CategoriaId", f.CategoriaId);
        p.Add("Tipo", f.Tipo?.ToString());   // el enum nulable, otra vez
        p.Add("Activo", f.Activo);

        return await _db.ConsultarUno<ResumenInventario>(
            """
        SELECT * FROM sp_inv_c_resumen(
            @Buscar::text, @CategoriaId::int, @Tipo::tipo_producto, @Activo::boolean)
        """,
            p, ct);
    }

    /// <summary>sp_inv_c_productos — la grilla. Trae total_filas para paginar.</summary>
    public async Task<IEnumerable<Producto>> ConsultarProductos(
     ProductoFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("Buscar", f.Buscar);
        p.Add("CategoriaId", f.CategoriaId);
        p.Add("Tipo", f.Tipo?.ToString());
        p.Add("Activo", f.Activo);
        p.Add("BajoMinimo", f.BajoMinimo ?? false);
        p.Add("ControlaLotes", f.ControlaLotes);
        p.Add("SoloEnVenta", f.SoloEnVenta ?? false);
        p.Add("Pagina", f.PaginaReal);
        p.Add("Tamano", f.TamanoReal);

        return await _db.ConsultarLista<Producto>(
        """
        SELECT * FROM sp_inv_c_productos(
            @Buscar::text, @CategoriaId::int, @Tipo::tipo_producto,
            @Activo::boolean, @BajoMinimo::boolean, @ControlaLotes::boolean,
            @SoloEnVenta::boolean, @Pagina::int, @Tamano::int)
        """,
        p, ct);
    }

    /// <summary>sp_inv_c_producto — el detalle. Null si no existe.</summary>
    public async Task<Producto?> ConsultarProducto(int id, CancellationToken ct = default)
        => await _db.ConsultarUno<Producto>(
            "SELECT * FROM sp_inv_c_producto(@id)", new { id }, ct);

    /// <summary>sp_inv_c_producto_codigo — lo que lee el lector de barras.</summary>
    public async Task<Producto?> ConsultarProductoPorCodigo(
        string codigo, CancellationToken ct = default)
        => await _db.ConsultarUno<Producto>(
            "SELECT * FROM sp_inv_c_producto_codigo(@codigo)", new { codigo }, ct);

    /// <summary>sp_inv_c_categorias — combo del formulario.</summary>
    public async Task<IEnumerable<Categoria>> ConsultarCategorias(CancellationToken ct = default)
        => await _db.ConsultarLista<Categoria>(
            "SELECT * FROM sp_inv_c_categorias()", null, ct);

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>
    /// sp_inv_i_producto. Devuelve el id nuevo, o 0 con el mensaje del RAISE si
    /// la función rechazó la operación.
    /// </summary>
    public async Task<(int Id, string Error)> InsertarProducto(
        CrearProductoRequest r, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                """
                SELECT sp_inv_i_producto(
                    @Codigo, @Nombre, @CategoriaId, @Tipo, @Precio, @Emoji,
                    @Minimo, @Costo, @Stock, @ControlaLotes, @DiasVida)
                """,
                r, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>sp_inv_u_producto.</summary>
    public async Task<string> ActualizarProducto(
        int id, ActualizarProductoRequest r, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                """
                SELECT sp_inv_u_producto(
                    @id, @Nombre, @CategoriaId, @Precio, @Emoji, @Minimo, @Costo, @DiasVida)
                """,
                new
                {
                    id,
                    r.Nombre,
                    r.CategoriaId,
                    r.Precio,
                    r.Emoji,
                    r.Minimo,
                    r.Costo,
                    r.DiasVida
                }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    /// <summary>sp_inv_u_producto_estado — activar / desactivar.</summary>
    public async Task<string> CambiarEstadoProducto(
        int id, bool activo, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>(
                "SELECT sp_inv_u_producto_estado(@id, @activo)", new { id, activo }, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    /// <summary>sp_inv_d_producto — borrado definitivo.</summary>
    public async Task<string> EliminarProducto(int id, CancellationToken ct = default)
    {
        try
        {
            await _db.Escalar<int>("SELECT sp_inv_d_producto(@id)", new { id }, ct);
            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    /// <summary>sp_inv_i_categoria.</summary>
    public async Task<(int Id, string Error)> InsertarCategoria(
        string nombre, CancellationToken ct = default)
    {
        try
        {
            var id = await _db.Escalar<int>(
                "SELECT sp_inv_i_categoria(@nombre)", new { nombre }, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>sp_inv_i_traspaso: baja de bodega al mostrador.</summary>
    public async Task<ResultadoOp<ResultadoTraspaso>> Traspasar(
        int productoId, int cantidad, int usuarioId, string? detalle, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoTraspaso>(
                "SELECT * FROM sp_inv_i_traspaso(@productoId, @cantidad, @usuarioId, @detalle)",
                new { productoId, cantidad, usuarioId, detalle }, ct);

            return r is null
                ? ResultadoOp<ResultadoTraspaso>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoTraspaso>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoTraspaso>.Error(ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>sp_inv_i_retorno: devuelve del mostrador a bodega.</summary>
    public async Task<ResultadoOp<ResultadoTraspaso>> Retornar(
        int productoId, int cantidad, int usuarioId, string? detalle, CancellationToken ct = default)
    {
        try
        {
            var r = await _db.ConsultarUno<ResultadoTraspaso>(
                "SELECT * FROM sp_inv_i_retorno(@productoId, @cantidad, @usuarioId, @detalle)",
                new { productoId, cantidad, usuarioId, detalle }, ct);

            return r is null
                ? ResultadoOp<ResultadoTraspaso>.Error("La función no devolvió resultado.")
                : ResultadoOp<ResultadoTraspaso>.Exito(r);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ResultadoOp<ResultadoTraspaso>.Error(ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>sp_inv_c_movimientos: el libro mayor.</summary>
    public async Task<IEnumerable<Movimiento>> ConsultarMovimientos(
        MovimientoFiltro f, CancellationToken ct = default)
    {
        var p = new DynamicParameters();
        p.Add("ProductoId", f.ProductoId);
        p.Add("Tipo", f.Tipo?.ToString());   // enum nulable: va como texto
        p.Add("Desde", f.Desde);
        p.Add("Hasta", f.Hasta);
        p.Add("Pagina", f.PaginaReal);
        p.Add("Tamano", f.TamanoReal);

        return await _db.ConsultarLista<Movimiento>(
            """
        SELECT * FROM sp_inv_c_movimientos(
            @ProductoId::int, @Tipo::tipo_movimiento,
            @Desde::date, @Hasta::date, @Pagina::int, @Tamano::int)
        """,
            p, ct);
    }
}
