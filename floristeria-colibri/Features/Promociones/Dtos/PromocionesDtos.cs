using System.ComponentModel.DataAnnotations;
using Colibri.Api.Common.Paginacion;

namespace Colibri.Api.Features.Promociones.Dtos;

/* ===================== Lectura ===================== */

public class PromocionDto
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Descripcion { get; set; }

    /// <summary>porcentaje o monto.</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>Porcentaje si el tipo es porcentaje; pesos si es monto.</summary>
    public int Valor { get; set; }

    /// <summary>boleta, categoria o producto.</summary>
    public string Alcance { get; set; } = string.Empty;

    public int? CategoriaId { get; set; }
    public string? Categoria { get; set; }
    public int? ProductoId { get; set; }
    public string? Producto { get; set; }

    /// <summary>Compra mínima de la boleta para que aplique.</summary>
    public int Minimo { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    /// <summary>0 = domingo. Vacío significa todos los días.</summary>
    public short[] Dias { get; set; } = Array.Empty<short>();

    /// <summary>Para mostrar: "martes" o "todos los días".</summary>
    public string DiasTexto { get; set; } = string.Empty;

    public bool Activa { get; set; }

    /// <summary>Si corre hoy: activa, vigente y en día habilitado.</summary>
    public bool VigenteHoy { get; set; }

    /// <summary>Por qué no corre hoy. Null si sí corre.</summary>
    public string? MotivoNoVigente { get; set; }

    public int Usos { get; set; }

    /// <summary>Cuánto se ha descontado con esta promoción.</summary>
    public long DescuentoAcumulado { get; set; }

    public DateTimeOffset? UltimoUso { get; set; }
}

public class PromocionDetalleDto : PromocionDto
{
    /// <summary>Promoción promedio otorgada por boleta.</summary>
    public int DescuentoPromedio { get; set; }

    /// <summary>Venta bruta de las boletas donde se aplicó.</summary>
    public long VentaAsociada { get; set; }

    /// <summary>
    /// Otras promociones que pueden aplicar a la misma boleta. El punto de
    /// venta ofrece la más conveniente, así que la peor nunca se usa.
    /// </summary>
    public IReadOnlyList<ConflictoDto> Conflictos { get; set; } = Array.Empty<ConflictoDto>();
}

public class ConflictoDto
{
    public int PromocionId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Tipo { get; set; } = string.Empty;
    public int Valor { get; set; }
    public bool Activa { get; set; }

    /// <summary>Explicación de por qué se pisan.</summary>
    public string Detalle { get; set; } = string.Empty;
}

/* ===================== Simulación ===================== */

public class SimulacionDto
{
    public string Nombre { get; set; } = string.Empty;
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    /// <summary>Boletas del período que se evaluaron.</summary>
    public int BoletasEvaluadas { get; set; }

    /// <summary>En cuántas habría aplicado.</summary>
    public int BoletasQueAplican { get; set; }

    public decimal PorcentajeCobertura { get; set; }

    /// <summary>Cuánto habría descontado en total.</summary>
    public long DescuentoTotal { get; set; }

    public int DescuentoPromedio { get; set; }

    /// <summary>Venta bruta de las boletas alcanzadas.</summary>
    public long VentaAlcanzada { get; set; }

    /// <summary>Descuento sobre la venta total del período.</summary>
    public decimal ImpactoSobreVentas { get; set; }

    /// <summary>Las boletas donde más habría descontado.</summary>
    public IReadOnlyList<EjemploSimulacionDto> Ejemplos { get; set; }
        = Array.Empty<EjemploSimulacionDto>();
}

public class EjemploSimulacionDto
{
    public string Folio { get; set; } = string.Empty;
    public DateTimeOffset Fecha { get; set; }
    public int Bruto { get; set; }
    public int DescuentoSimulado { get; set; }
    public int TotalConPromocion { get; set; }
}

public class SimularRequest : GuardarPromocionRequest
{
    /// <summary>
    /// Inicio del período de ventas a evaluar. Sin fecha, toma los últimos
    /// 30 días.
    ///
    /// No confundir con Desde/Hasta, que son la vigencia de la promoción:
    /// una cosa es desde cuándo correría, y otra contra qué ventas pasadas
    /// se la quiere medir.
    /// </summary>
    public DateOnly? PeriodoDesde { get; set; }

    /// <summary>Término del período de ventas a evaluar.</summary>
    public DateOnly? PeriodoHasta { get; set; }
}

/* ===================== Escritura ===================== */

public class GuardarPromocionRequest
{
    [Required(ErrorMessage = "El nombre es obligatorio.")]
    [StringLength(160, MinimumLength = 3)]
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Lo que lee el cliente: "10% en compras sobre $20.000".</summary>
    [StringLength(400)]
    public string? Descripcion { get; set; }

    /// <summary>porcentaje o monto.</summary>
    [Required(ErrorMessage = "El tipo es obligatorio.")]
    public string Tipo { get; set; } = "porcentaje";

    [Range(1, int.MaxValue, ErrorMessage = "El valor debe ser mayor que cero.")]
    public int Valor { get; set; }

    /// <summary>
    /// boleta descuenta sobre el total; categoria sobre los productos de esa
    /// categoría; producto sobre uno en particular.
    /// </summary>
    [Required(ErrorMessage = "El alcance es obligatorio.")]
    public string Alcance { get; set; } = "boleta";

    /// <summary>Obligatorio si el alcance es categoria.</summary>
    public int? CategoriaId { get; set; }

    /// <summary>Obligatorio si el alcance es producto.</summary>
    public int? ProductoId { get; set; }

    /// <summary>Compra mínima de la boleta. Cero significa sin mínimo.</summary>
    [Range(0, int.MaxValue)]
    public int Minimo { get; set; }

    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    /// <summary>
    /// Días en que corre. 0 = domingo, 6 = sábado.
    /// Lista vacía significa todos los días.
    /// </summary>
    public List<short> Dias { get; set; } = new();
}

/* ===================== Filtros ===================== */

public class PromocionFiltro : ParametrosPagina
{
    /// <summary>Null trae todas; true solo las activas.</summary>
    public bool? Activa { get; set; }

    /// <summary>Solo las que corren hoy.</summary>
    public bool SoloVigentes { get; set; }

    /// <summary>boleta, categoria o producto.</summary>
    public string? Alcance { get; set; }
}