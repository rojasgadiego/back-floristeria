namespace Colibri.Api.Domain.Entities;

/// <summary>
/// Un turno de caja. Un índice único parcial en la base impide que haya
/// dos abiertas a la vez.
/// </summary>
public class Caja
{
    public int Id { get; set; }
    public EstadoCaja Estado { get; set; } = EstadoCaja.abierta;
    public int FondoInicial { get; set; }
    public DateTimeOffset AbiertaEn { get; set; }
    public int AbiertaPor { get; set; }

    public DateTimeOffset? CerradaEn { get; set; }
    public int? CerradaPor { get; set; }
    public int? EfectivoEsperado { get; set; }
    public int? EfectivoContado { get; set; }
    public int? Diferencia { get; set; }
    public string? NotaCierre { get; set; }

    public Usuario UsuarioApertura { get; set; } = null!;
    public Usuario? UsuarioCierre { get; set; }
    public ICollection<Venta> Ventas { get; set; } = new List<Venta>();
}

public class Venta
{
    public int Id { get; set; }
    public string Folio { get; set; } = null!;
    public string NumeroAtencion { get; set; } = null!;

    public int CajaId { get; set; }
    public int UsuarioId { get; set; }
    public int? ClienteId { get; set; }
    public int? PromocionId { get; set; }
    public int? CotizacionId { get; set; }

    public int Bruto { get; set; }
    public int DescuentoPromo { get; set; }
    public int DescuentoManual { get; set; }
    public int DescuentoCanje { get; set; }
    public int DescuentoTotal { get; set; }

    /// <summary>
    /// Plata ya recibida en abonos anteriores.
    ///
    /// La boleta final de un evento lleva TODOS los productos —para consumir
    /// el inventario— pero cobra solo el saldo. No es un descuento: un
    /// descuento es una rebaja de precio, esto es dinero que ya entró.
    /// </summary>
    public int AbonoPrevio { get; set; }

    /// <summary>
    /// La tasa se guarda con la venta: si mañana cambia el IVA, las boletas
    /// viejas conservan el desglose con el que se emitieron.
    /// </summary>
    public decimal IvaTasa { get; set; } = 19m;
    public int Neto { get; set; }
    public int IvaMonto { get; set; }
    public int Total { get; set; }

    public MedioPago MedioPago { get; set; }
    public int? Recibido { get; set; }
    public int? Vuelto { get; set; }
    public string? AutorizadoPor { get; set; }

    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }

    public bool Anulada { get; set; }
    public string? MotivoAnulacion { get; set; }
    public int? AnuladaPor { get; set; }
    public DateTimeOffset? AnuladaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Caja Caja { get; set; } = null!;
    public Usuario Usuario { get; set; } = null!;
    public Cliente? Cliente { get; set; }
    public Promocion? Promocion { get; set; }
    public Cotizacion? Cotizacion { get; set; }
    public Usuario? UsuarioAnulacion { get; set; }

    /// <summary>Lo que el cliente compró: es lo que se imprime en el ticket.</summary>
    public ICollection<VentaItem> Items { get; set; } = new List<VentaItem>();

    /// <summary>Lo que realmente salió del inventario. No es lo mismo que Items.</summary>
    public ICollection<VentaConsumo> Consumos { get; set; } = new List<VentaConsumo>();
}

public class VentaItem
{
    public int Id { get; set; }
    public int VentaId { get; set; }
    public int? ProductoId { get; set; }

    /// <summary>
    /// El nombre se copia. Si el producto se renombra o se da de baja, la
    /// boleta histórica debe seguir diciendo lo que se vendió ese día.
    /// </summary>
    public string Nombre { get; set; } = null!;

    public int PrecioUnitario { get; set; }
    public int Cantidad { get; set; }
    public int Subtotal { get; set; }

    /// <summary>Despachos y traslados: no tocan inventario.</summary>
    public bool EsServicio { get; set; }

    public Venta Venta { get; set; } = null!;
    public Producto? Producto { get; set; }
}

/// <summary>
/// Plan de consumo de la venta. Vender un ramo puede descontar una unidad ya
/// armada, o armarla al momento descontando doce rosas y tres eucaliptos.
/// Guardarlo es lo que permite que una anulación devuelva exactamente lo que
/// la venta sacó, sin recalcular contra el stock de hoy, que ya cambió.
/// </summary>
public class VentaConsumo
{
    public int Id { get; set; }
    public int VentaId { get; set; }
    public int ProductoId { get; set; }

    /// <summary>
    /// De qué lote salió. Una sola línea de boleta puede consumir dos lotes:
    /// las 6 varas que quedaban del viejo más 6 del nuevo.
    /// </summary>
    public int? LoteId { get; set; }

    /// <summary>listo descuenta stock_listo; simple descuenta stock.</summary>
    public TipoConsumo Tipo { get; set; }
    public int Cantidad { get; set; }

    /// <summary>
    /// Costo real del lote consumido. Con lotes, dos rosas iguales pueden
    /// haber costado distinto: el margen verdadero sale de acá.
    /// </summary>
    public decimal? CostoUnitario { get; set; }

