
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// La mitad de lotes y etiquetas. Ver AbastecimientoBLL.cs para el resto.
/// </summary>
public partial class AbastecimientoBLL
{
    public async Task<LoteEscaneado?> Escanear(string codigo, CancellationToken ct = default)
    {
        // Se limpia acá para no mandar a la base cualquier cosa que el lector
        // haya decidido enviar: algunos agregan un salto de línea al final y
        // la comparación del SP no lo perdonaría.
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        if (string.IsNullOrWhiteSpace(limpio)) return null;

        return await _dal.Escanear(limpio, ct);
    }

    public async Task<LoteEtiqueta?> ObtenerEtiqueta(int loteId, CancellationToken ct = default)
        => loteId <= 0 ? null : await _dal.ConsultarEtiqueta(loteId, ct);

    public async Task<IEnumerable<LoteEtiqueta>> ListarEtiquetas(
        int compraId, CancellationToken ct = default)
        => compraId <= 0
            ? Enumerable.Empty<LoteEtiqueta>()
            : await _dal.ConsultarEtiquetas(compraId, ct);
}
