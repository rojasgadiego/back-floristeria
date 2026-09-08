using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// La primera pantalla del día. Responde tres preguntas en orden: cómo va
/// hoy, qué hay que atender, y qué viene.
/// </summary>
public class Panel
{
    public PanelHoy Hoy { get; set; } = new();

    /// <summary>
    /// El mismo día de la semana pasada, no ayer: un martes se parece a otro
    /// martes. En una florería la diferencia entre un miércoles y un sábado
    /// es de tres a uno.
    /// </summary>
    public PanelComparativo SemanaPasada { get; set; } = new();

    /// <summary>Positiva = se vende más que la semana pasada.</summary>
    public decimal VariacionSemanal { get; set; }

    /// <summary>Null si no hay turno abierto.</summary>
    public PanelCaja? Caja { get; set; }

    public PanelContexto Contexto { get; set; } = new();

    public IReadOnlyList<Alerta> Alertas { get; set; } = Array.Empty<Alerta>();
    public IReadOnlyList<EventoProximo> ProximosEventos { get; set; } = Array.Empty<EventoProximo>();
}

public class PanelHoy
{
    public long Boletas { get; set; }
    public long Vendido { get; set; }
    public long TicketPromedio { get; set; }
    public long Unidades { get; set; }

    /// <summary>
    /// Sale de los consumos: lo que valían las varas que efectivamente
    /// salieron de la cámara, no el costo de ficha del producto.
    /// </summary>
    public long Costo { get; set; }

    public long Utilidad { get; set; }
    public decimal Margen { get; set; }

    public long Anuladas { get; set; }
    public long Descuentos { get; set; }
    public long ClientesNuevos { get; set; }
}

public class PanelComparativo
{
    public long Boletas { get; set; }
    public long Vendido { get; set; }
}

public class PanelCaja
{
    public int Id { get; set; }
    public string? AbiertaPor { get; set; }
    public DateTime AbiertaEn { get; set; }
    public int Fondo { get; set; }
    public int Efectivo { get; set; }

    /// <summary>Fondo más lo que entró en efectivo. Es lo que debería haber.</summary>
    public int EnCajon { get; set; }

    public long Boletas { get; set; }
}

/// <summary>Lo que sitúa los números de hoy: el mes, el stock, la deuda.</summary>
public class PanelContexto
{
    public long MesVendido { get; set; }
    public long MesBoletas { get; set; }

    /// <summary>Al costo, no al precio: es lo invertido.</summary>
    public decimal InventarioValorizado { get; set; }

    public long ClientesActivos { get; set; }

    /// <summary>Lo que el local debe si todos canjearan mañana.</summary>
    public long PuntosPorPagar { get; set; }
}

/// <summary>
/// Cada alerta es accionable: el front la mapea a una ruta concreta. Una
/// alerta que no lleva a ninguna parte es ruido.
/// </summary>
public class Alerta
{
    /// <summary>
    /// lote_vencido · lote_por_vencer · lote_rezagado · bajo_minimo ·
    /// caja_sin_cerrar · merma_alta · cumpleanos
    /// </summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>alta · media · baja</summary>
    public string Urgencia { get; set; } = string.Empty;

    /// <summary>Redactado en el SP, con el dato concreto adentro.</summary>
    public string Mensaje { get; set; } = string.Empty;

    public long Cantidad { get; set; }

    /// <summary>Lo que está en juego. Cero cuando no aplica.</summary>
    public long Monto { get; set; }
}

/// <summary>
/// Cumpleaños de clientes y lotes que vencen, en una línea de tiempo. Son
/// las dos cosas que obligan a hacer algo con anticipación: llamar, o vender
/// antes de que se pierda.
/// </summary>
public class EventoProximo
{
    /// <summary>cumpleanos · vencimiento</summary>
    public string Tipo { get; set; } = string.Empty;

    public DateOnly Fecha { get; set; }

    /// <summary>Días desde hoy. Cero es hoy.</summary>
    public int Dias { get; set; }

    public string Titulo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public long Monto { get; set; }

    /// <summary>El teléfono del cliente o el código del lote, según el tipo.</summary>
    public string? Referencia { get; set; }
}

// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Cómo va el negocio. La merma entra como línea aparte: no es costo de
/// venta, es plata que se perdió sin haber vendido nada.
/// </summary>
public class ResultadoPeriodo
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public int Dias { get; set; }

    public long Boletas { get; set; }
    public long Unidades { get; set; }
    public long Ingresos { get; set; }
    public long Neto { get; set; }
    public long Iva { get; set; }

    public long CostoVentas { get; set; }
    public long UtilidadBruta { get; set; }
    public decimal Margen { get; set; }

    public long Descuentos { get; set; }
    public long DescuentoPromo { get; set; }
    public long DescuentoManual { get; set; }
    public long DescuentoCanje { get; set; }

    public long Merma { get; set; }

    /// <summary>
    /// Utilidad bruta menos merma. NO es utilidad contable: faltan arriendo,
    /// sueldos y todo lo que este sistema no sabe.
    /// </summary>
    public long Resultado { get; set; }

    public long TicketPromedio { get; set; }
    public decimal BoletasPorDia { get; set; }
    public long Anuladas { get; set; }
    public long MontoAnulado { get; set; }

    public long Efectivo { get; set; }
    public long Debito { get; set; }
    public long Credito { get; set; }
    public long Transferencia { get; set; }

    public long ClientesDistintos { get; set; }
    public long ConFicha { get; set; }

    public IReadOnlyList<DiaResultado> PorDia { get; set; } = Array.Empty<DiaResultado>();
    public IReadOnlyList<CategoriaResultado> PorCategoria { get; set; } = Array.Empty<CategoriaResultado>();
}

