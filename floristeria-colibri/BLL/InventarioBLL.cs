using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// Business Logic Layer de INVENTARIO.
///
/// QUÉ VALIDA ESTA CAPA: solo lo que ahorra un viaje a la base. Ids en cero,
/// strings vacíos, precios negativos.
///
/// QUÉ NO VALIDA: unicidad del código, si la categoría existe, si el producto
/// es ingrediente de un ramo, si tiene movimientos. Eso lo sabe la base, y
/// duplicarlo en C# garantiza que las dos versiones se desincronicen. Cuando la
/// función diga que no se puede, ese mensaje es el bueno.
/// </summary>
public class InventarioBLL
{
    private readonly InventarioDAL _dal;

    public InventarioBLL(InventarioDAL dal) => _dal = dal;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResumenInventario?> Resumen(ProductoFiltro f, CancellationToken ct = default)
    {
        f.Normalizar();
        return await _dal.ConsultarResumen(f, ct);
    }

    /// <summary>La grilla con bajoMinimo forzado. Sin paginar: son pocos.</summary>
    public async Task<IEnumerable<Producto>> BajoMinimo(CancellationToken ct = default)
        => await _dal.ConsultarProductos(
            new ProductoFiltro { BajoMinimo = true, Activo = true, Tamano = 200 }, ct);

    public async Task<ResultadoPagina<Producto>> ListarProductos(
        ProductoFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();

        var filas = (await _dal.ConsultarProductos(filtro, ct)).ToList();

        return new ResultadoPagina<Producto>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            // Sin filas no hay de dónde sacar el total, y es 0 igual.
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<Producto?> ObtenerProducto(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarProducto(id, ct);

    public async Task<Producto?> ObtenerProductoPorCodigo(
        string codigo, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(codigo) ? null : await _dal.ConsultarProductoPorCodigo(codigo.Trim(), ct);

    public async Task<IEnumerable<Categoria>> ListarCategorias(CancellationToken ct = default)
        => await _dal.ConsultarCategorias(ct);

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<ResultadoOp<Producto>> CrearProducto(
        CrearProductoRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Codigo)) return ResultadoOp<Producto>.Error("Debe indicar el código del producto.");
        if (string.IsNullOrWhiteSpace(r.Nombre)) return ResultadoOp<Producto>.Error("Debe indicar el nombre del producto.");
        if (r.Precio <= 0) return ResultadoOp<Producto>.Error("El precio debe ser mayor a 0.");
        if (r.Costo < 0) return ResultadoOp<Producto>.Error("El costo no puede ser negativo.");
        if (r.Minimo < 0) return ResultadoOp<Producto>.Error("El mínimo no puede ser negativo.");

        var (id, error) = await _dal.InsertarProducto(r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Producto>.Error(error);

        // Se relee para devolver la fila completa —con categoría, margen y los
        // defaults que puso la base— en vez de devolver el eco del request.
        var creado = await _dal.ConsultarProducto(id, ct);
        return creado is null
            ? ResultadoOp<Producto>.Error("El producto se creó pero no se pudo leer.")
            : ResultadoOp<Producto>.Exito(creado);
    }

    public async Task<ResultadoOp<Producto>> ActualizarProducto(
        int id, ActualizarProductoRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Producto>.Error("El ID debe ser mayor a 0.");
        if (string.IsNullOrWhiteSpace(r.Nombre)) return ResultadoOp<Producto>.Error("Debe indicar el nombre del producto.");
        if (r.Precio <= 0) return ResultadoOp<Producto>.Error("El precio debe ser mayor a 0.");

        var error = await _dal.ActualizarProducto(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Producto>.Error(error);

        var actualizado = await _dal.ConsultarProducto(id, ct);
        return actualizado is null
            ? ResultadoOp<Producto>.Error("El producto no existe.")
            : ResultadoOp<Producto>.Exito(actualizado);
    }

    public async Task<ResultadoOp<Producto>> CambiarEstado(
        int id, bool activo, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Producto>.Error("El ID debe ser mayor a 0.");

        var error = await _dal.CambiarEstadoProducto(id, activo, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Producto>.Error(error);

        var p = await _dal.ConsultarProducto(id, ct);
        return p is null
            ? ResultadoOp<Producto>.Error("El producto no existe.")
            : ResultadoOp<Producto>.Exito(p);
    }

    /// <summary>"" = eliminado. Texto = por qué no se pudo.</summary>
    public async Task<string> EliminarProducto(int id, CancellationToken ct = default)
        => id <= 0 ? "El ID debe ser mayor a 0." : await _dal.EliminarProducto(id, ct);

    public async Task<ResultadoOp<Categoria>> CrearCategoria(
        CrearCategoriaRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre))
            return ResultadoOp<Categoria>.Error("Debe indicar el nombre de la categoría.");

        var (id, error) = await _dal.InsertarCategoria(r.Nombre.Trim(), ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Categoria>.Error(error);

        return ResultadoOp<Categoria>.Exito(new Categoria { Id = id, Nombre = r.Nombre.Trim() });
    }

    public async Task<ResultadoPagina<Movimiento>> ListarMovimientos(
        MovimientoFiltro filtro, int? soloDe = null, CancellationToken ct = default)
    {
        filtro.Normalizar();

        var filas = (await _dal.ConsultarMovimientos(filtro, soloDe, ct)).ToList();

        return new ResultadoPagina<Movimiento>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

     public async Task<IEnumerable<LineaReceta>> Receta(
        int productoId, CancellationToken ct = default)
        => productoId <= 0 ? [] : await _dal.ConsultarReceta(productoId, ct);

    public async Task<IEnumerable<ComponenteDisponible>> Componentes(
        CancellationToken ct = default)
        => await _dal.ConsultarComponentes(ct);

    /// <summary>
    /// Reemplaza la receta completa. Una receta vacía es válida: puede que
    /// el producto todavía no esté definido, y bloquearlo obligaría a
    /// inventar componentes para poder guardar.
    /// </summary>
    public async Task<ResultadoOp<ResultadoReceta>> GuardarReceta(
        int productoId, RecetaRequest r, CancellationToken ct = default)
    {
        if (productoId <= 0)
            return ResultadoOp<ResultadoReceta>.Error("Indica el producto.");

        if (r.Lineas.Any(l => l.Cantidad < 1))
            return ResultadoOp<ResultadoReceta>.Error(
                "Cada componente tiene que llevar al menos 1 unidad.");

        // Que sean simples y que no haya ciclos lo valida el SP: sus
        // mensajes nombran el componente que falla.
        return await _dal.GuardarReceta(productoId, r, ct);
    }
}
