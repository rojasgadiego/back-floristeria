using System.Security.Cryptography;
using Colibri.Api.Auth;
using Colibri.Api.Correo;
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

    /// <summary>Para poner nombre a quien registra (el correo del código, la firma de la admin).</summary>
    private readonly AccesoDAL _acceso;

    private readonly AvisosCorreo _avisos;
    private readonly ILogger<MermasBLL> _log;
    private readonly LimiteIntentos _limite;

    public MermasBLL(
        MermasDAL dal, AccesoDAL acceso, AvisosCorreo avisos, ILogger<MermasBLL> log,
        LimiteIntentos limite)
    {
        _dal = dal;
        _acceso = acceso;
        _avisos = avisos;
        _log = log;
        _limite = limite;
    }

    // ============================================================
    // Consultas
    // ============================================================

    /// <summary>`soloDe`: null para el administrador; si no, solo lo propio.</summary>
    public async Task<ResultadoPagina<Merma>> Listar(
        MermaFiltro filtro, int? soloDe, CancellationToken ct = default)
    {
        filtro.Normalizar();
        var filas = (await _dal.Consultar(filtro, soloDe, ct)).ToList();

        return new ResultadoPagina<Merma>
        {
            Items = filas,
            Pagina = filtro.PaginaReal,
            Tamano = filtro.TamanoReal,
            Total = filas.Count > 0 ? filas[0].TotalFilas : 0
        };
    }

    public async Task<Merma?> Obtener(int id, int? soloDe, CancellationToken ct = default)
        => id <= 0 ? null : await _dal.ConsultarUna(id, soloDe, ct);

    public async Task<IEnumerable<MotivoMerma>> Motivos(bool todos, CancellationToken ct = default)
        => await _dal.ConsultarMotivos(todos, ct);

    private static readonly string[] Categorias =
        ["natural", "accidente", "operacional", "proveedor", "comercial", "faltante", "otro"];

    private static string? ValidarMotivo(MotivoMermaRequest r)
    {
        if (string.IsNullOrWhiteSpace(r.Nombre)) return "Indica el nombre del motivo.";
        if (r.Nombre.Trim().Length > 60) return "El nombre del motivo es muy largo (máximo 60).";
        if (!Categorias.Contains(r.Categoria)) return $"Categoría desconocida: {r.Categoria}.";
        return null;
    }

    public async Task<ResultadoOp<MotivoMerma>> CrearMotivo(
        MotivoMermaRequest r, CancellationToken ct = default)
    {
        var invalido = ValidarMotivo(r);
        if (invalido is not null) return ResultadoOp<MotivoMerma>.Error(invalido);

        var (id, error) = await _dal.CrearMotivo(r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<MotivoMerma>.Error(error);

        return await MotivoLeido(id, ct);
    }

    public async Task<ResultadoOp<MotivoMerma>> ActualizarMotivo(
        int id, MotivoMermaRequest r, CancellationToken ct = default)
    {
        if (id <= 0) return ResultadoOp<MotivoMerma>.Error("Indica el motivo.");

        var invalido = ValidarMotivo(r);
        if (invalido is not null) return ResultadoOp<MotivoMerma>.Error(invalido);

        var error = await _dal.ActualizarMotivo(id, r, ct);
        if (!string.IsNullOrWhiteSpace(error)) return ResultadoOp<MotivoMerma>.Error(error);

        return await MotivoLeido(id, ct);
    }

    private async Task<ResultadoOp<MotivoMerma>> MotivoLeido(int id, CancellationToken ct)
    {
        var m = (await _dal.ConsultarMotivos(true, ct)).FirstOrDefault(x => x.Id == id);
        return m is null
            ? ResultadoOp<MotivoMerma>.Error("El motivo se guardó pero no se pudo leer.")
            : ResultadoOp<MotivoMerma>.Exito(m);
    }

    /// <summary>
    /// El resumen con sus tres desgloses. Cuatro consultas en vez de una: son
    /// agregaciones distintas y unirlas en un solo SELECT multiplicaría las
    /// filas de cada una por las de las otras.
    /// </summary>
    public async Task<ResumenMermas?> Resumen(
        DateOnly? desde, DateOnly? hasta, int? soloDe, CancellationToken ct = default)
    {
        var r = await _dal.ConsultarResumen(desde, hasta, soloDe, ct);
        if (r is null) return null;

        r.PorDestino = (await _dal.PorDestino(desde, hasta, soloDe, ct)).ToList();
        r.PorProducto = (await _dal.PorProducto(desde, hasta, soloDe, ct)).ToList();
        r.PorMotivo = (await _dal.PorMotivo(desde, hasta, soloDe, ct)).ToList();
        r.PorCategoria = (await _dal.PorCategoria(desde, hasta, soloDe, ct)).ToList();

        return r;
    }

    // ============================================================
    // Escaneo
    // ============================================================

    /// <summary>
    /// Desde el mesón solo se merma lo del mostrador. Un vendedor que escanea
    /// un balde de bodega lo ve (existe) pero bloqueado, con el porqué.
    /// </summary>
    public const string SoloMostrador =
        "Desde el mesón solo se registran mermas del mostrador (partidas PAR-…). " +
        "Los baldes de bodega los registra bodega.";

    public async Task<OrigenMerma?> Escanear(
        string codigo, bool soloMostrador = false, CancellationToken ct = default)
    {
        // Se limpia acá para no mandar a la base cualquier cosa que el lector
        // haya decidido enviar: algunos agregan un salto de línea al final.
        var limpio = QRCodeHelper.ExtraerCodigo(codigo);
        if (string.IsNullOrWhiteSpace(limpio)) return null;

        var o = await _dal.Escanear(limpio, ct);

        if (o is not null && soloMostrador && o.PartidaId is null && o.PuedeMermar)
        {
            o.PuedeMermar = false;
            o.MotivoBloqueo = SoloMostrador;
        }

        return o;
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
    /// Pide el código de autorización: lo genera, lo guarda ligado a quien lo
    /// pide y lo manda por correo a la administración, que se lo dicta.
    /// </summary>
    public async Task<ResultadoOp<string>> SolicitarCodigo(
        int valor, int usuarioId, CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<string>.Error("Sesión inválida.");
        if (valor <= 0) return ResultadoOp<string>.Error("Indica cuánto vale la merma.");

        var codigo = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        var quien = await _acceso.ConsultarUsuario(usuarioId, ct);

        await _dal.GuardarCodigo(codigo, valor, usuarioId, ct);
        _avisos.CodigoMermaSolicitado(codigo, valor, quien?.Nombre ?? $"usuario {usuarioId}");

        return ResultadoOp<string>.Exito("Código enviado a la administración por correo.");
    }

    /// <summary>
    /// Devuelve qué queda anotado como firma, o el error si el código no
    /// sirve. Null en las dos posiciones es "sin firma": si el monto la
    /// necesitaba, el SP rechaza.
    ///
    /// Una administradora no necesita código: es ella quien lo dictaría.
    /// Para el resto, el código llega por correo a la administración y solo
    /// sirve en la sesión de quien lo pidió; con cinco intentos fallidos la
    /// persona queda bloqueada un rato, y así no se puede adivinar.
    ///
    /// Antes se pedía el correo y la contraseña de la admin, escritos en el
    /// teléfono del vendedor; además el mensaje de error confirmaba si una
    /// clave ajena era correcta.
    /// </summary>
    public async Task<(string? Firma, string? Error)> VerificarAutorizacion(
        AutorizacionCodigoRequest? aut, int usuarioId, bool esAdmin, CancellationToken ct)
    {
        if (esAdmin)
        {
            var admin = await _acceso.ConsultarUsuario(usuarioId, ct);
            return (admin?.Nombre ?? "Administración", null);
        }

        if (aut is null || string.IsNullOrWhiteSpace(aut.Codigo)) return (null, null);

        var clave = "codigo-merma:" + usuarioId;
        if (_limite.Bloqueado(clave) is { } resta) return (null, LimiteIntentos.Mensaje(resta));

        if (!await _dal.UsarCodigo(aut.Codigo.Trim(), usuarioId, ct))
        {
            _limite.Fallo(clave);
            _log.LogWarning("Código de merma inválido del usuario {Usuario}", usuarioId);
            return (null, "El código es incorrecto, ya se usó o venció. Pide uno nuevo.");
        }

        _limite.Exito(clave);
        return ("Código por correo", null);
    }

    // ============================================================
    // Reportes con evidencia
    // ============================================================

    /// <summary>Lo que acepta la evidencia: lo que produce la cámara del teléfono.</summary>
    private static readonly string[] TiposFoto = ["image/jpeg", "image/png", "image/webp"];

    /// <summary>Una foto ya reducida pesa ~200 KB. 5 MB deja margen sin abrir la puerta a cualquier cosa.</summary>
    public const int PesoMaximoFoto = 5 * 1024 * 1024;

    /// <summary>
    /// Registra el reporte: una cosa (puntual) o varias (incidente), con su
    /// evidencia y, si el total pasa el tope, la firma.
    /// </summary>
    /// <param name="soloVitrina">
    /// true para la vendedora: puede registrar lo de la vitrina (partidas del
    /// mostrador y ramos armados), no lo de bodega.
    /// </param>
    public async Task<ResultadoOp<ReporteRegistrado>> RegistrarReporte(
        RegistrarReporteRequest r, int usuarioId, bool soloVitrina, bool esAdmin,
        CancellationToken ct = default)
    {
        if (usuarioId <= 0) return ResultadoOp<ReporteRegistrado>.Error("Sesión inválida.");
        if (r.Tipo is not ("puntual" or "incidente"))
            return ResultadoOp<ReporteRegistrado>.Error("Indica si es una merma puntual o un incidente.");
        if (string.IsNullOrWhiteSpace(r.Motivo))
            return ResultadoOp<ReporteRegistrado>.Error("Indica el motivo.");
        if (r.Lineas is null or { Count: 0 })
            return ResultadoOp<ReporteRegistrado>.Error("Indica qué se dañó.");
        if (r.Lineas.Any(l => l.ProductoId <= 0 || l.Cantidad < 1))
            return ResultadoOp<ReporteRegistrado>.Error("Cada cosa necesita su producto y una cantidad de al menos 1.");
        if (r.Lineas.Any(l => l.LoteId.HasValue && l.PartidaId.HasValue))
            return ResultadoOp<ReporteRegistrado>.Error("Cada cosa sale de un balde o de una partida, no de los dos.");

        var errorFotos = ValidarFotos(r.Fotos);
        if (errorFotos is not null) return ResultadoOp<ReporteRegistrado>.Error(errorFotos);

        var (autorizadoPor, errorAuth) = await VerificarAutorizacion(r.Autorizacion, usuarioId, esAdmin, ct);
        if (errorAuth is not null) return ResultadoOp<ReporteRegistrado>.Error(errorAuth);

        var res = await _dal.RegistrarReporte(r, soloVitrina, autorizadoPor, usuarioId, ct);

        // Con código = sobre el tope: eso es lo que se avisa, una vez por
        // reporte. La de una admin no: la firmó ella misma.
        if (res.Ok && autorizadoPor is not null && !esAdmin)
            _avisos.ReporteMermaRegistrado(res.Datos!.ReporteId);

        return res;
    }

    /// <summary>
    /// Que sean fotos de verdad y no cualquier archivo: se mira el tipo
    /// declarado, el tamaño y la firma de los primeros bytes.
    /// </summary>
    private static string? ValidarFotos(List<FotoEvidenciaRequest>? fotos)
    {
        if (fotos is null or { Count: 0 }) return "Agrega al menos una foto de lo que se dañó.";
        if (fotos.Count > 3) return "Son máximo 3 fotos por reporte.";

        foreach (var f in fotos)
        {
            if (!TiposFoto.Contains(f.TipoMime)) return "La foto tiene que ser JPG, PNG o WEBP.";

            byte[] bytes;
            try { bytes = Convert.FromBase64String(f.Base64 ?? ""); }
            catch (FormatException) { return "Una de las fotos llegó dañada. Vuelve a tomarla."; }

            if (bytes.Length == 0) return "Una de las fotos llegó vacía. Vuelve a tomarla.";
            if (bytes.Length > PesoMaximoFoto) return "Una foto pesa demasiado. El máximo es 5 MB.";

            var esImagen =
                (bytes.Length > 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) ||
                (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) ||
                (bytes.Length > 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46 &&
                 bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50);

            if (!esImagen) return "Uno de los archivos no es una foto.";
        }

        return null;
    }

    /// <summary>Solo quien registró, dentro de 10 minutos (lo valida la base).</summary>
    public async Task<string> DeshacerReporte(int reporteId, int usuarioId, CancellationToken ct = default)
        => reporteId <= 0 ? "Indica el reporte." : await _dal.DeshacerReporte(reporteId, usuarioId, ct);

    /// <summary>
    /// Las fotos son de quien registró y de la administración. Null si no
    /// existe o no le corresponde: mejor no confirmar que existe.
    /// </summary>
    public async Task<IEnumerable<EvidenciaMerma>?> Evidencias(
        int reporteId, int usuarioId, bool esAdmin, CancellationToken ct = default)
    {
        var dueno = await _dal.DuenoReporte(reporteId, ct);
        if (dueno is null || (!esAdmin && dueno != usuarioId)) return null;
        return await _dal.Evidencias(reporteId, ct);
    }

    public async Task<FotoEvidencia?> Evidencia(
        int id, int usuarioId, bool esAdmin, CancellationToken ct = default)
    {
        var f = await _dal.Evidencia(id, ct);
        return f is null || (!esAdmin && f.UsuarioId != usuarioId) ? null : f;
    }

    // ============================================================
    // Desarme
    // ============================================================

    public async Task<IEnumerable<LineaDesarme>> PlanDesarme(
        int productoId, int cantidad, CancellationToken ct = default)
        => productoId <= 0 ? [] : await _dal.PlanDesarme(productoId, Math.Max(cantidad, 1), ct);

    public async Task<ResultadoOp<ResultadoDesarme>> Desarmar(
        int productoId, DesarmeRequest r, int usuarioId, bool esAdmin = false,
        CancellationToken ct = default)
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

        var errorFotos = ValidarFotos(r.Fotos);
        if (errorFotos is not null) return ResultadoOp<ResultadoDesarme>.Error(errorFotos);

        var (autorizadoPor, errorAuth) = await VerificarAutorizacion(r.Autorizacion, usuarioId, esAdmin, ct);
        if (errorAuth is not null) return ResultadoOp<ResultadoDesarme>.Error(errorAuth);

        // Que la suma cuadre con la receta lo valida el SP: es él quien sabe
        // cuántas varas lleva cada unidad, y su mensaje nombra el componente
        // que no calza. También exige la firma si el desarme supera el umbral.
        var res = await _dal.Desarmar(productoId, r, autorizadoPor, usuarioId, ct);

        if (res.Ok && autorizadoPor is not null && !esAdmin)
            _avisos.ReporteMermaRegistrado(res.Datos!.ReporteId);

        return res;
    }

    // ============================================================
    // Revisión
    // ============================================================

    public async Task<IEnumerable<ReportePendiente>> Pendientes(CancellationToken ct = default)
        => await _dal.Pendientes(ct);

    public async Task<string> MarcarRevisado(
        int reporteId, int usuarioId, string? nota, CancellationToken ct = default)
        => reporteId <= 0 ? "Indica el reporte." : await _dal.MarcarRevisado(reporteId, usuarioId, nota, ct);
}
