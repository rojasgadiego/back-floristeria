using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Un turno de caja con su arqueo.
///
/// Los totales NO son columnas de la tabla: se derivan de las boletas cada
/// vez que se consultan. Una columna acumulada se desincroniza en cuanto
/// alguien anula una venta, y el arqueo dejaría de significar nada.
///
/// La excepción es `EfectivoEsperado` en una caja cerrada: ese sí se congela
/// al cerrar, porque el arqueo histórico es "lo que se contó esa noche contra
/// lo que había que contar esa noche" y no debe moverse después.
/// </summary>
public class Caja
{
    public int Id { get; set; }
    public EstadoCaja Estado { get; set; }

    public int FondoInicial { get; set; }

    public DateTime AbiertaEn { get; set; }
    public string? AbiertaPor { get; set; }
    public int AbiertaPorId { get; set; }

    public DateTime? CerradaEn { get; set; }
    public string? CerradaPor { get; set; }
    public string? NotaCierre { get; set; }

    // ─── Por medio de pago, sin las anuladas ───
    public int Efectivo { get; set; }
    public int Debito { get; set; }
    public int Credito { get; set; }
    public int Transferencia { get; set; }

    public int TotalVendido { get; set; }
    public long Boletas { get; set; }

    /// <summary>Cuenta aparte: no mueven plata, pero cuatro anulaciones son información.</summary>
    public long Anuladas { get; set; }

    public int TotalDescuentos { get; set; }
    public int PuntosOtorgados { get; set; }
    public int PuntosCanjeados { get; set; }

    /// <summary>Lo que debería haber físicamente: fondo + efectivo recibido.</summary>
    public int EnCajon { get; set; }

    public int EfectivoEsperado { get; set; }
    public int? EfectivoContado { get; set; }

    /// <summary>Contado menos esperado. Negativo es faltante, positivo sobrante.</summary>
    public int? Diferencia { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}
