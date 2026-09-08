using Colibri.Api.Auth;
using Colibri.Api.DAL;
using Colibri.Api.Dto;
using Colibri.Api.Dto.Requests;
using Colibri.Api.Models.Enums;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

public class MermasBLL
{
    private readonly MermasDAL _dal;

    /// <summary>Para verificar la firma de quien autoriza. BCrypt vive en C#.</summary>
    private readonly AccesoDAL _acceso;

    private readonly ILogger<MermasBLL> _log;

    public MermasBLL(MermasDAL dal, AccesoDAL acceso, ILogger<MermasBLL> log)
    {
        _dal = dal;
        _acceso = acceso;
        _log = log;
    }

    // ============================================================
    // Consultas
    // ============================================================

    public async Task<ResultadoPagina<Merma>> Listar(
        MermaFiltro filtro, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.Consultar(filtro, ct)).ToList();

        return new ResultadoPagina<Merma>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<Merma?> Obtener(int id, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarUna(id, ct);

    public async Task<IEnumerable<MotivoMerma>> Motivos(CancellationToken ct = default)
        => await _dal.ConsultarMotivos(ct);

    /// <summary>
    /// El resumen con sus tres desgloses. Cuatro consultas en vez de una: son
    /// agregaciones distintas y unirlas en un solo SELECT multiplicaría las
    /// filas de cada una por las de las otras.
    /// </summary>
    public async Task<ResumenMermas?> Resumen(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var r = await _dal.ConsultarResumen(desde, hasta, ct);
        if (r is null) return null;

        r.PorDestino = (await _dal.PorDestino(desde, hasta, ct)).ToList();
        r.PorProducto = (await _dal.PorProducto(desde, hasta, ct)).ToList();
        r.PorMotivo = (await _dal.PorMotivo(desde, hasta, ct)).ToList();

        return r;
    }

    // ============================================================
    // Escaneo
    // ============================================================

    public async Task<OrigenMerma?> Escanear(string codigo, CancellationToken ct = default)
    {
        // Se limpia acá para no mandar a la base cualquier cosa que el lector
        // haya decidido enviar: algunos agregan un salto de línea al final.
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        return string.IsNullOrWhiteSpace(limpio) ? null : await _dal.Escanear(limpio, ct);
    }

    public async Task<int> Umbral(CancellationToken ct = default)
        => await _dal.UmbralAutorizacion(ct);

    /// <summary>
    /// Los tres patrones juntos. Es lo que responde "hay algo raro acá",
    /// aunque no lo diga con esas palabras: quién merma sin escanear, a qué
    /// hora, y la lista de las registradas a mano para revisar de a una.
    /// </summary>
    public async Task<PatronesMerma> Patrones(
        DateOnly? desde, DateOnly? hasta, CancellationToken ct = default)
    {
        var d = desde ?? DateOnly.FromDateTime(DateTime.Today.AddDays(-30));
        var h = hasta ?? DateOnly.FromDateTime(DateTime.Today);

        return new PatronesMerma
        {
            Desde = d,
            Hasta = h,
            UmbralAutorizacion = await _dal.UmbralAutorizacion(ct),
            PorUsuario = (await _dal.PatronUsuario(desde, hasta, ct)).ToList(),
            PorHora = (await _dal.PatronHorario(desde, hasta, ct)).ToList(),
            SinEscanear = (await _dal.SinEscanear(desde, hasta, ct)).ToList()
        };
    }

    // ============================================================
    // Escritura
    // ============================================================

    /// <summary>
    /// Registra la merma, verificando la firma cuando el monto la necesita.
    ///
    /// La firma se comprueba ACÁ y no en el SP porque BCrypt vive en C#. El SP
    /// solo exige que la merma cara venga firmada; quién puede firmar lo
    /// decide VerificarAutorizacion, y hoy solo un admin.
    /// </summary>
    public async Task<ResultadoOp<Merma>> Registrar(
        RegistrarMermaRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<Merma>.Error("Sesión inválida.");
        if (r.ProductoId <= 0) return ResultadoOp<Merma>.Error("Indica el producto.");
        if (r.Cantidad < 1) return ResultadoOp<Merma>.Error("La cantidad debe ser al menos 1.");

        if (string.IsNullOrWhiteSpace(r.Motivo))
            return ResultadoOp<Merma>.Error("Indica el motivo de la merma.");

        if (r.LoteId.HasValue && r.PartidaId.HasValue)
            return ResultadoOp<Merma>.Error(
                "La merma sale de un lote o de una partida, no de los dos.");

        if (r.Destino == DestinoMerma.reingreso)
        {
            if (r.CantidadRecuperada < 1)
                return ResultadoOp<Merma>.Error("Indica cuántas varas se recuperan.");

            if (r.CantidadRecuperada > r.Cantidad)
                return ResultadoOp<Merma>.Error(
                    $"No se pueden recuperar {r.CantidadRecuperada} de {r.Cantidad} varas.");

            if (r.Calidad is null)
                return ResultadoOp<Merma>.Error("Indica en qué estado vuelve la flor.");
        }

        var (autorizadoPor, errorAuth) = await VerificarAutorizacion(r.Autorizacion, ct);
        if (errorAuth is not null) return ResultadoOp<Merma>.Error(errorAuth);

        var (id, error) = await _dal.Registrar(r, autorizadoPor, usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Merma>.Error(error);

        var merma = await _dal.ConsultarUna(id, ct);
        return merma is null
            ? ResultadoOp<Merma>.Error("La merma se registró pero no se pudo leer.")
            : ResultadoOp<Merma>.Exito(merma);
    }

    public async Task<ResultadoOp<Merma>> DescartarLote(
        int loteId, DescartarLoteRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (loteId <= 0) return ResultadoOp<Merma>.Error("Indica el lote.");

        if (string.IsNullOrWhiteSpace(r.Motivo))
            return ResultadoOp<Merma>.Error("Indica por qué se descarta el lote.");

        var (autorizadoPor, errorAuth) = await VerificarAutorizacion(r.Autorizacion, ct);
        if (errorAuth is not null) return ResultadoOp<Merma>.Error(errorAuth);

        var (id, error) = await _dal.DescartarLote(loteId, r, autorizadoPor, usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Merma>.Error(error);

        var merma = await _dal.ConsultarUna(id, ct);
        return merma is null
            ? ResultadoOp<Merma>.Error("El lote se descartó pero no se pudo leer la merma.")
            : ResultadoOp<Merma>.Exito(merma);
    }

    /// <summary>
    /// El motivo es obligatorio y con mínimo de largo: revertir deshace un
    /// registro de pérdida, y dentro de seis meses alguien va a querer saber
    /// por qué.
    /// </summary>
    public async Task<ResultadoOp<Merma>> Revertir(
        int id, string motivo, int usuarioId, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<Merma>.Error("Indica la merma.");

        if (string.IsNullOrWhiteSpace(motivo) || motivo.Trim().Length < 5)
            return ResultadoOp<Merma>.Error(
                "Explica por qué se revierte, con al menos 5 caracteres.");

        var error = await _dal.Revertir(id, motivo.Trim(), usuarioId, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<Merma>.Error(error);

        var merma = await _dal.ConsultarUna(id, ct);
        return merma is null
            ? ResultadoOp<Merma>.Error("La merma no existe.")
            : ResultadoOp<Merma>.Exito(merma);
    }

    /// <summary>
    /// Devuelve el nombre de quien firmó, o el error si las credenciales no
    /// sirven. Null en las dos posiciones significa que no venía firma, y eso
    /// lo resuelve el SP: si el monto la necesitaba, rechaza.
    ///
    /// Solo un admin autoriza. Si un vendedor pudiera firmar la merma de otro
    /// vendedor, el umbral no serviría de nada: dos personas que se ponen de
    /// acuerdo son exactamente el caso que este control busca frenar.
    /// </summary>
    private async Task<(string? Nombre, string? Error)> VerificarAutorizacion(
        AutorizacionRequest? aut, CancellationToken ct)
    {
        if (aut is null || string.IsNullOrWhiteSpace(aut.Email)) return (null, null);

        var quien = await _acceso.BuscarParaLogin(aut.Email.Trim(), ct);

        if (quien is null || !PasswordHasher.Verificar(aut.Password, quien.PasswordHash))
        {
            _log.LogWarning("Autorización de merma fallida para {Email}", aut.Email);
            return (null, "La clave de autorización no es correcta.");
        }

        if (!quien.Activo) return (null, "Esa cuenta está desactivada.");

        if (quien.Rol != RolUsuario.admin)
            return (null, $"{quien.Nombre} no puede autorizar mermas. Pide a una administradora.");

        return (quien.Nombre, null);
    }

    // ============================================================
    // Desarme
    // ============================================================

    public async Task<IEnumerable<LineaDesarme>> PlanDesarme(
        int productoId, int cantidad, CancellationToken ct = default)
        => productoId <= 0 ? [] : await _dal.PlanDesarme(productoId, Math.Max(cantidad, 1), ct);

    public async Task<ResultadoOp<ResultadoDesarme>> Desarmar(
        int productoId, DesarmeRequest r, int usuarioId, CancellationToken ct = default)
    {
        if (productoId <= 0) return ResultadoOp<ResultadoDesarme>.Error("Indica el producto.");
        if (r.Cantidad < 1) return ResultadoOp<ResultadoDesarme>.Error("Indica cuántas unidades desarmar.");

        if (string.IsNullOrWhiteSpace(r.Motivo))
            return ResultadoOp<ResultadoDesarme>.Error("Indica el motivo del desarme.");

        if (r.Lineas is null or { Count: 0 })
            return ResultadoOp<ResultadoDesarme>.Error(
                "Falta decir qué pasa con cada componente.");

        if (r.Lineas.Any(l => l.Recuperadas < 0 || l.Perdidas < 0))
            return ResultadoOp<ResultadoDesarme>.Error("Las cantidades no pueden ser negativas.");

        // Que la suma cuadre con la receta lo valida el SP: es él quien sabe
        // cuántas varas lleva cada unidad, y su mensaje nombra el componente
        // que no calza.
        return await _dal.Desarmar(productoId, r, usuarioId, ct);
    }
}
