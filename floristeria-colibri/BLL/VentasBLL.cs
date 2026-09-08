using System.Text.Json;
using Colibri.Api.Auth;
using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class VentasBLL
{
    private readonly VentasDAL _dal;
    private readonly AccesoDAL _acceso;
    private readonly ILogger<VentasBLL> _log;

    public VentasBLL(VentasDAL dal, AccesoDAL acceso, ILogger<VentasBLL> log)
    {
        _dal = dal;
        _acceso = acceso;
        _log = log;
    }

    // ============================================================
    // Cobro
    // ============================================================

    public async Task<ResultadoOp<VentaDetalle>> Registrar(
        RegistrarVentaRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<VentaDetalle>.Error("Sesión inválida.");

        if (r.Items is null or { Count: 0 })
            return ResultadoOp<VentaDetalle>.Error("El carrito está vacío.");

        if (r.Items.Any(i => i.Cantidad < 1))
            return ResultadoOp<VentaDetalle>.Error("Todas las cantidades deben ser al menos 1.");

        if (r.Items.Any(i => string.IsNullOrWhiteSpace(i.Partida) && i.ProductoId is null or <= 0))
            return ResultadoOp<VentaDetalle>.Error("Cada línea necesita una partida o un producto.");

        if (r.DescuentoManual < 0)
            return ResultadoOp<VentaDetalle>.Error("El descuento no puede ser negativo.");

        // ─── La autorización ───
        //
        // Se verifica ACÁ y no en el SP porque BCrypt vive en C#. El SP solo
        // exige que la boleta venga firmada por alguien; quién es ese alguien
        // y si su clave es correcta se resuelve en este bloque.
        string? autorizadoPor = null;

        if (r.Autorizacion is { } aut && !string.IsNullOrWhiteSpace(aut.Email))
        {
            var quien = await _acceso.BuscarParaLogin(aut.Email.Trim(), ct);

            if (quien is null || !PasswordHasher.Verificar(aut.Password, quien.PasswordHash))
            {
                _log.LogWarning("Autorización de descuento fallida para {Email}", aut.Email);
                return ResultadoOp<VentaDetalle>.Error("La clave de autorización no es correcta.");
            }

            if (!quien.Activo)
                return ResultadoOp<VentaDetalle>.Error("Esa cuenta está desactivada.");

            // Solo un admin autoriza. Si un vendedor pudiera firmar el
            // descuento de otro vendedor, el umbral no serviría de nada.
            if (quien.Rol != Models.Enums.RolUsuario.admin)
                return ResultadoOp<VentaDetalle>.Error(
                    $"{quien.Nombre} no puede autorizar descuentos. Pide a una administradora.");

            autorizadoPor = quien.Nombre;
        }

        var (id, error) = await _dal.Registrar(r, usuarioId, autorizadoPor, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<VentaDetalle>.Error(error);

        var boleta = await Obtener(id, ct);
        return boleta is null
            ? ResultadoOp<VentaDetalle>.Error("La venta se registró pero no se pudo leer.")
            : ResultadoOp<VentaDetalle>.Exito(boleta);
    }

    public async Task<ResultadoOp<ResultadoAnulacion>> Anular(
        int ventaId, string motivo, int usuarioId, CancellationToken ct = default)
    {
        if (ventaId <= 0) return ResultadoOp<ResultadoAnulacion>.Error("Indica la boleta.");

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 4)
            return ResultadoOp<ResultadoAnulacion>.Error(
                "Explica por qué se anula, con al menos 4 caracteres.");

        return await _dal.Anular(ventaId, motivo.Trim(), usuarioId, ct);
    }

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResultadoPagina<Venta>> Listar(
        VentaFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarVentas(filtro, ct)).ToList();

        return new ResultadoPagina<Venta>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    /// <summary>
    /// La boleta con sus líneas y su plan de consumo. Tres consultas: las
    /// líneas y los consumos son listas, y meterlas en el mismo SELECT
    /// multiplicaría la cabecera por cada fila.
    /// </summary>
    public async Task<VentaDetalle?> Obtener(int id, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var v = await _dal.ConsultarVenta(id, ct);
        if (v is null) return null;

        v.Items = (await _dal.ConsultarItems(id, ct)).ToList();
        v.Consumos = (await _dal.ConsultarConsumos(id, ct)).ToList();

        return v;
    }

    public async Task<IEnumerable<PromocionAplicable>> PromocionesAplicables(
        IEnumerable<VentaLineaPrevia> items, CancellationToken ct = default)
    {
        var limpios = items?.Where(i => i.ProductoId > 0 && i.Cantidad > 0).ToList() ?? [];
        if (limpios.Count == 0) return [];

        return await _dal.PromocionesAplicables(
            limpios.Select(i => new
            {
                productoId = i.ProductoId,
                cantidad = i.Cantidad,
                subtotal = i.Subtotal
            }), ct);
    }

    /// <summary>
    /// El ticket: la boleta más los datos del local. Todo sale de la base,
    /// incluidos el nombre, el RUT y la leyenda, porque cambiar el teléfono
    /// del local no debería requerir un despliegue.
    /// </summary>
    public async Task<Ticket?> Ticket(int ventaId, CancellationToken ct = default)
    {
        var venta = await Obtener(ventaId, ct);
        if (venta is null) return null;

        return new Ticket
        {
            Venta = venta,
            Local = await LeerConfig(_dal, "local", ct),
            Configuracion = await LeerConfig(_dal, "ticket", ct)
        };
    }

    private static async Task<JsonElement?> LeerConfig(
        VentasDAL dal, string clave, CancellationToken ct)
    {
        var json = await dal.ConsultarConfig(clave, ct);
        return string.IsNullOrWhiteSpace(json) ? null : JsonDocument.Parse(json).RootElement.Clone();
    }
}

/// <summary>Una línea del carrito para previsualizar promociones.</summary>
public class VentaLineaPrevia
{
    public int ProductoId { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }
}

/// <summary>
/// Lo que se imprime. El JsonElement deja pasar la configuración tal como
/// está en la base: agregar un campo al local no obliga a tocar una clase.
/// </summary>
public class Ticket
{
    public VentaDetalle Venta { get; set; } = new();
    public JsonElement? Local { get; set; }
    public JsonElement? Configuracion { get; set; }
}
