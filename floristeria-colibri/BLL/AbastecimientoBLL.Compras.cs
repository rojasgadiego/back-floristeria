// Reemplaza la sección de Compras de BLL/AbastecimientoBLL.cs

using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public partial class AbastecimientoBLL
{
    /// <summary>
    /// El detalle completo. Tres consultas y no una: los lotes solo existen
    /// si la compra ya se recibió, así que ni siquiera se piden mientras sea
    /// borrador.
    /// </summary>
    public async Task<CompraDetalle?> ObtenerDetalle(int id, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var c = await _dal.ConsultarCompra(id, ct);
        if (c is null) return null;

        var items = (await _dal.ConsultarCompraItems(id, ct)).ToList();

        var lotes = c.Estado == EstadoCompra.recibida
            ? (await _dal.ConsultarLotesResumen(id, ct)).ToList()
            : [];

        return new CompraDetalle
        {
            Id = c.Id,
            Folio = c.Folio,
            ProveedorId = c.ProveedorId,
            Proveedor = c.Proveedor,
            ProveedorRut = c.ProveedorRut,
            Fecha = c.Fecha,
            Documento = c.Documento,
            Estado = c.Estado,
            Neto = c.Neto,
            Iva = c.Iva,
            Total = c.Total,
            Lineas = c.Lineas,
            VarasTotales = c.VarasTotales,
            Notas = c.Notas,
            Usuario = c.Usuario,
            RecibidaEn = c.RecibidaEn,
            CreadoEn = c.CreadoEn,
            Items = items,
            Lotes = lotes
        };
    }

    public async Task<ResultadoOp<CompraDetalle>> CrearCompra(
        GuardarCompraRequest r, int usuarioId, CancellationToken ct = default)
    {
        var invalido = ValidarCompra(r, usuarioId);
        if (invalido is not null) return ResultadoOp<CompraDetalle>.Error(invalido);

        var (id, error) = await _dal.GuardarCompra(r, usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<CompraDetalle>.Error(error);

        var creada = await ObtenerDetalle(id, ct);
        return creada is null
            ? ResultadoOp<CompraDetalle>.Error("La compra se creó pero no se pudo leer.")
            : ResultadoOp<CompraDetalle>.Exito(creada);
    }

    public async Task<ResultadoOp<CompraDetalle>> ActualizarCompra(
        int id, GuardarCompraRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<CompraDetalle>.Error("El ID debe ser mayor a 0.");

        var invalido = ValidarCompra(r, 1);
        if (invalido is not null) return ResultadoOp<CompraDetalle>.Error(invalido);

        var error = await _dal.ActualizarCompra(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<CompraDetalle>.Error(error);

        var c = await ObtenerDetalle(id, ct);
        return c is null
            ? ResultadoOp<CompraDetalle>.Error("La compra no existe.")
            : ResultadoOp<CompraDetalle>.Exito(c);
    }

    /// <summary>
    /// Solo lo que ahorra un viaje. Que la presentación sea del producto,
    /// que el proveedor esté activo y que no se cuele un armado lo valida el
    /// SP, con mensajes que dicen en qué línea está el problema.
    /// </summary>
    private static string? ValidarCompra(GuardarCompraRequest r, int usuarioId)
    {
        if (usuarioId <= 0) return "Sesión inválida.";
        if (r.ProveedorId <= 0) return "Elige el proveedor.";
        if (r.Items is null or { Count: 0 }) return "La compra necesita al menos una línea.";

        if (r.Items.Any(l => l.PresentacionId <= 0))
            return "Falta la presentación en alguna línea.";

        if (r.Items.Any(l => l.Cantidad < 1))
            return "Todas las cantidades deben ser al menos 1.";

        if (r.IvaTasa is < 0 or > 100)
            return "La tasa de IVA debe estar entre 0 y 100.";

        return null;
    }

    public async Task<IEnumerable<EvolucionCosto>> EvolucionCosto(
        int productoId, int limite = 12, CancellationToken ct = default)
        => productoId <= 0
            ? []
            : await _dal.ConsultarEvolucionCosto(productoId, Math.Clamp(limite, 1, 60), ct);
}
