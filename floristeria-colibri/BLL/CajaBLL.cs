using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class CajaBLL
{
    private readonly CajaDAL _dal;

    public CajaBLL(CajaDAL dal) => _dal = dal;

    /// <summary>Null si no hay turno abierto. El front lo usa para decidir si se puede vender.</summary>
    // `soloDe`: null = administrador, ve el turno completo. Con un id, lo
    // que ve ese vendedor: sus totales y el arqueo en null. Lo decide el
    // endpoint desde el token, nunca el cliente.

    public async Task<Caja?> Actual(int? soloDe = null, CancellationToken ct = default)
        => soloDe is null
            ? await _dal.ConsultarActual(ct)
            : await _dal.ConsultarCajaDe(null, soloDe.Value, ct);

    public async Task<Caja?> Resumen(int id, int? soloDe = null, CancellationToken ct = default)
    {
        if (id <= 0) return null;
        return soloDe is null
            ? await _dal.ConsultarCaja(id, ct)
            : await _dal.ConsultarCajaDe(id, soloDe.Value, ct);
    }

    public async Task<ResultadoPagina<Caja>> Historial(
        CajaFiltro filtro, int? soloDe = null, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = soloDe is null
            ? (await _dal.ConsultarHistorial(filtro, ct)).ToList()
            : (await _dal.ConsultarHistorialDe(filtro, soloDe.Value, ct)).ToList();

        return new ResultadoPagina<Caja>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<ResultadoOp<Caja>> Abrir(
        AbrirCajaRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<Caja>.Error("Sesión inválida.");
        if (r.FondoInicial < 0) return ResultadoOp<Caja>.Error("El fondo inicial no puede ser negativo.");

        // Un fondo de siete cifras es un dedo de más en el teclado, no una
        // decisión. Vale la pena atajarlo antes de que quede en el arqueo.
        if (r.FondoInicial > 5_000_000)
            return ResultadoOp<Caja>.Error("Ese fondo inicial parece un error. Revisa el monto.");

        var (id, error) = await _dal.Abrir(r.FondoInicial, usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Caja>.Error(error);

        var caja = await _dal.ConsultarCaja(id, ct);
        return caja is null
            ? ResultadoOp<Caja>.Error("La caja se abrió pero no se pudo leer.")
            : ResultadoOp<Caja>.Exito(caja);
    }

    /// <summary>
    /// Devuelve el resumen ya cerrado, con la diferencia calculada: es lo que
    /// la pantalla muestra inmediatamente después, y pedirlo en una segunda
    /// llamada dejaría un parpadeo justo en el momento que más importa.
    /// </summary>
    /// <summary>
    /// Con `ciego`, lo que se devuelve es la vista del vendedor: puede cerrar,
    /// pero no ve el esperado ni la diferencia, que suman lo de todos.
    /// </summary>
    public async Task<ResultadoOp<Caja>> Cerrar(
        CerrarCajaRequest r, int usuarioId, bool ciego = false, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<Caja>.Error("Sesión inválida.");

        if (r.EfectivoContado < 0)
            return ResultadoOp<Caja>.Error("Indica cuánto efectivo contaste en el cajón.");

        var (id, error) = await _dal.Cerrar(r.EfectivoContado, usuarioId, r.Nota, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Caja>.Error(error);

        var caja = ciego
            ? await _dal.ConsultarCajaDe(id, usuarioId, ct)
            : await _dal.ConsultarCaja(id, ct);
        return caja is null
            ? ResultadoOp<Caja>.Error("La caja se cerró pero no se pudo leer el resumen.")
            : ResultadoOp<Caja>.Exito(caja);
    }
}
