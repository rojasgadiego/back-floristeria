namespace Colibri.Api.Domain.Entities;

public class Cliente
{
    public int Id { get; set; }
    public string Rut { get; set; } = null!;
    public string Nombre { get; set; } = null!;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }

    /// <summary>
    /// Mes y día por separado: el año no aporta, y así "cumpleaños del mes"
    /// es un filtro directo sobre un índice.
    /// </summary>
    public short? CumpleMes { get; set; }
    public short? CumpleDia { get; set; }

    public string? Notas { get; set; }
    public int Puntos { get; set; }
    public bool Activo { get; set; } = true;
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public ICollection<Venta> Compras { get; set; } = new List<Venta>();
    public ICollection<PuntoMovimiento> MovimientosPuntos { get; set; } = new List<PuntoMovimiento>();
}

/// <summary>
/// Los puntos llevan su propio libro: si un saldo no cuadra, tiene que
/// haber dónde mirar.
/// </summary>
public class PuntoMovimiento
{
    public int Id { get; set; }
    public int ClienteId { get; set; }

    /// <summary>Positivo acumula, negativo canjea.</summary>
    public int Cantidad { get; set; }
    public int? SaldoResultante { get; set; }

    public string Motivo { get; set; } = null!;
    public int? VentaId { get; set; }
    public int? UsuarioId { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    public Cliente Cliente { get; set; } = null!;
    public Venta? Venta { get; set; }
    public Usuario? Usuario { get; set; }
}

/// <summary>
/// Guarda la REGLA, nunca un monto: el descuento se recalcula con la boleta
/// que haya en caja en ese momento.
/// </summary>
public class Promocion
{
    public int Id { get; set; }
    public string Nombre { get; set; } = null!;
    public string? Descripcion { get; set; }
    public TipoPromocion Tipo { get; set; }

    /// <summary>Porcentaje si Tipo es porcentaje; pesos si es monto.</summary>
    public int Valor { get; set; }

    public AlcancePromocion Alcance { get; set; } = AlcancePromocion.boleta;
    public int? CategoriaId { get; set; }
    public int? ProductoId { get; set; }
    public int Minimo { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    /// <summary>Días en que corre. 0 = domingo. Vacío significa todos los días.</summary>
    public short[] Dias { get; set; } = Array.Empty<short>();

    public bool Activa { get; set; } = true;
    public int Usos { get; set; }
    public DateTimeOffset CreadoEn { get; set; }
    public DateTimeOffset ActualizadoEn { get; set; }

    public Categoria? Categoria { get; set; }
    public Producto? Producto { get; set; }
}
