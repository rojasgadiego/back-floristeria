using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Ventas.Dtos;

public class CajaDto
{
    public int Id { get; set; }

    /// <summary>abierta o cerrada.</summary>
    public string Estado { get; set; } = string.Empty;

    public int FondoInicial { get; set; }
    public DateTimeOffset AbiertaEn { get; set; }
    public string AbiertaPor { get; set; } = string.Empty;

    public DateTimeOffset? CerradaEn { get; set; }
    public string? CerradaPor { get; set; }

    /// <summary>Fondo inicial más las ventas en efectivo.</summary>
    public int? EfectivoEsperado { get; set; }

    public int? EfectivoContado { get; set; }

    /// <summary>Contado menos esperado. Negativo es faltante.</summary>
    public int? Diferencia { get; set; }

    public string? NotaCierre { get; set; }
}

/// <summary>Lo que hay en el turno hasta este momento.</summary>
public class ResumenCajaDto : CajaDto
{
    public int Boletas { get; set; }
    public int Anuladas { get; set; }
    public int TotalVendido { get; set; }
    public int TotalDescuentos { get; set; }

    /// <summary>Solo el efectivo: es lo que debería haber en el cajón.</summary>
    public int Efectivo { get; set; }

    public int Debito { get; set; }
    public int Credito { get; set; }
    public int Transferencia { get; set; }

    /// <summary>Fondo inicial más el efectivo recibido.</summary>
    public int EnCajon { get; set; }

    public int PuntosOtorgados { get; set; }
    public int PuntosCanjeados { get; set; }
}

public class AbrirCajaRequest
{
    [Range(0, int.MaxValue, ErrorMessage = "El fondo inicial no puede ser negativo.")]
    public int FondoInicial { get; set; }
}

public class CerrarCajaRequest
{
    /// <summary>Lo que se contó físicamente en el cajón.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "El efectivo contado no puede ser negativo.")]
    public int EfectivoContado { get; set; }

    [StringLength(600)]
    public string? Nota { get; set; }
}

public class CajaFiltro : ParametrosPagina
{
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}