using System.Text.Json;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Las cuatro secciones juntas. El POS las necesita todas al arrancar, y
/// pedirlas de a una serían cuatro viajes para pintar una pantalla.
///
/// Cada sección viaja como JsonElement, no como una clase tipada: la forma
/// la decide el front, y agregarle el Instagram al local no debería obligar
/// a tocar C#, recompilar y desplegar.
/// </summary>
public class Configuracion
{
    /// <summary>Datos del negocio. Salen impresos en cada ticket.</summary>
    public JsonElement? Local { get; set; }

    /// <summary>Qué se imprime y qué no: mensaje, leyenda, si van los puntos.</summary>
    public JsonElement? Ticket { get; set; }

    /// <summary>IVA y umbral de descuento sin autorización.</summary>
    public JsonElement? Venta { get; set; }

    /// <summary>Puntos: si está activo, cuánto vale cada uno, mínimo de canje.</summary>
    public JsonElement? Club { get; set; }

    /// <summary>La más reciente de las cuatro secciones.</summary>
    public DateTime? ActualizadoEn { get; set; }

    public string? ActualizadoPor { get; set; }
}

/// <summary>
/// Qué mueve cambiar el valor del punto.
///
/// Los puntos que ya están en las cuentas valen lo que diga la
/// configuración HOY, no lo que valían cuando se ganaron. Subir de $10 a
/// $50 quintuplica de golpe una deuda que ya existe.
/// </summary>
public class ImpactoClub
{
    public long ClientesConPuntos { get; set; }
    public long PuntosVigentes { get; set; }

    public int ValorActual { get; set; }
    public int ValorNuevo { get; set; }

    /// <summary>Lo que valen hoy los puntos en circulación.</summary>
    public long PasivoActual { get; set; }

    /// <summary>Lo que valdrían con el valor nuevo.</summary>
    public long PasivoNuevo { get; set; }

    /// <summary>Positivo = el local queda debiendo más.</summary>
    public long Diferencia { get; set; }
}
