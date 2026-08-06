using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Clientes.Dtos;

/* ===================== Lectura ===================== */

public class ClienteDto
{
    public int Id { get; set; }

    /// <summary>Con formato para mostrar: 12.345.678-5</summary>
    public string Rut { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }

    public short? CumpleMes { get; set; }
    public short? CumpleDia { get; set; }

    /// <summary>Para mostrar: "14 de marzo". Null si no tiene fecha.</summary>
    public string? Cumpleanos { get; set; }

    public string? Notas { get; set; }
    public int Puntos { get; set; }

    /// <summary>Cuánto valen esos puntos en pesos, según la configuración.</summary>
    public int ValorPuntos { get; set; }

    public bool Activo { get; set; }
    public DateTimeOffset CreadoEn { get; set; }

    // --- Historial acumulado ---
    public int Compras { get; set; }
    public long TotalComprado { get; set; }
    public DateTimeOffset? UltimaCompra { get; set; }

    /// <summary>Días desde la última compra. Sirve para detectar quién se alejó.</summary>
    public int? DiasSinComprar { get; set; }
}

public class ClienteDetalleDto : ClienteDto
{
    /// <summary>Compra promedio: el ticket típico de este cliente.</summary>
    public int TicketPromedio { get; set; }

    public IReadOnlyList<CompraClienteDto> UltimasCompras { get; set; }
        = Array.Empty<CompraClienteDto>();

    public IReadOnlyList<MovimientoPuntosDto> MovimientosPuntos { get; set; }
        = Array.Empty<MovimientoPuntosDto>();

    /// <summary>Lo que más compra, para sugerir en el mesón.</summary>
    public IReadOnlyList<ProductoFrecuenteDto> ProductosFrecuentes { get; set; }
        = Array.Empty<ProductoFrecuenteDto>();
}

public class CompraClienteDto
{
    public int VentaId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; }
    public int Total { get; set; }
    public int DescuentoTotal { get; set; }
    public string MedioPago { get; set; } = string.Empty;
    public int PuntosGanados { get; set; }
    public int PuntosCanjeados { get; set; }
    public bool Anulada { get; set; }
    public int Lineas { get; set; }
}

public class MovimientoPuntosDto
{
    public int Id { get; set; }
    public DateTimeOffset Fecha { get; set; }

    /// <summary>Positivo acumula, negativo canjea.</summary>
    public int Cantidad { get; set; }

    public int? SaldoResultante { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Folio { get; set; }
    public string? Usuario { get; set; }
}

public class ProductoFrecuenteDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Veces { get; set; }
    public int Unidades { get; set; }
}

/// <summary>Cliente que cumple años, para la campaña del mes.</summary>
public class CumpleanosDto
{
    public int ClienteId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public short Dia { get; set; }
    public short Mes { get; set; }

    /// <summary>Negativo si ya pasó este mes.</summary>
    public int DiasFaltantes { get; set; }

    public int Puntos { get; set; }
    public long TotalComprado { get; set; }
    public string? Notas { get; set; }
}

/* ===================== Escritura ===================== */

public class GuardarClienteRequest
{
    /// <summary>
    /// Se acepta con o sin puntos y guion. Se valida el dígito verificador:
    /// un RUT mal tipeado crea una ficha que después nadie encuentra.
    /// </summary>
    [Required(ErrorMessage = "El RUT es obligatorio.")]
    [StringLength(20)]
    public string Rut { get; set; } = string.Empty;

    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(160, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(40)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(160)]
    public string? Correo { get; set; }

    [StringLength(240)]
    public string? Direccion { get; set; }

    [Range(1, 12, ErrorMessage = "El mes de cumpleaños debe estar entre 1 y 12.")]
    public short? CumpleMes { get; set; }

    [Range(1, 31, ErrorMessage = "El día de cumpleaños debe estar entre 1 y 31.")]
    public short? CumpleDia { get; set; }

    /// <summary>Preferencias, alergias, con quién trabaja. Lo que sirve al atender.</summary>
    [StringLength(1000)]
    public string? Notas { get; set; }
}

public class AjustarPuntosRequest
{
    /// <summary>Con signo: positivo regala, negativo descuenta.</summary>
    [Required(ErrorMessage = "La cantidad es obligatoria.")]
    public int Cantidad { get; set; }

    [Required(ErrorMessage = "El motivo es obligatorio.")]
    [StringLength(200, MinimumLength = 5,
        ErrorMessage = "Explica el motivo con al menos 5 caracteres.")]
    public string Motivo { get; set; } = string.Empty;
}

/* ===================== Filtros ===================== */

public class ClienteFiltro : ParametrosPagina
{
    /// <summary>Null trae todos; true solo activos.</summary>
    public bool? Activo { get; set; } = true;

    /// <summary>Solo los que tienen puntos para canjear.</summary>
    public bool ConPuntos { get; set; }

    /// <summary>Cumpleaños en este mes. 1 a 12.</summary>
    [Range(1, 12)]
    public short? CumpleMes { get; set; }

    /// <summary>Sin comprar hace más de N días: los que se alejaron.</summary>
    public int? SinComprarDias { get; set; }
}