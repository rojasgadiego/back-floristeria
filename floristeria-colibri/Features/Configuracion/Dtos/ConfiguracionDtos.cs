using System.ComponentModel.DataAnnotations;

namespace Colibri.Api.Features.Configuracion.Dtos;

/* ===================== Lectura ===================== */

public class ConfiguracionDto
{
    public AjustesLocalDto Local { get; set; } = new();
    public AjustesTicketDto Ticket { get; set; } = new();
    public AjustesVentaDto Venta { get; set; } = new();
    public AjustesClubDto Club { get; set; } = new();

    public DateTimeOffset? ActualizadoEn { get; set; }
    public string? ActualizadoPor { get; set; }
}

/* ===================== Secciones ===================== */

public class AjustesLocalDto
{
    [Required(ErrorMessage = "El nombre del local es obligatorio.")]
    [StringLength(160, MinimumLength = 2)]
    public string Nombre { get; set; } = "Floristería Colibrí";

    [StringLength(200)]
    public string? Giro { get; set; }

    /// <summary>RUT del local. Se valida el dígito verificador.</summary>
    [StringLength(20)]
    public string? Rut { get; set; }

    [StringLength(240)]
    public string? Direccion { get; set; }

    [StringLength(80)]
    public string? Comuna { get; set; }

    [StringLength(80)]
    public string? Ciudad { get; set; }

    [StringLength(40)]
    public string? Telefono { get; set; }

    [EmailAddress(ErrorMessage = "El correo no tiene un formato válido.")]
    [StringLength(160)]
    public string? Correo { get; set; }

    [StringLength(80)]
    public string? Instagram { get; set; }
}

public class AjustesTicketDto
{
    [StringLength(200)]
    public string Mensaje { get; set; } = "¡Gracias por preferirnos!";

    /// <summary>
    /// El ticket NO es boleta electrónica: la real se emite al SII con un
    /// emisor de DTE. Esta leyenda lo deja claro, y cambiarla para que
    /// parezca tributaria sería un problema legal, no de software.
    /// </summary>
    [StringLength(200)]
    public string Leyenda { get; set; } = "Documento no tributario · conserve su ticket";

    public bool MostrarPuntos { get; set; } = true;
}

public class AjustesVentaDto
{
    /// <summary>
    /// Tasa de IVA. Cambiarla NO reescribe las boletas ya emitidas: cada
    /// venta guarda la tasa con que se calculó.
    /// </summary>
    [Range(0, 100, ErrorMessage = "El IVA debe estar entre 0 y 100.")]
    public decimal Iva { get; set; } = 19;

    /// <summary>
    /// Hasta cuánto puede rebajar quien atiende sin pedir autorización.
    /// Por encima, la venta exige credenciales de una administradora.
    ///
    /// Subirlo mucho es el atajo más rentable para vaciar un punto de venta.
    /// </summary>
    [Range(0, int.MaxValue)]
    public int DescuentoSinAutorizacion { get; set; } = 5000;
}

public class AjustesClubDto
{
    public bool Activo { get; set; } = true;

    /// <summary>Cuántos pesos de compra otorgan un punto.</summary>
    [Range(1, int.MaxValue, ErrorMessage = "Debe ser al menos 1 peso por punto.")]
    public int PuntosPorPeso { get; set; } = 1000;

    /// <summary>
    /// Cuántos pesos vale un punto al canjearlo.
    /// Cambiarlo revalúa TODOS los saldos existentes de inmediato.
    /// </summary>
    [Range(1, int.MaxValue, ErrorMessage = "Un punto debe valer al menos 1 peso.")]
    public int ValorPunto { get; set; } = 50;

    [Range(1, int.MaxValue)]
    public int CanjeMinimo { get; set; } = 50;
}

/* ===================== Impacto ===================== */

/// <summary>
/// Qué mueve un cambio antes de aplicarlo. El club de puntos es un
/// compromiso con los clientes: cambiar el valor del punto no es ajustar
/// una preferencia, es revaluar una deuda.
/// </summary>
public class ImpactoClubDto
{
    public int ClientesConPuntos { get; set; }
    public long PuntosEnCirculacion { get; set; }

    /// <summary>Lo que valen hoy, en pesos.</summary>
    public long CompromisoActual { get; set; }

    /// <summary>Lo que valdrían con el valor propuesto.</summary>
    public long CompromisoNuevo { get; set; }

    public long Diferencia { get; set; }

    /// <summary>Texto listo para mostrar como advertencia. Null si no hay cambio.</summary>
    public string? Advertencia { get; set; }
}

public class GuardarClubRequest : AjustesClubDto
{
    /// <summary>
    /// Confirma que se entiende el impacto de revaluar los puntos. Es
    /// obligatorio cuando el valor del punto cambia y hay saldos vigentes.
    /// </summary>
    public bool ConfirmaRevaluacion { get; set; }
}