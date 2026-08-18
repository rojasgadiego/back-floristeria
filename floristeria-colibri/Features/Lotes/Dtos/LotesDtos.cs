using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Domain;

namespace Colibri.Api.Features.Lotes.Dtos;

/* ===================== Lectura ===================== */

public class LoteDto
{
    public int Id { get; set; }

    /// <summary>Lo que va dentro del QR. Corto para poder tipearlo si la etiqueta se borra.</summary>
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string? Proveedor { get; set; }
    public string? Presentacion { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int DiasEnCamara { get; set; }
    public int? DiasParaVencer { get; set; }

    public int VarasIniciales { get; set; }
    public int VarasDisponibles { get; set; }
    public int VarasConsumidas { get; set; }
    public decimal PorcentajeVendido { get; set; }

    public decimal CostoPorVara { get; set; }

    /// <summary>Cuánto dinero queda parado en este lote.</summary>
    public int ValorRestante { get; set; }

    /// <summary>
    /// Dónde está guardado: 'Cámara 1, balde 3'. Es una nota para encontrarlo
    /// físicamente. NO confundir con UbicacionInventario.
    /// </summary>
    public string? Ubicacion { get; set; }

    /// <summary>
    /// De qué lado está: bodega o venta. Solo se puede vender lo que está en
    /// venta; lo demás hay que bajarlo al mostrador primero.
    /// </summary>
    public string UbicacionInventario { get; set; } = "bodega";

    /// <summary>
    /// Posición en la fila de consumo: el 1 es el que debería venderse ahora.
    /// Se numera dentro de cada lado, no sobre el total. Null en los lotes que
    /// requieren escaneo, que no participan del reparto automático.
    /// </summary>
    public long? OrdenFifo { get; set; }

    /// <summary>optima · buena · limitada. Null en flor recién comprada.</summary>
    public string? Calidad { get; set; }

    public int? OrigenLoteId { get; set; }
    public string? LoteOrigen { get; set; }

    /// <summary>Precio propio del lote. Null = se vende al precio del producto.</summary>
    public int? PrecioUnitario { get; set; }

    /// <summary>El precio que rige al vender desde este lote.</summary>
    public int PrecioVenta { get; set; }

    public bool EsRecuperado { get; set; }

    /// <summary>
    /// Este lote quedó fuera del reparto automático: hay que escanearlo para
    /// venderlo. Si entrara en el FIFO, una venta sin escaneo cobraría flor
    /// de segunda a precio de primera sin que nadie lo note.
    /// </summary>
    public bool RequiereEscaneo { get; set; }

    /// <summary>normal · por vencer · vencido · resto por liquidar</summary>
    public string Alerta { get; set; } = string.Empty;
}

public class LoteDetalleDto : LoteDto
{
    public string Estado { get; set; } = string.Empty;
    public int? CompraId { get; set; }
    public string? CompraFolio { get; set; }
    public string? Documento { get; set; }
    public string? Notas { get; set; }

    /// <summary>Contenido del QR: la URL que abre esta ficha.</summary>
    public string ContenidoQr { get; set; } = string.Empty;

    public IReadOnlyList<MovimientoLoteDto> Movimientos { get; set; }
        = Array.Empty<MovimientoLoteDto>();
}

public class MovimientoLoteDto
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public string Tipo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Usuario { get; set; }
}

