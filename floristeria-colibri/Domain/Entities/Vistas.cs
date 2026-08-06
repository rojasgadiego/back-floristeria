using System.ComponentModel.DataAnnotations.Schema;

namespace Colibri.Api.Domain.Entities;

// Las vistas del esquema se mapean como entidades sin clave (HasNoKey).
// Encapsulan reglas de negocio que conviene resolver en la base y no en C#:
// calcular la disponibilidad de un ramo en memoria obligaría a traerse toda
// la tabla de recetas en cada consulta.

/// <summary>
/// Disponibilidad real de un producto. Para un armado: las unidades ya hechas
/// más las que alcanzan a armarse con el stock de tallos, que es el mínimo
/// entre todos sus ingredientes.
/// </summary>
public class ProductoDisponible
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public TipoProducto Tipo { get; set; }
    public int CategoriaId { get; set; }
    public string Categoria { get; set; } = null!;
    public int Precio { get; set; }
    public int Minimo { get; set; }
    public bool Activo { get; set; }
    public int? Stock { get; set; }
    public int? StockListo { get; set; }
    public int CostoUnitario { get; set; }
    public int Disponible { get; set; }
    public int? PosiblesDeArmar { get; set; }
    public decimal MargenPorcentaje { get; set; }
}

/// <summary>Costo unitario real: para un armado, su receta más la mano de obra.</summary>
public class ProductoCosto
{
    public int ProductoId { get; set; }
    public int CostoUnitario { get; set; }
}

/// <summary>De los ingresos a la utilidad, día por día.</summary>
public class ResultadoDiario
{
    public DateOnly Dia { get; set; }
    public long Boletas { get; set; }
    public long Bruto { get; set; }
    public long Descuentos { get; set; }
    public long Ingresos { get; set; }
    public long Neto { get; set; }
    public long Iva { get; set; }
    public long CostoVendido { get; set; }
    public long Mermas { get; set; }
    public long UtilidadBruta { get; set; }
    public long Resultado { get; set; }
}

/// <summary>Flor comprometida por eventos aprobados que el stock no cubre.</summary>
public class CompromisoEvento
{
    public int ProductoId { get; set; }
    public string Nombre { get; set; } = null!;
    public long Comprometido { get; set; }
    public int Disponible { get; set; }
    public long Faltante { get; set; }
}

/// <summary>
/// Lotes con existencias, su antigüedad y su alerta.
/// OrdenFifo dice en qué posición está el lote en la fila de consumo:
/// el 1 es el que debería venderse ahora.
/// </summary>
/// <summary>
/// Lotes con existencias, su antigüedad y su alerta.
/// OrdenFifo dice en qué posición está el lote en la fila de consumo:
/// el 1 es el que debería venderse ahora.
/// </summary>
public class LoteActivo
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = null!;
    public string Emoji { get; set; } = null!;
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
    public int ValorRestante { get; set; }
    public string? Ubicacion { get; set; }

    // --- Recuperación ---

    /// <summary>Null significa flor de primera, recién comprada.</summary>
    public CalidadReingreso? Calidad { get; set; }

    public int? OrigenLoteId { get; set; }
    public string? LoteOrigen { get; set; }

    /// <summary>Precio propio del lote. Null = se vende al precio del producto.</summary>
    public int? PrecioUnitario { get; set; }

    /// <summary>El precio que rige: el del lote si lo tiene, si no el del producto.</summary>
    public int PrecioVenta { get; set; }

    public bool EsRecuperado { get; set; }

    /// <summary>
    /// Este lote quedó fuera del reparto automático: hay que escanearlo
    /// para venderlo.
    /// </summary>
    public bool RequiereEscaneo { get; set; }

    /// <summary>
    /// Posición en la fila de consumo. Null en los lotes que requieren
    /// escaneo: no participan del reparto automático.
    /// </summary>
    public long? OrdenFifo { get; set; }

    /// <summary>normal · por vencer · vencido · resto por liquidar</summary>
    public string Alerta { get; set; } = null!;
}

