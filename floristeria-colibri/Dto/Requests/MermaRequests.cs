using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Registrar un reporte de merma: una cosa (puntual) o varias a la vez
/// (incidente), con un motivo, la evidencia y, si hace falta, la firma.
/// </summary>
public class RegistrarReporteRequest
{
    /// <summary>"puntual" (una línea) o "incidente" (varias).</summary>
    public string Tipo { get; set; } = "puntual";

    /// <summary>
    /// Del catálogo, no libre: "marchita", "Marchita" y "se marchitó" serían
    /// tres categorías distintas en el reporte, y el reporte es para lo que
    /// existe este módulo.
    /// </summary>
    public string Motivo { get; set; } = string.Empty;

    public string? Detalle { get; set; }

    public List<LineaReporteRequest> Lineas { get; set; } = [];

    /// <summary>De 1 a 3 fotos. Sin evidencia no se registra.</summary>
    public List<FotoEvidenciaRequest> Fotos { get; set; } = [];

    /// <summary>
    /// Código que la administración recibió por correo, cuando el total supera
    /// el umbral. Solo sirve en la sesión de quien lo pidió (sql/17).
    /// </summary>
    public AutorizacionCodigoRequest? Autorizacion { get; set; }
}

/// <summary>
/// Una cosa dañada. El origen es UNO: partida (mostrador), lote (bodega) o
/// ninguno (lo que no controla lotes, o un armado).
/// </summary>
public class LineaReporteRequest
{
    public int ProductoId { get; set; }
    public int? LoteId { get; set; }

    /// <summary>La flor que se marchita en la vitrina sale del mostrador.</summary>
    public int? PartidaId { get; set; }

    public int Cantidad { get; set; }

    public DestinoMerma Destino { get; set; } = DestinoMerma.perdida;

    /// <summary>Cuántas se pueden vender todavía. Solo con destino reingreso.</summary>
    public int CantidadRecuperada { get; set; }

    /// <summary>En qué estado quedan. Define la rebaja y la vida útil.</summary>
    public CalidadReingreso? Calidad { get; set; }

    /// <summary>Lo que se escaneó o se tipeó, tal cual.</summary>
    public string? CodigoEscaneado { get; set; }

    /// <summary>
    /// true si se leyó la etiqueta. La base solo lo acepta si el código es el
    /// del balde o la partida de origen (sql/20).
    /// </summary>
    public bool Escaneado { get; set; }
}

/// <summary>
/// Una foto, en base64 solo para viajar en el JSON: se guarda como binario
/// (bytea) en la base.
/// </summary>
public class FotoEvidenciaRequest
{
    public string Base64 { get; set; } = string.Empty;
    public string TipoMime { get; set; } = "image/jpeg";
}

/// <summary>Crear o editar un motivo del catálogo. Solo administración.</summary>
public class MotivoMermaRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string Categoria { get; set; } = "otro";
    public bool RequiereDetalle { get; set; }
    public DestinoMerma? DestinoSugerido { get; set; }

    /// <summary>Solo al editar: false lo saca de la lista.</summary>
    public bool Activo { get; set; } = true;

    public int? Orden { get; set; }
}

public class DesarmeRequest
{
    public int Cantidad { get; set; } = 1;
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }

    /// <summary>
    /// Qué pasa con cada componente. Las cantidades deben sumar exactamente
    /// lo que dice la receta: cada vara tiene que tener un destino.
    /// </summary>
    public List<LineaDesarmeRequest> Lineas { get; set; } = [];

    /// <summary>De 1 a 3 fotos del ramo. Sin evidencia no se desarma.</summary>
    public List<FotoEvidenciaRequest> Fotos { get; set; } = [];

    /// <summary>
    /// Código de autorización cuando lo que sale supera el umbral. Es la misma
    /// regla que una merma suelta: sin ella, desarmar era la forma de mermar
    /// caro sin pedir permiso.
    /// </summary>
    public AutorizacionCodigoRequest? Autorizacion { get; set; }
}

public class LineaDesarmeRequest
{
    public int ComponenteId { get; set; }
    public int Recuperadas { get; set; }
    public int Perdidas { get; set; }
    public CalidadReingreso? Calidad { get; set; }
}

public class MermaFiltro
{
    public string? Buscar { get; set; }
    public int? ProductoId { get; set; }
    public int? LoteId { get; set; }
    public string? Motivo { get; set; }
    public DestinoMerma? Destino { get; set; }

    /// <summary>
    /// false excluye las revertidas: una merma revertida no es una pérdida, y
    /// mezclarlas hace que los números no cuadren con el resumen.
    /// </summary>
    public bool? Revertida { get; set; } = false;

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 50;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;
        TamanoReal = Tamano switch { null or < 1 => 50, > 200 => 200, _ => Tamano.Value };
        Buscar = string.IsNullOrWhiteSpace(Buscar) ? null : Buscar.Trim();
        Motivo = string.IsNullOrWhiteSpace(Motivo) ? null : Motivo.Trim();
    }
}

public class RevisarReporteRequest
{
    /// <summary>Opcional: lo que vio quien revisó ("ok", "pedir explicación a…").</summary>
    public string? Nota { get; set; }
}
