using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Models.Enums;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// Business Logic Layer de ABASTECIMIENTO.
///
/// QUÉ VALIDA ESTA CAPA: lo que ahorra un viaje a la base. Ids en cero,
/// cantidades negativas, strings vacíos.
///
/// QUÉ NO VALIDA: si el proveedor está activo, si la presentación
/// pertenece al producto, si la compra sigue en borrador, si un armado se
/// puede comprar. Todo eso lo sabe la base y sus mensajes son los buenos.
/// </summary>
public partial class AbastecimientoBLL
{
    private readonly AbastecimientoDAL _dal;

    public AbastecimientoBLL(AbastecimientoDAL dal) => _dal = dal;

    // ============================================================
    // Proveedores
    // ============================================================

    public async Task<ResultadoPagina<Proveedor>> ListarProveedores(
        ProveedorFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarProveedores(filtro, ct)).ToList();

        return new ResultadoPagina<Proveedor>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<Proveedor?> ObtenerProveedor(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarProveedor(id, ct);

    public async Task<ResultadoOp<Proveedor>> CrearProveedor(
        ProveedorRequest r, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre))
            return ResultadoOp<Proveedor>.Error("Debe indicar el nombre del proveedor.");

        var (id, error) = await _dal.InsertarProveedor(r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Proveedor>.Error(error);

        var creado = await _dal.ConsultarProveedor(id, ct);
        return creado is null
            ? ResultadoOp<Proveedor>.Error("El proveedor se creó pero no se pudo leer.")
            : ResultadoOp<Proveedor>.Exito(creado);
    }

