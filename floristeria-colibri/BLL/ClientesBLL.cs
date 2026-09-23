using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class ClientesBLL
{
    private readonly ClientesDAL _dal;

    public ClientesBLL(ClientesDAL dal) => _dal = dal;

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResultadoPagina<Cliente>> Listar(
        ClienteFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.Consultar(filtro, ct)).ToList();

        return new ResultadoPagina<Cliente>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    /// <summary>
    /// La ficha con sus últimas siete compras, su libro de puntos y lo que
    /// más se lleva. Cuatro consultas: son listas distintas, y unirlas en un
    /// SELECT multiplicaría las filas de cada una por las de las otras.
    /// </summary>
    /// <param name="soloDe">Null = administrador, ve todas las compras. Con
    /// un id, solo las que vendió esa persona.</param>
    public async Task<ClienteDetalle?> Obtener(
        int id, int? soloDe = null, CancellationToken ct = default)
    {
        if (id <= 0) return null;

        var c = await _dal.ConsultarUno(id, ct);
        if (c is null) return null;

        c.Compras7 = soloDe is null
            ? (await _dal.Compras(id, 1, 7, ct)).ToList()
            : (await _dal.ComprasDe(id, soloDe.Value, 1, 7, ct)).ToList();
        c.Puntos7 = (await _dal.Puntos(id, 1, 7, ct)).ToList();
        c.Frecuentes = (await _dal.Frecuentes(id, 5, ct)).ToList();

        return c;
    }

    /// <summary>
    /// Lo que llama el punto de venta. Null cuando no hay ficha: en el mesón
    /// lo normal es que el cliente no esté registrado, y tratarlo como error
    /// haría que el vendedor viera un mensaje rojo por atender a alguien
    /// nuevo.
    /// </summary>
    public async Task<Cliente?> PorRut(string rut, CancellationToken ct = default)
        => string.IsNullOrWhiteSpace(rut) ? null : await _dal.ConsultarPorRut(rut.Trim(), ct);

    public async Task<ResultadoPagina<CompraCliente>> Compras(
        int clienteId, PaginaFiltro p, int? soloDe = null, CancellationToken ct = default)
    {
        var filas = soloDe is null
            ? (await _dal.Compras(clienteId, p.PaginaReal, p.TamanoReal, ct)).ToList()
            : (await _dal.ComprasDe(clienteId, soloDe.Value, p.PaginaReal, p.TamanoReal, ct)).ToList();

        return new ResultadoPagina<CompraCliente>
        {
            Items = filas,
            Pagina = p.PaginaReal,
            Tamano = p.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<ResultadoPagina<MovimientoPuntos>> Puntos(
        int clienteId, PaginaFiltro p, CancellationToken ct = default)
    {
        var filas = (await _dal.Puntos(clienteId, p.PaginaReal, p.TamanoReal, ct)).ToList();

        return new ResultadoPagina<MovimientoPuntos>
        {
            Items = filas,
            Pagina = p.PaginaReal,
            Tamano = p.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<IEnumerable<ClienteCumpleanos>> Cumpleanos(
        int? mes, CancellationToken ct = default)
        => mes is < 1 or > 12 ? [] : await _dal.Cumpleanos(mes, ct);

    // ============================================================
    // Escritura
    // ============================================================

    public async Task<ResultadoOp<Cliente>> Crear(
        ClienteRequest r, CancellationToken ct = default)
    {
        var invalido = Validar(r);
        if (invalido is not null) return ResultadoOp<Cliente>.Error(invalido);

        var (id, error) = await _dal.Insertar(r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Cliente>.Error(error);

        var creado = await _dal.ConsultarUno(id, ct);
        return creado is null
            ? ResultadoOp<Cliente>.Error("El cliente se creó pero no se pudo leer.")
            : ResultadoOp<Cliente>.Exito(creado);
    }

    public async Task<ResultadoOp<Cliente>> Actualizar(
        int id, ClienteRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Cliente>.Error("El ID debe ser mayor a 0.");

        var invalido = Validar(r);
        if (invalido is not null) return ResultadoOp<Cliente>.Error(invalido);

        var error = await _dal.Actualizar(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Cliente>.Error(error);

        var c = await _dal.ConsultarUno(id, ct);
        return c is null
            ? ResultadoOp<Cliente>.Error("El cliente no existe.")
            : ResultadoOp<Cliente>.Exito(c);
    }

    public async Task<ResultadoOp<Cliente>> CambiarEstado(
        int id, bool activo, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Cliente>.Error("El ID debe ser mayor a 0.");

        var error = await _dal.CambiarEstado(id, activo, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Cliente>.Error(error);

        var c = await _dal.ConsultarUno(id, ct);
        return c is null
            ? ResultadoOp<Cliente>.Error("El cliente no existe.")
            : ResultadoOp<Cliente>.Exito(c);
    }

    /// <summary>
    /// El motivo es obligatorio y con mínimo de largo: los puntos son dinero,
    /// el local le debe al cliente lo que valen, y un saldo que no cuadra
    /// tiene que poder explicarse seis meses después.
    /// </summary>
    public async Task<ResultadoOp<Cliente>> AjustarPuntos(
        int id, AjustePuntosRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Cliente>.Error("El ID debe ser mayor a 0.");
        if (r.Cantidad == 0) return ResultadoOp<Cliente>.Error("La cantidad no puede ser cero.");

        if (string.IsNullOrWhiteSpace(r.Motivo) || r.Motivo.Trim().Length < 5)
            return ResultadoOp<Cliente>.Error(
                "Explica el motivo del ajuste, con al menos 5 caracteres.");

        var error = await _dal.AjustarPuntos(id, r.Cantidad, r.Motivo.Trim(), usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Cliente>.Error(error);

        var c = await _dal.ConsultarUno(id, ct);
        return c is null
            ? ResultadoOp<Cliente>.Error("El cliente no existe.")
            : ResultadoOp<Cliente>.Exito(c);
    }

    /// <summary>
    /// Solo lo que ahorra un viaje. Que el RUT no esté repetido y que el mes
    /// sea válido lo sabe el SP, con mensajes que nombran a quién pertenece
    /// el RUT en conflicto.
    /// </summary>
    private static string? Validar(ClienteRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Rut))
            return "El RUT es obligatorio: es con lo que se busca la ficha.";

        if (string.IsNullOrWhiteSpace(r.Nombre) || r.Nombre.Trim().Length < 2)
            return "El nombre debe tener al menos 2 caracteres.";

        // Uno sin el otro no sirve: la campaña de cumpleaños necesita la
        // fecha completa.
        if (r.CumpleMes.HasValue != r.CumpleDia.HasValue)
            return "Indica el día y el mes del cumpleaños, o ninguno.";

        return null;
    }
}