    public Venta Venta { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
    public Lote? Lote { get; set; }
}

/// <summary>
/// Presupuesto de evento. Una cotización aprobada no bloquea stock: la flor
/// de un matrimonio en tres semanas todavía no se compra. Lo que sí hace el
/// sistema es avisar cuánto hay comprometido.
/// </summary>
public class Cotizacion
{
    public int Id { get; set; }
    public string Folio { get; set; } = null!;
    public int? ClienteId { get; set; }

    /// <summary>Puede ser un evento sin cliente registrado en el club.</summary>
    public string ClienteNombre { get; set; } = null!;

    public string TipoEvento { get; set; } = null!;
    public DateOnly? FechaEvento { get; set; }
    public string? Contacto { get; set; }

    public int Traslado { get; set; }
    public int Montaje { get; set; }
    public int Total { get; set; }
    public int Abono { get; set; }

    public EstadoCotizacion Estado { get; set; } = EstadoCotizacion.borrador;
    public string? Notas { get; set; }

    public int? CreadaPor { get; set; }
    public int? VentaId { get; set; }
    public DateTimeOffset? CobradaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Cliente? Cliente { get; set; }
    public Usuario? UsuarioCreador { get; set; }
    public Venta? Venta { get; set; }
    public ICollection<CotizacionItem> Items { get; set; } = new List<CotizacionItem>();
    public ICollection<CotizacionPago> Pagos { get; set; } = new List<CotizacionPago>();
    public ICollection<CotizacionCuota> Cuotas { get; set; } = new List<CotizacionCuota>();
}

/// <summary>
/// Un pago recibido a cuenta de un evento.
///
/// Cada uno registra una venta real: entra por caja, tiene folio y medio de
/// pago. `cotizaciones.abono` es la suma de estos, mantenida por trigger.
/// </summary>
public class CotizacionPago
{
    public int Id { get; set; }
    public int CotizacionId { get; set; }

    /// <summary>La boleta con que entró la plata.</summary>
    public int? VentaId { get; set; }

    public int Monto { get; set; }
    public MedioPago MedioPago { get; set; }
    public DateTimeOffset Fecha { get; set; }
    public int? UsuarioId { get; set; }
    public string? Notas { get; set; }

    /// <summary>
    /// Un pago anulado deja de sumar, pero no se borra: la boleta que lo
    /// registró existió y hay que poder explicarla.
    /// </summary>
    public bool Anulado { get; set; }

    public DateTimeOffset? AnuladoEn { get; set; }
    public string? MotivoAnulacion { get; set; }

    public Cotizacion Cotizacion { get; set; } = null!;
    public Venta? Venta { get; set; }
    public Usuario? Usuario { get; set; }
}

/// <summary>
/// Cuota del plan de pago acordado.
///
/// Es una expectativa, no un hecho: las cuotas NO se marcan como pagadas una
/// por una. En un acuerdo de palabra el cliente paga 120 en vez de 100, junta
/// dos o se atrasa. Lo que el sistema compara es el total abonado contra lo
/// que ya venció, que es la única pregunta que importa: a quién llamar.
/// </summary>
public class CotizacionCuota
{
    public int Id { get; set; }
    public int CotizacionId { get; set; }
    public int Numero { get; set; }
    public int Monto { get; set; }
    public DateOnly Vence { get; set; }
    public string? Notas { get; set; }

    public Cotizacion Cotizacion { get; set; } = null!;
}

public class CotizacionItem
{
    public int Id { get; set; }
    public int CotizacionId { get; set; }
    public int? ProductoId { get; set; }
    public string Nombre { get; set; } = null!;
    public int Precio { get; set; }
    public int Cantidad { get; set; }

    /// <summary>Línea sin producto de catálogo: un arco floral, por ejemplo.</summary>
    public bool AMedida { get; set; }

    public Cotizacion Cotizacion { get; set; } = null!;
    public Producto? Producto { get; set; }
}

/// <summary>
/// Fila que devuelve fn_recibir_compra: cada lote generado al recibir, con
/// el código que va en su etiqueta QR.
/// </summary>
public class RecepcionLote
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = null!;
    public string Producto { get; set; } = null!;
    public int Varas { get; set; }
}

/// <summary>
/// Respuesta de fn_validar_lote: lo que devuelve el escaneo del QR.
/// Incluye la advertencia redactada, para que el mensaje sea el mismo en
/// cualquier cliente que consulte.
/// </summary>
public class ValidacionLote
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = null!;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = null!;
    public int VarasDisponibles { get; set; }
    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int DiasEnCamara { get; set; }
    public bool Vencido { get; set; }

    /// <summary>Si el lote cubre la cantidad pedida.</summary>
    public bool Alcanza { get; set; }

    /// <summary>Hay un lote más antiguo del mismo producto con existencias.</summary>
    public bool HayLoteAnterior { get; set; }

    public string? LoteAnterior { get; set; }
    public int? VarasAnteriores { get; set; }

    /// <summary>Texto listo para mostrar. Null si no hay nada que advertir.</summary>
    public string? Advertencia { get; set; }
}