    public async Task<ResultadoOp<Proveedor>> ActualizarProveedor(
        int id, ProveedorRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Proveedor>.Error("El ID debe ser mayor a 0.");
        if (string.IsNullOrWhiteSpace(r.Nombre))
            return ResultadoOp<Proveedor>.Error("Debe indicar el nombre del proveedor.");

        var error = await _dal.ActualizarProveedor(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Proveedor>.Error(error);

        var p = await _dal.ConsultarProveedor(id, ct);
        return p is null
            ? ResultadoOp<Proveedor>.Error("El proveedor no existe.")
            : ResultadoOp<Proveedor>.Exito(p);
    }

    public async Task<ResultadoOp<Proveedor>> CambiarEstadoProveedor(
        int id, bool activo, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Proveedor>.Error("El ID debe ser mayor a 0.");

        var error = await _dal.CambiarEstadoProveedor(id, activo, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Proveedor>.Error(error);

        var p = await _dal.ConsultarProveedor(id, ct);
        return p is null
            ? ResultadoOp<Proveedor>.Error("El proveedor no existe.")
            : ResultadoOp<Proveedor>.Exito(p);
    }

    // ============================================================
    // Presentaciones
    // ============================================================

    public async Task<IEnumerable<Presentacion>> ListarPresentaciones(
        int? productoId, bool? activa, CancellationToken ct = default)
        => await _dal.ConsultarPresentaciones(
            productoId is <= 0 ? null : productoId, activa, ct);

        /// <summary>
    /// El productoId llega de la ruta (/presentaciones/producto/{id}), no del
    /// body: una presentación sin producto no existe, y tenerlo en los dos
    /// lados abre la puerta a que no coincidan.
    /// </summary>
    public async Task<ResultadoOp<Presentacion>> CrearPresentacion(
        int productoId, PresentacionRequest r, CancellationToken ct = default)
    {
        var invalido = ValidarPresentacion(productoId, r);
        if (invalido is not null) return ResultadoOp<Presentacion>.Error(invalido);

        var (id, error) = await _dal.InsertarPresentacion(productoId, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Presentacion>.Error(error);

        var creada = (await _dal.ConsultarPresentaciones(productoId, null, ct))
                     .FirstOrDefault(x => x.Id == id);

        return creada is null
            ? ResultadoOp<Presentacion>.Error("La presentación se creó pero no se pudo leer.")
            : ResultadoOp<Presentacion>.Exito(creada);
    }

    public async Task<string> ActualizarPresentacion(
        int id, PresentacionRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return "El ID debe ser mayor a 0.";
        // Al editar no se toca el producto, así que se pasa 1 solo para que
        // la validación de forma no falle por un id que acá no aplica.
        return ValidarPresentacion(1, r) ?? await _dal.ActualizarPresentacion(id, r, ct);
    }

    private static string? ValidarPresentacion(int productoId, PresentacionRequest r)
    {
        if (productoId <= 0) return "Debe indicar el producto.";
        if (string.IsNullOrWhiteSpace(r.Nombre)) return "Debe indicar el nombre de la presentación.";

        // Todo entra en paquetes o cajas: una presentación por vara suelta
        // generaría un lote —y una etiqueta QR— por cada tallo.
        if (r.Tipo == TipoPresentacion.vara)
            return "Las presentaciones se registran por paquete o por caja, no por vara suelta.";

        if (r.Paquetes < 1) return "La presentación debe traer al menos un paquete.";
        if (r.VarasPorPaquete < 1) return "Cada paquete debe traer al menos una vara.";
        if (r.Paquetes > 200) return "Doscientos paquetes por presentación es demasiado. Revisa el dato.";

        return null;
    }

    public async Task<string> CambiarEstadoPresentacion(
        int id, bool activa, CancellationToken ct = default)
        => id <= 0 ? "El ID debe ser mayor a 0." : await _dal.CambiarEstadoPresentacion(id, activa, ct);

    private static string? ValidarPresentacion(PresentacionRequest r)
    {
        if (r.ProductoId <= 0) return "Debe indicar el producto.";
        if (string.IsNullOrWhiteSpace(r.Nombre)) return "Debe indicar el nombre de la presentación.";

        // Ver el comentario del enum: la compra al detalle no existe acá.
        if (r.Tipo == TipoPresentacion.vara)
            return "Las presentaciones se registran por paquete o por caja, no por vara suelta.";

        if (r.Paquetes < 1) return "La presentación debe traer al menos un paquete.";
        if (r.VarasPorPaquete < 1) return "Cada paquete debe traer al menos una vara.";
        if (r.Paquetes > 200) return "Doscientos paquetes por presentación es demasiado. Revisa el dato.";

        return null;
    }

    // ============================================================
    // Compras
    // ============================================================

    public async Task<ResultadoPagina<Compra>> ListarCompras(
        CompraFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarCompras(filtro, ct)).ToList();

        return new ResultadoPagina<Compra>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<Compra?> ObtenerCompra(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarCompra(id, ct);

    public async Task<IEnumerable<CompraItem>> ListarCompraItems(
        int compraId, CancellationToken ct = default)
        => compraId <= 0
            ? Enumerable.Empty<CompraItem>()
            : await _dal.ConsultarCompraItems(compraId, ct);

    public async Task<ResultadoOp<Compra>> CrearCompra(
        CrearCompraRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (r.ProveedorId <= 0) return ResultadoOp<Compra>.Error("Debe indicar el proveedor.");
        if (usuarioId <= 0) return ResultadoOp<Compra>.Error("Sesión inválida.");

        var (id, error) = await _dal.InsertarCompra(r, usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Compra>.Error(error);

        var creada = await _dal.ConsultarCompra(id, ct);
        return creada is null
            ? ResultadoOp<Compra>.Error("La compra se creó pero no se pudo leer.")
            : ResultadoOp<Compra>.Exito(creada);
    }

    public async Task<ResultadoOp<Compra>> ActualizarCompra(
        int id, ActualizarCompraRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Compra>.Error("El ID debe ser mayor a 0.");
        if (r.ProveedorId <= 0) return ResultadoOp<Compra>.Error("Debe indicar el proveedor.");

        var error = await _dal.ActualizarCompra(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Compra>.Error(error);

        var c = await _dal.ConsultarCompra(id, ct);
        return c is null
            ? ResultadoOp<Compra>.Error("La compra no existe.")
            : ResultadoOp<Compra>.Exito(c);
    }

    /// <summary>
    /// Devuelve el detalle completo, no solo la línea nueva: agregar un
    /// ítem cambia también el neto, el IVA y el total de la cabecera, y el
    /// front necesita los tres para no quedar mostrando cifras viejas.
    /// </summary>
    public async Task<ResultadoOp<IEnumerable<CompraItem>>> AgregarItem(
        int compraId, CompraItemRequest r, CancellationToken ct = default)
    {
        if (compraId <= 0) return ResultadoOp<IEnumerable<CompraItem>>.Error("Debe indicar la compra.");
        if (r.ProductoId <= 0) return ResultadoOp<IEnumerable<CompraItem>>.Error("Debe indicar el producto.");
        if (r.PresentacionId <= 0) return ResultadoOp<IEnumerable<CompraItem>>.Error("Debe indicar la presentación.");
        if (r.Cantidad < 1) return ResultadoOp<IEnumerable<CompraItem>>.Error("La cantidad debe ser al menos 1.");
        if (r.CostoUnitario < 0) return ResultadoOp<IEnumerable<CompraItem>>.Error("El costo no puede ser negativo.");

        var (_, error) = await _dal.InsertarCompraItem(compraId, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<IEnumerable<CompraItem>>.Error(error);

        return ResultadoOp<IEnumerable<CompraItem>>.Exito(
            await _dal.ConsultarCompraItems(compraId, ct));
    }

    public async Task<string> QuitarItem(int itemId, CancellationToken ct = default)
        => itemId <= 0 ? "El ID debe ser mayor a 0." : await _dal.EliminarCompraItem(itemId, ct);

    /// <summary>
    /// La puerta de entrada al inventario. Si sale bien, hay etiquetas que
    /// imprimir.
    /// </summary>
    public async Task<ResultadoOp<ResultadoRecepcion>> RecibirCompra(
    int compraId, RecibirCompraRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (compraId <= 0) return ResultadoOp<ResultadoRecepcion>.Error("Debe indicar la compra.");
        if (usuarioId <= 0) return ResultadoOp<ResultadoRecepcion>.Error("Sesión inválida.");

        // Recibir con fecha futura fecharía mal el vencimiento de todos los
        // lotes: la flor duraría más en el sistema que en la cámara.
        if (r.FechaIngreso is { } f && f > DateOnly.FromDateTime(DateTime.Today))
            return ResultadoOp<ResultadoRecepcion>.Error("La fecha de ingreso no puede ser futura.");

        var resultado = await _dal.RecibirCompra(compraId, usuarioId, r.FechaIngreso, ct);
        if (!resultado.Ok) return resultado;

        // Los lotes recién creados, para que la pantalla de resultado los
        // muestre sin una segunda vuelta al servidor. Es lo que se lee justo
        // antes de mandar a imprimir.
        resultado.Datos!.Lotes = (await _dal.ConsultarLotesResumen(compraId, ct)).ToList();

        return resultado;
    }

    public async Task<string> AnularCompra(
        int compraId, string? motivo, int usuarioId, CancellationToken ct = default)
        => compraId <= 0
            ? "Debe indicar la compra."
            : await _dal.AnularCompra(compraId, usuarioId, motivo, ct);
}
