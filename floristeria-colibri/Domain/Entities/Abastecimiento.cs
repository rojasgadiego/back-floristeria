namespace Colibri.Api.Domain.Entities;

public class Proveedor
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string? Rut { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public ICollection<Compra> Compras { get; set; } = new List<Compra>();
}

/// <summary>
/// Unidad en que se compra un producto. Las varas por paquete cambian según
/// la especie, así que la equivalencia vive por producto y no en una constante.
/// VarasTotales es columna generada en la base: no se escribe desde acá.
/// </summary>
public class Presentacion
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string Nombre { get; set; } = null!;
    public TipoPresentacion Tipo { get; set; }

    /// <summary>Cuántos paquetes trae. Una caja de rosas: 12.</summary>
    public int Paquetes { get; set; } = 1;
    public int VarasPorPaquete { get; set; }

    /// <summary>Calculada por PostgreSQL como Paquetes × VarasPorPaquete.</summary>
    public int VarasTotales { get; private set; }

    public bool Predeterminada { get; set; }
    public bool Activa { get; set; } = true;

    public Producto Producto { get; set; } = null!;
}

public class Compra
{
    public int Id { get; set; }
    public string Folio { get; set; } = null!;
    public int ProveedorId { get; set; }
    public DateOnly Fecha { get; set; }

    /// <summary>N° de factura o guía de despacho.</summary>
    public string? Documento { get; set; }

    public EstadoCompra Estado { get; set; } = EstadoCompra.borrador;
    public int Neto { get; set; }
    public int Iva { get; set; }
    public int Total { get; set; }
    public string? Notas { get; set; }
    public int? UsuarioId { get; set; }

    /// <summary>Al recibir se generan los lotes con su código QR.</summary>
    public DateTimeOffset? RecibidaEn { get; set; }

    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Proveedor Proveedor { get; set; } = null!;
    public Usuario? Usuario { get; set; }
    public ICollection<CompraItem> Items { get; set; } = new List<CompraItem>();
    public ICollection<Lote> Lotes { get; set; } = new List<Lote>();
}

public class CompraItem
{
    public int Id { get; set; }
    public int CompraId { get; set; }
    public int ProductoId { get; set; }
    public int PresentacionId { get; set; }

    /// <summary>Cuántas cajas o paquetes se compraron.</summary>
    public int Cantidad { get; set; }

    /// <summary>Lo que cuesta UNA caja o UN paquete.</summary>
    public int CostoUnitario { get; set; }

    public int VarasTotales { get; set; }

    /// <summary>
    /// Único monto con decimales del sistema: repartir el costo de una caja
    /// entre 300 varas rara vez da un entero.
    /// </summary>
    public decimal CostoPorVara { get; set; }

    public Compra Compra { get; set; } = null!;
    public Producto Producto { get; set; } = null!;
    public Presentacion Presentacion { get; set; } = null!;
}

/// <summary>
/// El paquete físico que se recibe, se etiqueta con QR y se consume.
/// Codigo es lo que va dentro del QR y lo que permite reimprimir la etiqueta
/// si se pierde: en el papel no vive ningún dato, solo la identidad del lote.
/// </summary>
public class Lote
{
    public int Id { get; set; }

    /// <summary>Formato LOT-000123. Corto para poder tipearlo si el QR se borra.</summary>
    public string Codigo { get; set; } = null!;

    public int ProductoId { get; set; }
    public int? CompraId { get; set; }
    public int? CompraItemId { get; set; }
    public int? ProveedorId { get; set; }
    public int? PresentacionId { get; set; }

    public DateOnly FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }

    public int VarasIniciales { get; set; }
    public int VarasDisponibles { get; set; }
    public decimal CostoPorVara { get; set; }

    public EstadoLote Estado { get; set; } = EstadoLote.activo;


    /// <summary>
    /// De qué lote vienen estas varas. Null en una compra normal.
    /// </summary>
    public int? OrigenLoteId { get; set; }

    /// <summary>
    /// En qué estado volvió la flor. Null significa flor de primera,
    /// recién comprada.
    /// </summary>
    public CalidadReingreso? Calidad { get; set; }

    /// <summary>
    /// Precio propio del lote. Null = se vende al precio del producto.
    ///
    /// Ponerle precio tiene una consecuencia: el lote sale del FIFO
    /// automático y solo se vende escaneándolo. Si entrara en el reparto
    /// automático, una venta sin escaneo cobraría flor de segunda a precio
    /// de primera —o al revés— sin que nadie lo note en el mesón.
    /// </summary>
    public int? PrecioUnitario { get; set; }

    /// <summary>Dónde está físicamente: 'Cámara 1, balde 3'.</summary>
    public string? Ubicacion { get; set; }

    public string? Notas { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Producto Producto { get; set; } = null!;
    public Compra? Compra { get; set; }
    public Proveedor? Proveedor { get; set; }
    public Presentacion? Presentacion { get; set; }

    /// <summary>Lote del que provienen estas varas, si es recuperación.</summary>
    public Lote? OrigenLote { get; set; }
}
