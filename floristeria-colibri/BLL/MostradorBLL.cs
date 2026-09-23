using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class MostradorBLL
{
    private readonly MostradorDAL _dal;

    public MostradorBLL(MostradorDAL dal) => _dal = dal;

    public async Task<ResultadoPagina<Partida>> Listar(
        PartidaFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarPartidas(filtro, ct)).ToList();

        return new ResultadoPagina<Partida>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<PartidaEscaneada?> Escanear(string codigo, CancellationToken ct = default)
    {
        // Se limpia acá para no mandar a la base cualquier cosa que el lector
        // haya decidido enviar: algunos agregan un salto de línea al final.
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        return string.IsNullOrWhiteSpace(limpio) ? null : await _dal.Escanear(limpio, ct);
    }

    public async Task<string?> QrDePartida(string codigo, CancellationToken ct = default)
    {
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        return string.IsNullOrWhiteSpace(limpio) ? null : await _dal.QrDePartida(limpio, ct);
    }

    public async Task<IEnumerable<PartidaOrden>> DeProducto(
        int productoId, CancellationToken ct = default)
        => productoId <= 0 ? [] : await _dal.DeProducto(productoId, ct);

    public async Task<ResultadoOp<ResultadoTraspaso>> Traspasar(
        TraspasoLoteRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Lote))
            return ResultadoOp<ResultadoTraspaso>.Error("Escanea el lote que vas a bajar.");

        if (r.Cantidad < 1)
            return ResultadoOp<ResultadoTraspaso>.Error("Indica cuántas varas bajar.");

        if (usuarioId <= 0)
            return ResultadoOp<ResultadoTraspaso>.Error("Sesión inválida.");

        var codigo = QRCodeHelper.ExtraerCodigo(r.Lote)!;
        return await _dal.Traspasar(codigo, r.Cantidad, usuarioId, r.Notas, ct);
    }

    public async Task<ResultadoOp<ResultadoTraspaso>> TraspasarSimple(
        TraspasoSimpleRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (r.ProductoId <= 0)
            return ResultadoOp<ResultadoTraspaso>.Error("Indica el producto.");

        if (r.Cantidad < 1)
            return ResultadoOp<ResultadoTraspaso>.Error("Indica cuántas unidades bajar.");

        if (usuarioId <= 0)
            return ResultadoOp<ResultadoTraspaso>.Error("Sesión inválida.");

        return await _dal.TraspasarSimple(r.ProductoId, r.Cantidad, usuarioId, r.Notas, ct);
    }

    public async Task<ResultadoOp<ResultadoRetorno>> Retornar(
        RetornoPartidaRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(r.Partida))
            return ResultadoOp<ResultadoRetorno>.Error("Escanea la partida que vas a devolver.");

        if (r.Cantidad < 1)
            return ResultadoOp<ResultadoRetorno>.Error("Indica cuántas varas devolver.");

        var codigo = QRCodeHelper.ExtraerCodigo(r.Partida)!;
        return await _dal.Retornar(codigo, r.Cantidad, usuarioId, r.Notas, ct);
    }
}
