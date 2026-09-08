using Colibri.Api.Models.Tablas;

namespace Colibri.Api.DAL;

/// <summary>
/// La mitad de lotes y etiquetas. Ver AbastecimientoDAL.cs para el resto.
/// </summary>
public partial class AbastecimientoDAL
{
    /// <summary>sp_abs_c_lotes_compra — las etiquetas de una recepción.</summary>
    public async Task<IEnumerable<LoteEtiqueta>> ConsultarEtiquetas(
        int compraId, CancellationToken ct = default)
        => await _db.ConsultarLista<LoteEtiqueta>(
            "SELECT * FROM sp_abs_c_lotes_compra(@compraId)", new { compraId }, ct);

    /// <summary>Una etiqueta suelta, para reimprimir la que se despegó.</summary>
    public async Task<LoteEtiqueta?> ConsultarEtiqueta(
        int loteId, CancellationToken ct = default)
        => await _db.ConsultarUno<LoteEtiqueta>(
            """
            SELECT * FROM sp_abs_c_lotes_compra(
                (SELECT compra_id FROM lotes WHERE id = @loteId))
            WHERE id = @loteId
            """,
            new { loteId }, ct);

    /// <summary>
    /// sp_abs_c_lote_qr — lo que llama el punto de venta tras escanear.
    /// Acepta el QR completo o el código pelado.
    /// </summary>
    public async Task<LoteEscaneado?> Escanear(
        string codigo, CancellationToken ct = default)
        => await _db.ConsultarUno<LoteEscaneado>(
            "SELECT * FROM sp_abs_c_lote_qr(@codigo)", new { codigo }, ct);
}
