using System.Text.Json;
using Colibri.Api.DbAccess;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>
/// Data Access Layer de CONFIGURACIÓN.
///
/// Las secciones van y vienen como jsonb crudo: la forma la decide el
/// front. Tipar cada campo en C# obligaría a un despliegue para agregar el
/// Instagram del local.
/// </summary>
public class ConfiguracionDAL
{
    private readonly IAccesoDatos _db;

    public ConfiguracionDAL(IAccesoDatos db) => _db = db;

    /// <summary>
    /// Dapper no sabe mapear jsonb a JsonElement, así que la fila llega como
    /// strings y se parsea acá. Es una vuelta más, pero deja el contrato del
    /// front intacto.
    /// </summary>
    public async Task<Configuracion?> Consultar(CancellationToken ct = default)
    {
        var fila = await _db.ConsultarUno<ConfiguracionCruda>(
            """
            SELECT local::text            AS local,
                   ticket::text           AS ticket,
                   venta::text            AS venta,
                   club::text             AS club,
                   actualizado_en,
                   actualizado_por
            FROM sp_cfg_c_configuracion()
            """,
            null, ct);

        if (fila is null) return null;

        return new Configuracion
        {
            Local = Parsear(fila.Local),
            Ticket = Parsear(fila.Ticket),
            Venta = Parsear(fila.Venta),
            Club = Parsear(fila.Club),
            ActualizadoEn = fila.ActualizadoEn,
            ActualizadoPor = fila.ActualizadoPor
        };
    }

    public async Task<(JsonElement? Datos, string Error)> GuardarSeccion(
        string clave, JsonElement valor, int usuarioId, CancellationToken ct = default)
    {
        try
        {
            var json = await _db.Escalar<string>(
                "SELECT sp_cfg_u_seccion(@clave, @valor::jsonb, @usuarioId)::text",
                new { clave, valor = valor.GetRawText(), usuarioId }, ct);

            return (Parsear(json), string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (null, ErroresPg.Mensaje(ex));
        }
    }

    public async Task<ImpactoClub?> ConsultarImpacto(
        int valorPunto, CancellationToken ct = default)
        => await _db.ConsultarUno<ImpactoClub>(
            "SELECT * FROM sp_cfg_c_impacto_club(@valorPunto)", new { valorPunto }, ct);

    private static JsonElement? Parsear(string? json)
        => string.IsNullOrWhiteSpace(json)
            ? null
            : JsonDocument.Parse(json).RootElement.Clone();

    /// <summary>Solo para el salto jsonb → string → JsonElement.</summary>
    private class ConfiguracionCruda
    {
        public string? Local { get; set; }
        public string? Ticket { get; set; }
        public string? Venta { get; set; }
        public string? Club { get; set; }
        public DateTime? ActualizadoEn { get; set; }
        public string? ActualizadoPor { get; set; }
    }
}