/// <summary>Flor recuperada disponible para vender, con su rebaja.</summary>
/// <summary>
/// Flor recuperada disponible para vender, con su rebaja.
///
/// No hereda de LoteActivo a propósito: EF Core no admite herencia en
/// entidades sin clave, y el modelo falla al construirse — con un error
/// que aparece en el primer endpoint que toque cualquier DbSet.
/// </summary>
public class LoteRecuperado
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = null!;
    public string Emoji { get; set; } = null!;
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
    public int ValorRestante { get; set; }
    public string? Ubicacion { get; set; }

    public CalidadReingreso? Calidad { get; set; }
    public int? OrigenLoteId { get; set; }
    public string? LoteOrigen { get; set; }
    public int? PrecioUnitario { get; set; }
    public int PrecioVenta { get; set; }
    public bool EsRecuperado { get; set; }
    public bool RequiereEscaneo { get; set; }
    public long? OrdenFifo { get; set; }
    public string Alerta { get; set; } = null!;

    /// <summary>Pesos de diferencia contra el precio de lista.</summary>
    public int Rebaja { get; set; }

    public decimal RebajaPorcentaje { get; set; }
}

/// <summary>
/// Fila que devuelve fn_reingresar_lote. EsNuevo distingue si las varas
/// volvieron a su lote original o si se creó un lote de recuperación.
/// </summary>
public class ReingresoLote
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = null!;
    public bool EsNuevo { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
}

/// <summary>Costo promedio ponderado de lo que hay en cámara, por producto.</summary>
public class CostoPromedio
{
    public int ProductoId { get; set; }
    public long Varas { get; set; }
    public int ValorTotal { get; set; }

    [Column("costo_promedio")]   // ← acá
    public decimal Costo { get; set; }
}

/// <summary>Cómo se movió el costo por vara entre una compra y la siguiente.</summary>
public class EvolucionCosto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = null!;
    public DateOnly Fecha { get; set; }
    public string Proveedor { get; set; } = null!;
    public string? Presentacion { get; set; }
    public int Cantidad { get; set; }
    public int CostoUnitario { get; set; }
    public decimal CostoPorVara { get; set; }
    public decimal? CostoAnterior { get; set; }
    public decimal? Variacion { get; set; }
}

/// <summary>
/// Fila que devuelve fn_consumir_lotes: de qué lote salió cada porción y a
/// qué costo. No es una tabla ni una vista; se mapea sin clave para poder
/// leer el resultado de la función.
/// </summary>
public class ConsumoLote
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = null!;
    public int Cantidad { get; set; }
    public decimal CostoUnitario { get; set; }
    public DateOnly FechaIngreso { get; set; }
}

/// <summary>
/// Estado de cobro de un evento. `vencido` compara lo abonado contra las
/// cuotas que ya vencieron; sin plan de cuotas, el saldo completo vence el
/// día del evento.
/// </summary>
public class CotizacionSaldo
{
    public int Id { get; set; }
    public string Folio { get; set; } = null!;
    public string ClienteNombre { get; set; } = null!;
    public string TipoEvento { get; set; } = null!;
    public DateOnly? FechaEvento { get; set; }
    public EstadoCotizacion Estado { get; set; }

    public int Total { get; set; }
    public int Abono { get; set; }
    public int Saldo { get; set; }
    public decimal PorcentajePagado { get; set; }
    public int? DiasParaEvento { get; set; }

    public long Pagos { get; set; }
    public long Cuotas { get; set; }

    /// <summary>Lo que ya debería estar pagado a esta fecha.</summary>
    public int ExigibleHoy { get; set; }

    /// <summary>Lo exigible que todavía no se ha abonado.</summary>
    public int Vencido { get; set; }

    public DateOnly? ProximoVencimiento { get; set; }
}

