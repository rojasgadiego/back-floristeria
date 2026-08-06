using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Compras.Dtos;

/* ===================== Proveedores ===================== */

public class ProveedorDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Rut { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
    public bool Activo { get; set; }

    /// <summary>Compras recibidas, para negociar con datos a la vista.</summary>
    public int Compras { get; set; }
    public long TotalComprado { get; set; }
    public DateOnly? UltimaCompra { get; set; }
}

public class GuardarProveedorRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(160, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    [StringLength(20)]
    public string? Rut { get; set; }

    [StringLength(120)]
    public string? Contacto { get; set; }

    [StringLength(40)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    public string? Correo { get; set; }

    [StringLength(240)]
    public string? Direccion { get; set; }

    [StringLength(600)]
    public string? Notas { get; set; }
}

public class ProveedorFiltro : ParametrosPagina
{
    public bool? Activo { get; set; } = true;
}

/* ===================== Presentaciones ===================== */

public class PresentacionDto
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;

    /// <summary>vara, paquete o caja.</summary>
    public string Tipo { get; set; } = string.Empty;

    public int Paquetes { get; set; }
    public int VarasPorPaquete { get; set; }

    /// <summary>Calculada por la base: paquetes × varas por paquete.</summary>
    public int VarasTotales { get; set; }

    public bool Predeterminada { get; set; }
    public bool Activa { get; set; }
}

public class GuardarPresentacionRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(120, MinimumLength = 2)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>vara, paquete o caja.</summary>
    [Required(ErrorMessage = "El tipo es obligatorio.")]
    public string Tipo { get; set; } = "paquete";

    /// <summary>Cuántos paquetes trae. Una caja de rosas: 12.</summary>
    [Range(1, 1000)]
    public int Paquetes { get; set; } = 1;

    /// <summary>Varas por paquete. Depende de la especie: 25 en rosas, 10 en maule.</summary>
    [Range(1, 10000)]
    public int VarasPorPaquete { get; set; }

    public bool Predeterminada { get; set; }
}

/* ===================== Compras ===================== */

public class CompraDto
{
    public int Id { get; set; }
    public string Folio { get; set; } = string.Empty;
    public int ProveedorId { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public DateOnly Fecha { get; set; }
    public string? Documento { get; set; }

    /// <summary>borrador, recibida o anulada.</summary>
    public string Estado { get; set; } = string.Empty;

    public int Neto { get; set; }
    public int Iva { get; set; }
    public int Total { get; set; }
    public string? Notas { get; set; }
    public string? Usuario { get; set; }
    public DateTimeOffset? RecibidaEn { get; set; }
    public int Lineas { get; set; }

    /// <summary>Varas que ingresaron o van a ingresar.</summary>
    public int VarasTotales { get; set; }
}

public class CompraDetalleDto : CompraDto
{
    public IReadOnlyList<CompraItemDto> Items { get; set; } = Array.Empty<CompraItemDto>();

    /// <summary>Lotes generados al recibir. Vacío mientras esté en borrador.</summary>
    public IReadOnlyList<LoteGeneradoDto> Lotes { get; set; } = Array.Empty<LoteGeneradoDto>();
}

public class CompraItemDto
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int PresentacionId { get; set; }
    public string Presentacion { get; set; } = string.Empty;

    /// <summary>Cuántas cajas o paquetes.</summary>
    public int Cantidad { get; set; }

    /// <summary>Lo que cuesta UNA caja o UN paquete.</summary>
    public int CostoUnitario { get; set; }

    public int VarasTotales { get; set; }
    public decimal CostoPorVara { get; set; }
    public int Subtotal { get; set; }

    /// <summary>Costo por vara de la compra anterior del mismo producto.</summary>
    public decimal? CostoAnterior { get; set; }
}

public class LoteGeneradoDto
{
    public int Id { get; set; }

    /// <summary>Lo que va en la etiqueta QR.</summary>
    public string Codigo { get; set; } = string.Empty;

    public string Producto { get; set; } = string.Empty;
    public int Varas { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
}

public class LineaCompraRequest
{
    [Required]
    public int ProductoId { get; set; }

    [Required]
    public int PresentacionId { get; set; }

    [Range(1, 100000, ErrorMessage = "La cantidad debe ser al menos 1.")]
    public int Cantidad { get; set; }

    /// <summary>Lo que cuesta UNA caja o UN paquete, no una vara.</summary>
    [Range(0, int.MaxValue, ErrorMessage = "El costo no puede ser negativo.")]
    public int CostoUnitario { get; set; }
}

public class GuardarCompraRequest
{
    [Required(ErrorMessage = "El proveedor es obligatorio.")]
    public int ProveedorId { get; set; }

    public DateOnly? Fecha { get; set; }

    /// <summary>N° de factura o guía de despacho.</summary>
    [StringLength(60)]
    public string? Documento { get; set; }

    [StringLength(600)]
    public string? Notas { get; set; }

    /// <summary>IVA del documento. 19 salvo excepción.</summary>
    [Range(0, 100)]
    public decimal IvaTasa { get; set; } = 19;

    [MinLength(1, ErrorMessage = "La compra necesita al menos una línea.")]
    public List<LineaCompraRequest> Items { get; set; } = new();
}

public class CompraFiltro : ParametrosPagina
{
    public int? ProveedorId { get; set; }

    /// <summary>borrador, recibida o anulada.</summary>
    public string? Estado { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
}

public class ResultadoRecepcionDto
{
    public int CompraId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Proveedor { get; set; } = string.Empty;
    public int LotesGenerados { get; set; }
    public int VarasIngresadas { get; set; }
    public IReadOnlyList<LoteGeneradoDto> Lotes { get; set; } = Array.Empty<LoteGeneradoDto>();
}

public class EvolucionCostoDto
{
    public DateOnly Fecha { get; set; }
    public string Proveedor { get; set; } = string.Empty;
    public string? Presentacion { get; set; }
    public int Cantidad { get; set; }
    public int CostoUnitario { get; set; }
    public decimal CostoPorVara { get; set; }
    public decimal? CostoAnterior { get; set; }
    public decimal? Variacion { get; set; }
}