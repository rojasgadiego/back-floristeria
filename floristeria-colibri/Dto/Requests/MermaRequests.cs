using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Registrar una merma.
///
/// El origen es UNO de los tres: partida (mostrador), lote (bodega) o
/// ninguno (lo que no controla lotes). Mandar dos es un error de forma que
/// el SP rechaza.
/// </summary>
public class RegistrarMermaRequest
{
    public int ProductoId { get; set; }

    public int? LoteId { get; set; }

    /// <summary>La flor que se marchita en la vitrina sale del mostrador.</summary>
    public int? PartidaId { get; set; }

    public int Cantidad { get; set; }

    /// <summary>
    /// Del catálogo, no libre: "marchita", "Marchita" y "se marchitó" serían
    /// tres categorías distintas en el reporte, y el reporte es para lo que
    /// existe este módulo.
    /// </summary>
    public string Motivo { get; set; } = string.Empty;

    public string? Detalle { get; set; }

    public DestinoMerma Destino { get; set; } = DestinoMerma.perdida;

    /// <summary>Cuántas vuelven al stock. Solo con destino reingreso.</summary>
    public int CantidadRecuperada { get; set; }

    /// <summary>En qué estado vuelven. Define la rebaja y la vida útil.</summary>
    public CalidadReingreso? Calidad { get; set; }

    /// <summary>Lo que se escaneó o se tipeó, tal cual.</summary>
    public string? CodigoEscaneado { get; set; }

    /// <summary>
    /// true si se leyó con la cámara. Es el dato que después distingue a
    /// quien registra con el balde en la mano de quien inventa la merma.
    /// </summary>
    public bool Escaneado { get; set; }

    /// <summary>
    /// Correo y clave de quien autoriza cuando el monto supera el umbral. Se
    /// verifican contra la base: no alcanza con que la pantalla lo diga.
    /// </summary>
    public AutorizacionRequest? Autorizacion { get; set; }
}

public class DescartarLoteRequest
{
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }

    /// <summary>
    /// El proveedor lo abona: sale del stock pero no es costo. Marcarlo mal
    /// infla la merma del mes y hace ver mal a quien compró bien.
    /// </summary>
    public bool EsDevolucionProveedor { get; set; }

    public AutorizacionRequest? Autorizacion { get; set; }
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

public class RevertirMermaRequest
{
    public string Motivo { get; set; } = string.Empty;
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

    /// <summary>
    /// Firma de una administradora cuando lo que sale supera el umbral. Es la
    /// misma regla que una merma suelta: sin ella, desarmar era la forma de
    /// mermar caro sin pedir permiso.
    /// </summary>
    public AutorizacionRequest? Autorizacion { get; set; }
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
