namespace Colibri.Api.Domain;

// Estos enum se mapean a los tipos ENUM nativos de PostgreSQL.
// El nombre de cada miembro debe coincidir exactamente con la etiqueta
// declarada en el SQL: PostgreSQL distingue mayúsculas, y por eso van
// en minúscula aunque rompa la convención de C#.

/// <summary>Rol del usuario. Define qué módulos ve y qué puede modificar.</summary>
public enum RolUsuario { admin, vendedor, bodega }

/// <summary>
/// Un producto simple tiene stock real; uno armado se define por su receta
/// y no tiene existencias propias.
/// </summary>
public enum TipoProducto { simple, armado }

/// <summary>Origen de una variación de stock.</summary>
public enum TipoMovimiento { alta, entrada, salida, ajuste, venta, consumo, armado, merma, baja }

public enum MedioPago { efectivo, debito, credito, transferencia }

public enum EstadoCaja { abierta, cerrada }

public enum TipoPromocion { porcentaje, monto }

/// <summary>Sobre qué base se calcula el descuento de una promoción.</summary>
public enum AlcancePromocion { boleta, categoria, producto }

public enum EstadoCotizacion { borrador, aprobada, cobrada, anulada }

/// <summary>Qué se descontó al vender: una unidad ya armada, o los tallos de la receta.</summary>
public enum TipoConsumo { listo, simple }

/// <summary>
/// Cómo llega la flor del proveedor. Una caja de rosas trae 12 paquetes de
/// 25 varas; una de maule, 12 paquetes de 10.
/// </summary>
public enum TipoPresentacion { vara, paquete, caja }

public enum EstadoLote { activo, agotado, descartado }

public enum EstadoCompra { borrador, recibida, anulada }


/// <summary>
/// Qué pasó con lo que salió del inventario. Decide si hay costo y si
/// vuelve al stock.
/// </summary>
public enum DestinoMerma
{
    /// <summary>Se botó: es costo.</summary>
    perdida,

    /// <summary>Vuelve al inventario, total o parcialmente.</summary>
    reingreso,

    /// <summary>Se devuelve al proveedor y lo abona: NO es costo.</summary>
    devolucion_proveedor
}

/// <summary>
/// En qué estado vuelve la flor. Decide si regresa a su lote o a uno aparte.
/// </summary>
public enum CalidadReingreso
{
    /// <summary>Como nueva: vuelve al lote original.</summary>
    optima,

    /// <summary>Sirve, pero se nota: balde aparte con precio menor.</summary>
    buena,

    /// <summary>Solo para relleno de arreglos, no para vender suelta.</summary>
    limitada
}