namespace Colibri.Api.Domain.Entities;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public int Orden { get; set; }

    public ICollection<Producto> Productos { get; set; } = new List<Producto>();
}

/// <summary>
/// Un producto es simple o armado, nunca ambos. La base lo hace cumplir con
/// dos CHECK: un simple obliga a costo y stock y prohíbe stock_listo; un
/// armado al revés. Por eso las columnas de cada forma son nullable acá.
/// </summary>
public class Producto
{
    public int Id { get; set; }
    public string Codigo { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public int CategoriaId { get; set; }
    public TipoProducto Tipo { get; set; }
    public string Emoji { get; set; } = "🌿";

    /// <summary>Precio de venta con IVA incluido, en pesos enteros.</summary>
    public int Precio { get; set; }
    public int Minimo { get; set; }

    // --- Solo productos simples ---
    public int? Costo { get; set; }
    public int? Stock { get; set; }

    // --- Solo productos armados ---
    public int? StockListo { get; set; }
    public int? CostoArmado { get; set; }

    /// <summary>
    /// Las flores se controlan por lote: su Stock es la suma de los lotes
    /// activos, mantenida por un trigger. Escribirlo a mano lo rechaza la base.
    /// Un jarrón de vidrio no se muere y no necesita el seguimiento.
    /// </summary>
    public bool ControlaLotes { get; set; }

    /// <summary>Vida útil en cámara. Fija el vencimiento de cada lote.</summary>
    public int? DiasVida { get; set; }

    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Categoria Categoria { get; set; } = null!;
    public ICollection<Presentacion> Presentaciones { get; set; } = new List<Presentacion>();
    public ICollection<Lote> Lotes { get; set; } = new List<Lote>();

    /// <summary>Ingredientes de este ramo. Vacío si el producto es simple.</summary>
    public ICollection<Receta> Receta { get; set; } = new List<Receta>();

    /// <summary>Ramos que usan este producto como ingrediente.</summary>
    public ICollection<Receta> UsadoEn { get; set; } = new List<Receta>();

    public ICollection<MovimientoInventario> Movimientos { get; set; } = new List<MovimientoInventario>();
}

/// <summary>
/// Relación de un ramo con los tallos que lo componen. Un trigger impide que
/// el componente sea otro armado: sin eso habría recursión y el cálculo de
/// disponibilidad dejaría de ser resoluble.
/// </summary>
public class Receta
{
    public int ProductoId { get; set; }
    public int ComponenteId { get; set; }
    public int Cantidad { get; set; }

    public Producto Producto { get; set; } = null!;
    public Producto Componente { get; set; } = null!;
}

/// <summary>
/// Libro mayor del inventario. El stock nunca se edita: se mueve, y cada
/// movimiento deja motivo y responsable.
/// </summary>
public class MovimientoInventario
{
    public int Id { get; set; }
    public int ProductoId { get; set; }

    /// <summary>De qué lote salió o entró. Nulo en productos sin control por lote.</summary>
    public int? LoteId { get; set; }

    public TipoMovimiento Tipo { get; set; }

    /// <summary>Con signo: negativo cuando el producto sale.</summary>
    public int Cantidad { get; set; }

    /// <summary>Foto del stock justo después del movimiento.</summary>
    public int? StockResultante { get; set; }

    public string Motivo { get; set; } = null!;
    public string? Detalle { get; set; }
    public int? UsuarioId { get; set; }

    public string? ReferenciaTipo { get; set; }
    public int? ReferenciaId { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Producto Producto { get; set; } = null!;
    public Usuario? Usuario { get; set; }
    public Lote? Lote { get; set; }
}

public class Merma
{
    public int Id { get; set; }
    public int ProductoId { get; set; }

    /// <summary>Lote descartado. La flor que se marchita pertenece a un lote.</summary>
    public int? LoteId { get; set; }

    public int Cantidad { get; set; }
    public string Motivo { get; set; } = null!;
    public string? Detalle { get; set; }


    /// <summary>
    /// Qué pasó con lo que salió. Una devolución al proveedor mueve
    /// inventario pero no es pérdida: la mercadería se abona.
    /// </summary>
    public DestinoMerma Destino { get; set; } = DestinoMerma.perdida;

    /// <summary>Cuántas de las unidades movidas volvieron al inventario.</summary>
    public int CantidadRecuperada { get; set; }

    /// <summary>En qué estado volvieron. Obligatorio si algo se recuperó.</summary>
    public CalidadReingreso? CalidadReingreso { get; set; }

    /// <summary>Lote donde quedaron las varas recuperadas.</summary>
    public int? LoteRecuperacionId { get; set; }

    /// <summary>
    /// Costo con que vuelven las varas recuperadas. Null significa que
    /// conservan el original.
    ///
    /// La diferencia contra el costo original NO desaparece: es pérdida por
    /// deterioro, y la suma CostoPerdido. Una rosa de $700 que vuelve
    /// valiendo $400 deja $300 de pérdida reconocida en ese momento.
    /// </summary>
    public int? CostoRecuperadoUnitario { get; set; }


    /// <summary>
    /// El costo se congela al registrar la merma: si el proveedor sube el
    /// precio mañana, la pérdida de ayer no cambia.
    /// </summary>
    public int CostoUnitario { get; set; }
    public int CostoTotal { get; set; }

    /// <summary>
    /// Lo que realmente se perdió. Columna generada por PostgreSQL: de ella
    /// dependen todos los reportes, y si se escribiera a mano una merma
    /// parcial mal registrada inflaría la pérdida del mes.
    /// </summary>
    public int CostoPerdido { get; private set; }

    public int? UsuarioId { get; set; }
    public bool Revertida { get; set; }
    public int? RevertidaPor { get; set; }
    public DateTimeOffset? RevertidaEn { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Producto Producto { get; set; } = null!;
    public Usuario? Usuario { get; set; }
    public Lote? Lote { get; set; }
}
