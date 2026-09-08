using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class LotesBLL
{
    private readonly LotesDAL _dal;

    public LotesBLL(LotesDAL dal) => _dal = dal;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResultadoPagina<Lote>> Listar(
        LoteFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarLotes(filtro, ct)).ToList();

        return new ResultadoPagina<Lote>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    /// <summary>La grilla con historial forzado: incluye agotados y descartados.</summary>
    public async Task<ResultadoPagina<Lote>> Historial(
        LoteFiltro filtro, CancellationToken ct = default)
    {
        filtro.Historial = true;
        return await Listar(filtro, ct);
    }

    /// <summary>
    /// La ficha con sus movimientos. Dos consultas y no una: los movimientos
    /// son una lista y meterlos en el mismo SELECT multiplicaría la cabecera
    /// por cada fila.
    /// </summary>
    public async Task<LoteDetalle?> Obtener(int id, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var lote = await _dal.ConsultarLote(id, ct);
        if (lote is null) return null;

        lote.Movimientos = (await _dal.ConsultarMovimientos(id, ct)).ToList();
        return lote;
    }

    /// <summary>
    /// Lo que abre el QR. Acepta el código pelado o el contenido completo del
    /// código: el lector manda uno y el vendedor puede tipear el otro.
    /// </summary>
    public async Task<LoteDetalle?> ObtenerPorCodigo(
        string codigo, CancellationToken ct = default)
    {
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        if (string.IsNullOrWhiteSpace(limpio)) return null;

        var lote = await _dal.ConsultarLotePorCodigo(limpio, ct);
        if (lote is null) return null;

        lote.Movimientos = (await _dal.ConsultarMovimientos(lote.Id, ct)).ToList();
        return lote;
    }

    public async Task<IEnumerable<LoteAlerta>> Rezagados(CancellationToken ct = default)
        => await _dal.ConsultarRezagados(ct);

    public async Task<IEnumerable<LoteAlerta>> PorVencer(
        int dias = 3, CancellationToken ct = default)
        => await _dal.ConsultarPorVencer(Math.Clamp(dias, 0, 60), ct);

    public async Task<IEnumerable<LoteAlerta>> Recuperados(CancellationToken ct = default)
        => await _dal.ConsultarRecuperados(ct);

    public async Task<IEnumerable<CostoPromedio>> CostoPromedio(CancellationToken ct = default)
        => await _dal.ConsultarCostoPromedio(ct);

    // ============================================================
    // Etiquetas
    // ============================================================

    public async Task<IEnumerable<EtiquetaLote>> Etiquetas(
        int[]? ids, CancellationToken ct = default)
    {
        var limpios = (ids ?? []).Where(i => i > 0).Distinct().ToArray();

        // Un tope: cincuenta etiquetas son doce hojas. Doscientas es alguien
        // que se equivocó al seleccionar.
        if (limpios.Length is 0 or > 200) return [];

        return await _dal.ConsultarEtiquetas(limpios, ct);
    }

    public async Task<IEnumerable<EtiquetaLote>> EtiquetasDeCompra(
        int compraId, CancellationToken ct = default)
        => compraId <= 0 ? [] : await _dal.ConsultarEtiquetasCompra(compraId, ct);

    // ============================================================
    // Punto de venta
    // ============================================================

    public async Task<ValidacionLote?> Validar(
        ValidarLoteRequest r, CancellationToken ct = default)
    {
        var limpio = QRCodeHelper.ExtraerCodigo(r.Codigo);
        if (string.IsNullOrWhiteSpace(limpio)) return null;

        return await _dal.Validar(limpio, Math.Max(r.Cantidad, 1), ct);
    }

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<ResultadoOp<LoteDetalle>> ActualizarUbicacion(
        int id, string ubicacion, int usuarioId, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<LoteDetalle>.Error("El ID debe ser mayor a 0.");

        if (string.IsNullOrWhiteSpace(ubicacion) || ubicacion.Trim().Length < 2)
            return ResultadoOp<LoteDetalle>.Error("Indica dónde está, con al menos 2 caracteres.");

        var error = await _dal.ActualizarUbicacion(id, ubicacion.Trim(), usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<LoteDetalle>.Error(error);

        var lote = await _dal.ConsultarLote(id, ct);
        return lote is null
            ? ResultadoOp<LoteDetalle>.Error("El lote no existe.")
            : ResultadoOp<LoteDetalle>.Exito(lote);
    }
}
