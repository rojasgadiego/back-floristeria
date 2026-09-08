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
    public async Task<Caja?> Actual(CancellationToken ct = default)
        => await _dal.ConsultarActual(ct);

    public async Task<Caja?> Resumen(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarCaja(id, ct);

    public async Task<ResultadoPagina<Caja>> Historial(
        CajaFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.ConsultarHistorial(filtro, ct)).ToList();

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
    public async Task<ResultadoOp<Caja>> Cerrar(
        CerrarCajaRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<Caja>.Error("Sesión inválida.");

        if (r.EfectivoContado < 0)
            return ResultadoOp<Caja>.Error("Indica cuánto efectivo contaste en el cajón.");

        var (id, error) = await _dal.Cerrar(r.EfectivoContado, usuarioId, r.Nota, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Caja>.Error(error);

        var caja = await _dal.ConsultarCaja(id, ct);
        return caja is null
            ? ResultadoOp<Caja>.Error("La caja se cerró pero no se pudo leer el resumen.")
            : ResultadoOp<Caja>.Exito(caja);
    }
}