/// <summary>
/// Lo que responde el escaneo del QR. La advertencia viene redactada desde la
/// base para que el mensaje sea idéntico en cualquier cliente que consulte.
/// </summary>
public class ValidacionDto
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Precio { get; set; }

    public int VarasDisponibles { get; set; }
    public int CantidadPedida { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int DiasEnCamara { get; set; }
    public bool Vencido { get; set; }

    /// <summary>Si este lote cubre la cantidad pedida.</summary>
    public bool Alcanza { get; set; }

    /// <summary>
    /// Si se puede vender. Falso cuando el lote está agotado, descartado, o
    /// todavía en cámara: que no alcance no impide vender, porque el resto lo
    /// completa el siguiente lote por antigüedad.
    /// </summary>
    public bool SePuedeVender { get; set; }

    /// <summary>
    /// Si está en el mostrador. Un lote de bodega escaneado en el mesón no se
    /// puede vender todavía: hay que bajarlo. Decirlo acá evita que la venta
    /// falle después con un mensaje mucho menos claro.
    /// </summary>
    public bool EnMostrador { get; set; }

    /// <summary>Hay un lote más antiguo del mismo producto con existencias.</summary>
    public bool HayLoteAnterior { get; set; }

    public string? LoteAnterior { get; set; }
    public int? VarasAnteriores { get; set; }

    /// <summary>Texto listo para mostrar. Null si no hay nada que advertir.</summary>
    public string? Advertencia { get; set; }
}

public class CostoPromedioDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;

    /// <summary>Varas en inventario, sumando cámara y mostrador.</summary>
    public long Varas { get; set; }

    /// <summary>Dinero inmovilizado en ese producto.</summary>
    public int ValorTotal { get; set; }

    /// <summary>
    /// Promedio ponderado real. Con lotes de $900 y $800 mezclados, no es el
    /// promedio simple: pesa cuántas varas quedan de cada uno.
    /// </summary>
    public decimal CostoPromedio { get; set; }

    /// <summary>Costo de reposición de referencia, de la ficha del producto.</summary>
    public int? CostoReferencia { get; set; }
}

/// <summary>Datos para imprimir una etiqueta adhesiva.</summary>
public class EtiquetaDto
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string? Proveedor { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int Varas { get; set; }

    /// <summary>Dónde está guardado. Va impreso para encontrarlo en cámara.</summary>
    public string? Ubicacion { get; set; }

    /// <summary>Lo que se codifica en el QR.</summary>
    public string ContenidoQr { get; set; } = string.Empty;

    /// <summary>Ruta de la imagen del QR, para el &lt;img&gt; de la impresión.</summary>
    public string UrlQr { get; set; } = string.Empty;
}

/* ===================== Escritura ===================== */

public class ActualizarUbicacionRequest
{
    /// <summary>Dónde está físicamente: 'Cámara 1, balde 3'.</summary>
    [Required(ErrorMessage = "Indica la ubicación.")]
    [StringLength(120, MinimumLength = 2)]
    public string Ubicacion { get; set; } = string.Empty;
}

public class ValidarLoteRequest
{
    [Required(ErrorMessage = "Indica el código del lote.")]
    public string Codigo { get; set; } = string.Empty;

    [Range(1, 100000)]
    public int Cantidad { get; set; } = 1;
}

/* ===================== Filtros ===================== */

public class LoteFiltro : ParametrosPagina
{
    public int? ProductoId { get; set; }
    public int? ProveedorId { get; set; }

    /// <summary>
    /// Qué lado mirar. Null trae bodega, que es de lo que trata esta pantalla:
    /// lo que está adelante tiene su propia vista.
    /// </summary>
    public Ubicacion? Ubicacion { get; set; }

    /// <summary>normal, por vencer, vencido o resto por liquidar.</summary>
    public string? Alerta { get; set; }

    /// <summary>
    /// Solo los lotes viejos que quedaron atrás porque se empezó a vender de
    /// uno nuevo. Son los candidatos a merma si no se liquidan.
    /// </summary>
    public bool SoloRezagados { get; set; }
}

public class HistorialLoteFiltro : ParametrosPagina
{
    public int? ProductoId { get; set; }

    /// <summary>activo, agotado o descartado.</summary>
    public string? Estado { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}

/// <summary>Flor recuperada con su rebaja frente al precio de lista.</summary>
public class LoteRecuperadoDto : LoteDto
{
    public int Rebaja { get; set; }
    public decimal RebajaPorcentaje { get; set; }
}