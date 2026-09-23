using Colibri.Api.BLL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Utils;
using Microsoft.AspNetCore.Mvc;

namespace Colibri.Api.Endpoints;

/// <summary>
/// Endpoints de INVENTARIO. MÓDULO DE REFERENCIA: los que vengan después
/// (Ventas, Cotizaciones, Clientes) siguen exactamente este patrón.
///
/// Son DOS grupos porque el front los pide en dos raíces distintas:
/// los productos cuelgan de /productos y lo transversal —categorías,
/// movimientos, alertas— de /inventario. Meterlos todos bajo /inventario
/// obligaría a tocar productos.service.js, que ya funciona.
///
/// El BLL NO va en el constructor: entra como parámetro de cada handler y
/// minimal API lo inyecta por request. Meterlo en el constructor obliga a
/// resolverlo en el arranque y deja un servicio Scoped vivo para siempre.
/// </summary>
public class InventarioEndpoints : EndpointsBase
{
    public InventarioEndpoints(ILogger<InventarioEndpoints> logger) : base(logger) { }

    public void MapEndpoints(IEndpointRouteBuilder app)
    {
        // ═══════════════ /api/productos ═══════════════
        var productos = app.MapGroup("/api/productos")
            .WithTags("Productos")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        productos.MapGet("/", ListarProductos)
            .WithName("ListarProductos")
            .WithSummary("Grilla de inventario (sp_inv_c_productos)")
            .Produces<ResponseDto>(200).Produces(401).Produces(500);

        productos.MapGet("/{id:int}", ObtenerProducto)
            .WithName("ObtenerProducto")
            .WithSummary("Detalle del producto (sp_inv_c_producto)")
            .Produces<ResponseDto>(200).Produces(404).Produces(401).Produces(500);

        productos.MapGet("/codigo/{codigo}", ObtenerProductoPorCodigo)
            .WithName("ObtenerProductoPorCodigo")
            .WithSummary("Búsqueda por código de barras, para el punto de venta")
            .Produces<ResponseDto>(200).Produces(404).Produces(401).Produces(500);

        productos.MapPost("/", CrearProducto)
            .WithName("CrearProducto")
            .WithSummary("Crea un producto (sp_inv_i_producto)")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400).Produces(401).Produces(500);