/// <summary>
/// Un día del rango. Vienen todos, incluso los sin ventas: un domingo en
/// cero es información, y saltárselo haría que la línea mintiera sobre el
/// ritmo real.
/// </summary>
public class DiaResultado
{
    public DateOnly Dia { get; set; }
    public long Boletas { get; set; }
    public long Vendido { get; set; }
    public long Costo { get; set; }
    public long Utilidad { get; set; }
}

public class CategoriaResultado
{
    public int? CategoriaId { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public long Unidades { get; set; }
    public long Ingresos { get; set; }
    public long Costo { get; set; }
    public long Utilidad { get; set; }
    public decimal Margen { get; set; }
}

/// <summary>
/// El top va por UTILIDAD, no por ingresos. El producto que más factura
/// puede ser el que peor margen deja, y ordenar por ingresos esconde eso
/// justo cuando hay que decidir qué comprar.
/// </summary>
public class RendimientoProductos
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public IReadOnlyList<ProductoRendimiento> Productos { get; set; } = Array.Empty<ProductoRendimiento>();
    public IReadOnlyList<CategoriaResultado> Categorias { get; set; } = Array.Empty<CategoriaResultado>();
}

public class ProductoRendimiento
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Categoria { get; set; }

    public long Boletas { get; set; }
    public long Unidades { get; set; }
    public long Ingresos { get; set; }
    public long Costo { get; set; }
    public long Utilidad { get; set; }
    public decimal Margen { get; set; }

    /// <summary>Qué parte de la utilidad total viene de este producto.</summary>
    public decimal Peso { get; set; }

    public long MermaUnidades { get; set; }
    public long MermaCosto { get; set; }
}

/// <summary>La foto de hoy. Sin rango: el inventario es lo que hay ahora.</summary>
public class ValorInventario
{
    public long Productos { get; set; }
    public long ProductosActivos { get; set; }
    public long BajoMinimo { get; set; }
    public long SinStock { get; set; }

    public long LotesActivos { get; set; }
    public long VarasEnBodega { get; set; }
    public long VarasEnMostrador { get; set; }

    public decimal ValorBodega { get; set; }
    public decimal ValorMostrador { get; set; }
    public decimal ValorTotal { get; set; }

    /// <summary>
    /// Lo que se ganaría vendiendo todo a precio de lista. La diferencia con
    /// el costo es utilidad potencial, no realizada.
    /// </summary>
    public decimal ValorVenta { get; set; }

    public long LotesVencidos { get; set; }
    public decimal ValorVencido { get; set; }
    public long LotesPorVencer { get; set; }
    public decimal ValorPorVencer { get; set; }

    /// <summary>
    /// Cuántos días de venta cubre el stock actual, al ritmo del último mes.
    /// Es la pregunta que ordena las compras.
    /// </summary>
    public decimal? DiasDeStock { get; set; }

    public IReadOnlyList<InventarioPorCategoria> PorCategoria { get; set; }
        = Array.Empty<InventarioPorCategoria>();
}

public class InventarioPorCategoria
{
    public int? CategoriaId { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public long Productos { get; set; }
    public long Varas { get; set; }
    public decimal Valor { get; set; }
    public decimal Parte { get; set; }
}

/// <summary>
/// Cómo le va a cada uno. Solo admin: incluye las diferencias de caja por
/// persona, y eso no debería estar a la vista de todo el equipo.
/// </summary>
public class RendimientoEquipo
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public IReadOnlyList<PersonaRendimiento> Personas { get; set; } = Array.Empty<PersonaRendimiento>();
}

public class PersonaRendimiento
{
    public int UsuarioId { get; set; }
    public string Usuario { get; set; } = string.Empty;
    public RolUsuario Rol { get; set; }

    public long Boletas { get; set; }
    public long Vendido { get; set; }
    public long TicketPromedio { get; set; }
    public long Unidades { get; set; }
    public long DescuentosDados { get; set; }
    public long Anuladas { get; set; }

    public long Turnos { get; set; }
    public long TurnosDescuadrados { get; set; }

    /// <summary>
    /// La suma CON signo: cinco turnos que alternan +2.000 y −2.000 dan
    /// cero, y eso es distinto de cinco turnos exactos. Por eso va junto a
    /// DiferenciaAbsoluta.
    /// </summary>
    public long DiferenciaAcumulada { get; set; }

    public long DiferenciaAbsoluta { get; set; }

    public long Mermas { get; set; }
    public long MermaCosto { get; set; }

    /// <summary>
    /// Quien registra con el balde en la mano escanea; quien inventa la
    /// merma, tipea.
    /// </summary>
    public long MermasSinEscanear { get; set; }
}

public class DesgloseTurno
{
    public int CajaId { get; set; }
    public EstadoCaja Estado { get; set; }
    public DateTime AbiertaEn { get; set; }
    public DateTime? CerradaEn { get; set; }
    public string? AbiertaPor { get; set; }
    public string? CerradaPor { get; set; }
    public int Minutos { get; set; }

    public int FondoInicial { get; set; }
    public long Efectivo { get; set; }
    public long Debito { get; set; }
    public long Credito { get; set; }
    public long Transferencia { get; set; }
    public long TotalVendido { get; set; }

    public long Boletas { get; set; }
    public long Anuladas { get; set; }
    public long Unidades { get; set; }
    public long TicketPromedio { get; set; }

    public long Costo { get; set; }
    public long Utilidad { get; set; }
    public decimal Margen { get; set; }

    public int? EfectivoEsperado { get; set; }
    public int? EfectivoContado { get; set; }
    public int? Diferencia { get; set; }
    public string? NotaCierre { get; set; }

    public int? HoraPico { get; set; }
    public long BoletasHoraPico { get; set; }
}
