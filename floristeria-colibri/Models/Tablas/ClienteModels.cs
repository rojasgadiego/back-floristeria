using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// La ficha con su historial pegado. Los derivados se calculan cada vez en
/// vez de guardarse: un contador de compras se desincroniza en cuanto se
/// anula una boleta.
/// </summary>
public class Cliente
{
    public int Id { get; set; }

    /// <summary>La llave con que el vendedor busca en el mesón.</summary>
    public string Rut { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }

    public short? CumpleMes { get; set; }
    public short? CumpleDia { get; set; }

    public string? Notas { get; set; }

    public int Puntos { get; set; }

    /// <summary>
    /// Lo que el local le debe si canjea todo, con el valor de HOY: los
    /// puntos valen lo que dice la configuración ahora, no lo que valían
    /// cuando se ganaron.
    /// </summary>
    public int ValorPuntos { get; set; }

    public bool Activo { get; set; }

    // ─── Historial. Solo ventas no anuladas. ───

    public long Compras { get; set; }
    public long TotalGastado { get; set; }
    public decimal? TicketPromedio { get; set; }
    public DateTime? UltimaCompra { get; set; }

    /// <summary>Null si nunca compró. Sirve para campañas de recuperación.</summary>
    public int? DiasSinComprar { get; set; }

    /// <summary>Negativo si el cumpleaños ya pasó este año.</summary>
    public int? DiasParaCumple { get; set; }

    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>
/// La ficha completa. `Frecuentes` es lo que la vuelve útil al atender:
/// saber que siempre pide lilium blanco permite ofrecérselo antes de que
/// pregunte.
/// </summary>
public class ClienteDetalle : Cliente
{
    public IReadOnlyList<CompraCliente> Compras7 { get; set; } = Array.Empty<CompraCliente>();
    public IReadOnlyList<MovimientoPuntos> Puntos7 { get; set; } = Array.Empty<MovimientoPuntos>();
    public IReadOnlyList<ProductoFrecuente> Frecuentes { get; set; } = Array.Empty<ProductoFrecuente>();
}

public class CompraCliente
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateTime CreadoEn { get; set; }

    public int Total { get; set; }
    public int DescuentoTotal { get; set; }
    public MedioPago MedioPago { get; set; }

    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }

    public long Lineas { get; set; }
    public string? Vendedor { get; set; }
    public bool Anulada { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>
/// Una línea del libro de puntos. Cada una guarda su saldo resultante: los
/// puntos son dinero, y un saldo que no cuadra tiene que poder rastrearse
/// hasta el movimiento donde se desvió.
/// </summary>
public class MovimientoPuntos
{
    public int Id { get; set; }

    /// <summary>Con signo: positiva suma, negativa resta.</summary>
    public int Cantidad { get; set; }

    public int? SaldoResultante { get; set; }
    public string Motivo { get; set; } = string.Empty;

    public int? VentaId { get; set; }
    public string? Folio { get; set; }

    public string? Usuario { get; set; }
    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

public class ProductoFrecuente
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    /// <summary>En cuántas boletas distintas apareció.</summary>
    public long Veces { get; set; }

    public long Unidades { get; set; }
    public long Total { get; set; }
}

public class ClienteCumpleanos
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }

    public short? CumpleMes { get; set; }
    public short? CumpleDia { get; set; }

    /// <summary>Negativo si ya pasó, para separar "ya fue" de "viene".</summary>
    public int? DiasFaltantes { get; set; }

    public int Puntos { get; set; }
    public long Compras { get; set; }
    public long TotalGastado { get; set; }
    public DateTime? UltimaCompra { get; set; }
}
