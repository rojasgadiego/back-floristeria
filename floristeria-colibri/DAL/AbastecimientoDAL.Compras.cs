// Reemplaza la sección de Compras de DAL/AbastecimientoDAL.cs

using System.Text.Json;
using Colibri.Api.DbAccess;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Dapper;
using Npgsql;

namespace Colibri.Api.DAL;

public partial class AbastecimientoDAL
{
    /// <summary>
    /// Las líneas viajan a Postgres como JSONB. Es la forma de pasar un
    /// arreglo de objetos a una función sin inventar un tipo compuesto que
    /// después hay que mantener sincronizado en dos lados.
    ///
    /// camelCase a propósito: el SP lee 'productoId', no 'producto_id'.
    /// </summary>
    private static readonly JsonSerializerOptions JsonCamel = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public async Task<(int Id, string Error)> GuardarCompra(
        GuardarCompraRequest r, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("ProveedorId", r.ProveedorId);
            p.Add("Fecha", r.Fecha);
            p.Add("Documento", r.Documento);
            p.Add("Notas", r.Notas);
            p.Add("IvaTasa", r.IvaTasa);
            p.Add("Items", JsonSerializer.Serialize(r.Items, JsonCamel));
            p.Add("UsuarioId", usuarioId);

            var id = await _db.Escalar<int>(
                """
                SELECT sp_abs_i_compra(
                    @ProveedorId::int, @Fecha::date, @Documento::text, @Notas::text,
                    @IvaTasa::numeric, @Items::jsonb, @UsuarioId::int)
                """,
                p, ct);

            return (id, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (0, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<string> ActualizarCompra(
        int id, GuardarCompraRequest r, CancellationToken ct = default)
    {
        try
        {
            var p = new DynamicParameters();
            p.Add("id", id);
            p.Add("ProveedorId", r.ProveedorId);
            p.Add("Fecha", r.Fecha);
            p.Add("Documento", r.Documento);
            p.Add("Notas", r.Notas);
            p.Add("IvaTasa", r.IvaTasa);
            p.Add("Items", JsonSerializer.Serialize(r.Items, JsonCamel));

            await _db.Escalar<int>(
                """
                SELECT sp_abs_u_compra(
                    @id::int, @ProveedorId::int, @Fecha::date, @Documento::text,
                    @Notas::text, @IvaTasa::numeric, @Items::jsonb)
                """,
                p, ct);

            return string.Empty;
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return ErroresPg.Mensaje(ex);
        }
    }

    public async Task<IEnumerable<LoteResumen>> ConsultarLotesResumen(
        int compraId, CancellationToken ct = default)
        => await _db.ConsultarLista<LoteResumen>(
            "SELECT * FROM sp_abs_c_lotes_resumen(@compraId)", new { compraId }, ct);

    public async Task<IEnumerable<EvolucionCosto>> ConsultarEvolucionCosto(
        int productoId, int limite, CancellationToken ct = default)
        => await _db.ConsultarLista<EvolucionCosto>(
            "SELECT * FROM sp_abs_c_evolucion_costo(@productoId, @limite)",
            new { productoId, limite }, ct);
}