        productos.MapPut("/{id:int}", ActualizarProducto)
            .WithName("ActualizarProducto")
            .WithSummary("Actualiza la ficha. El tipo y el stock no se tocan acá.")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400).Produces(401).Produces(500);

        productos.MapPatch("/{id:int}/activar", (int id, InventarioBLL bll, CancellationToken ct)
                => CambiarEstado(id, true, bll, ct))
            .WithName("ActivarProducto")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400).Produces(401);

        productos.MapPatch("/{id:int}/desactivar", (int id, InventarioBLL bll, CancellationToken ct)
                => CambiarEstado(id, false, bll, ct))
            .WithName("DesactivarProducto")
            .WithSummary("Lo saca del punto de venta conservando su historial")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400).Produces(401);

        productos.MapDelete("/{id:int}", EliminarProducto)
            .WithName("EliminarProducto")
            .WithSummary("Borrado definitivo. Solo para lo creado por error.")
            .RequireAuthorization(Politicas.Admin)
            .Produces<ResponseDto>(200).Produces(400).Produces(401).Produces(500);

        // ═══════════════ /api/inventario ═══════════════
        var inventario = app.MapGroup("/api/inventario")
            .WithTags("Inventario")
            .RequireAuthorization(Politicas.VerInventario)
            .AddEndpointFilter<RespuestaFilter>();

        inventario.MapGet("/resumen", Resumen)
            .WithName("ResumenInventario")
            .WithSummary("Las tarjetas de arriba, en una sola pasada (sp_inv_c_resumen)")
            .Produces<ResponseDto>(200).Produces(401).Produces(500);

        inventario.MapGet("/bajo-minimo", BajoMinimo)
            .WithName("BajoMinimo")
            .WithSummary("Productos que llegaron a su stock mínimo. Sin paginar: son pocos.")
            .Produces<ResponseDto>(200).Produces(401).Produces(500);

        inventario.MapGet("/categorias", ListarCategorias)
            .WithName("ListarCategorias")
            .Produces<ResponseDto>(200).Produces(401).Produces(500);

        inventario.MapPost("/categorias", CrearCategoria)
            .WithName("CrearCategoria")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(201).Produces(400).Produces(401);

        inventario.MapGet("/movimientos", Movimientos)
            .WithName("Movimientos")
            .WithSummary("Libro mayor: toda entrada y salida con su motivo y responsable")
            .Produces<ResponseDto>(200);

         productos.MapGet("/componentes", Componentes)
            .WithName("ComponentesDisponibles")
            .WithSummary("Productos simples que pueden entrar en una receta")
            .Produces<ResponseDto>(200);

        productos.MapGet("/{id:int}/receta", Receta)
            .WithName("RecetaProducto")
            .WithSummary("Qué componentes lleva un armado y cuántos")
            .Produces<ResponseDto>(200);

        productos.MapPut("/{id:int}/receta", GuardarReceta)
            .WithName("GuardarReceta")
            .WithSummary("Reemplaza la receta completa")
            .RequireAuthorization(Politicas.Inventario)
            .Produces<ResponseDto>(200).Produces(400);


    }

    #region Consultas

    public async Task<ResponseDto> ListarProductos(
        [AsParameters] ProductoFiltro filtro, InventarioBLL bll, CancellationToken ct)
        => await Consultar(() => bll.ListarProductos(filtro, ct), "productos");

    public async Task<ResponseDto> ObtenerProducto(int id, InventarioBLL bll, CancellationToken ct)
        => await ConsultarUno(
            () => bll.ObtenerProducto(id, ct), "Producto", $"No existe el producto {id}.");

    public async Task<ResponseDto> ObtenerProductoPorCodigo(
        string codigo, InventarioBLL bll, CancellationToken ct)
        => await ConsultarUno(
            () => bll.ObtenerProductoPorCodigo(codigo, ct), "Producto", $"No existe el producto {codigo}.");

    /// <summary>
    /// Recibe los mismos filtros que la grilla: si el usuario filtró por
    /// categoría, las tarjetas tienen que hablar de lo que está viendo y no
    /// del total del local.
    /// </summary>
    public async Task<ResponseDto> Resumen(
        [AsParameters] ProductoFiltro filtro, InventarioBLL bll, CancellationToken ct)
        => await ConsultarUno(
            () => bll.Resumen(filtro, ct), "Resumen", "No se pudo calcular el resumen.");

    /// <summary>
    /// Atajo de la grilla con bajoMinimo forzado. Existe como ruta propia
    /// porque el front ya la llamaba así y es la alerta del dashboard.
    /// </summary>
    public async Task<ResponseDto> BajoMinimo(InventarioBLL bll, CancellationToken ct)
        => await Consultar(() => bll.BajoMinimo(ct), "productos bajo mínimo");

    public async Task<ResponseDto> ListarCategorias(InventarioBLL bll, CancellationToken ct)
        => await Consultar(() => bll.ListarCategorias(ct), "categorías");


    #region Mostrador

    /// <summary>
    /// El administrador ve el libro completo; cualquier otro, solo sus
    /// movimientos. Lo decide el token: antes el recorte lo hacía el front y
    /// la API entregaba los de todos.
    /// </summary>
    public async Task<ResponseDto> Movimientos(
        [AsParameters] MovimientoFiltro filtro, InventarioBLL bll, HttpContext http, CancellationToken ct)
    {
        int? soloDe = http.User.IsInRole("admin") ? null : UsuarioActual(http);
        return await Consultar(() => bll.ListarMovimientos(filtro, soloDe, ct), "movimientos");
    }

    #endregion
    #endregion

    #region Escritura

    public async Task<ResponseDto> CrearProducto(
        [FromBody] CrearProductoRequest peticion, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            if (peticion is null)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, "Request inválido", null);

            var r = await bll.CrearProducto(peticion, ct);

            // El mensaje del RAISE viaja tal cual, sin reformular.
            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Created, $"{r.Datos!.Nombre} agregado al inventario", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al crear producto");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al crear el producto: {ex.Message}", null);
        }
    }

    public async Task<ResponseDto> ActualizarProducto(
        int id, [FromBody] ActualizarProductoRequest peticion, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            if (peticion is null)
                return CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, "Request inválido", null);

            var r = await bll.ActualizarProducto(id, peticion, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Producto actualizado", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al actualizar producto {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al actualizar el producto: {ex.Message}", null);
        }
    }

    public async Task<ResponseDto> CambiarEstado(
        int id, bool activo, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            var r = await bll.CambiarEstado(id, activo, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(
                    HttpStatusCodes.Ok, activo ? "Producto activado" : "Producto desactivado", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al cambiar estado del producto {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al cambiar el estado: {ex.Message}", null);
        }
    }

    public async Task<ResponseDto> EliminarProducto(int id, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            var error = await bll.EliminarProducto(id, ct);

            // "" = eliminado, igual que el @dg_resultado del legacy.
            return string.IsNullOrWhiteSpace(error)
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok, "Producto eliminado", new { Eliminado = true })
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, error, new { Eliminado = false });
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al eliminar producto {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al eliminar el producto: {ex.Message}", null);
        }
    }

    public async Task<ResponseDto> CrearCategoria(
        [FromBody] CrearCategoriaRequest peticion, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            var r = await bll.CrearCategoria(peticion, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Created, "Categoría creada", r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al crear categoría");
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al crear la categoría: {ex.Message}", null);
        }
    }

    #endregion

     public async Task<ResponseDto> Receta(int id, InventarioBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Receta(id, ct), "receta");

    public async Task<ResponseDto> Componentes(InventarioBLL bll, CancellationToken ct)
        => await Consultar(() => bll.Componentes(ct), "componentes");

    public async Task<ResponseDto> GuardarReceta(
        int id, [FromBody] RecetaRequest peticion, InventarioBLL bll, CancellationToken ct)
    {
        try
        {
            var r = await bll.GuardarReceta(id, peticion, ct);

            return r.Ok
                ? CustomUtilz.CreateResponse(HttpStatusCodes.Ok,
                    $"Receta guardada · {r.Datos!.Componentes} componente(s) · " +
                    $"cuesta ${r.Datos.CostoTotal:N0} armar una",
                    r.Datos)
                : CustomUtilz.CreateResponse(HttpStatusCodes.BadRequest, r.Mensaje, null);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Error al guardar la receta de {Id}", id);
            return CustomUtilz.CreateResponse(
                HttpStatusCodes.InternalServerError, $"Error al guardar: {ex.Message}", null);
        }
    }
}