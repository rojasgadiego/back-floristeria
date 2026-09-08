using System.Text.Json;
using Colibri.Api.DAL;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class ConfiguracionBLL
{
    private readonly ConfiguracionDAL _dal;

    public ConfiguracionBLL(ConfiguracionDAL dal) => _dal = dal;

    public async Task<Configuracion?> Obtener(CancellationToken ct = default)
        => await _dal.Consultar(ct);

    public async Task<ResultadoOp<JsonElement>> Guardar(
        string seccion, JsonElement valor, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<JsonElement>.Error("Sesión inválida.");

        if (valor.ValueKind != JsonValueKind.Object)
            return ResultadoOp<JsonElement>.Error("La configuración debe ser un objeto.");

        var (datos, error) = await _dal.GuardarSeccion(seccion, valor, usuarioId, ct);

        return string.IsNullOrWhiteSpace(error) && datos.HasValue
            ? ResultadoOp<JsonElement>.Exito(datos.Value)
            : ResultadoOp<JsonElement>.Error(error);
    }

    /// <summary>
    /// El club se guarda en dos tiempos cuando cambia el valor del punto y
    /// hay saldos vigentes.
    ///
    /// No es una validación técnica: revaluar los puntos en circulación es
    /// una decisión de plata que alguien tiene que tomar mirando el número.
    /// Subir de $10 a $50 con 4.000 puntos en la calle convierte una deuda de
    /// $40.000 en una de $200.000 sin que nadie lo haya decidido.
    /// </summary>
    public async Task<(ResultadoOp<JsonElement> Resultado, ImpactoClub? Impacto)> GuardarClub(
        JsonElement valor, bool confirma, int usuarioId, CancellationToken ct = default)
    {
        if (!valor.TryGetProperty("valorPunto", out var vp) || !vp.TryGetInt32(out var nuevo))
            return (ResultadoOp<JsonElement>.Error("Falta el valor del punto."), null);

        var impacto = await _dal.ConsultarImpacto(nuevo, ct);

        var cambiaValor = impacto is not null && impacto.ValorActual != nuevo;
        var hayPuntos = impacto is { PuntosVigentes: > 0 };

        if (cambiaValor && hayPuntos && !confirma)
        {
            var signo = impacto!.Diferencia > 0 ? "sube" : "baja";

            return (ResultadoOp<JsonElement>.Error(
                $"Hay {impacto.PuntosVigentes:N0} puntos en circulación de " +
                $"{impacto.ClientesConPuntos} cliente(s). Con este cambio, lo que el " +
                $"local les debe {signo} de ${impacto.PasivoActual:N0} a " +
                $"${impacto.PasivoNuevo:N0}. Confirma para aplicarlo."), impacto);
        }

        var r = await Guardar("club", valor, usuarioId, ct);
        return (r, impacto);
    }

    public async Task<ImpactoClub?> Impacto(int valorPunto, CancellationToken ct = default)
        => valorPunto < 1 ? null : await _dal.ConsultarImpacto(valorPunto, ct);
}
