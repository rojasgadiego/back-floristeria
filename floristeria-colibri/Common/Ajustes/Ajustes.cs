namespace Colibri.Api.Common.Ajustes;

/// <summary>Datos del local, para el encabezado del ticket.</summary>
public class AjustesLocal
{
    public string Nombre { get; set; } = "Floristería Colibrí";
    public string? Giro { get; set; }
    public string? Rut { get; set; }
    public string? Direccion { get; set; }
    public string? Comuna { get; set; }
    public string? Ciudad { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Instagram { get; set; }
}

public class AjustesTicket
{
    public string Mensaje { get; set; } = "¡Gracias por preferirnos!";

    /// <summary>
    /// El ticket NO es boleta electrónica: la boleta real se emite al SII con
    /// un emisor de DTE. Esta leyenda lo deja claro a propósito.
    /// </summary>
    public string Leyenda { get; set; } = "Documento no tributario · conserve su ticket";

    public bool MostrarPuntos { get; set; } = true;
}

public class AjustesVenta
{
    public decimal Iva { get; set; } = 19;

    /// <summary>
    /// Hasta cuánto puede rebajar quien atiende sin pedir autorización.
    /// Por encima, se exigen las credenciales de una administradora.
    /// </summary>
    public int DescuentoSinAutorizacion { get; set; } = 5000;
}

public class AjustesClub
{
    public bool Activo { get; set; } = true;

    /// <summary>Cuántos pesos de compra otorgan un punto.</summary>
    public int PuntosPorPeso { get; set; } = 1000;

    /// <summary>Cuántos pesos vale un punto al canjearlo.</summary>
    public int ValorPunto { get; set; } = 50;

    /// <summary>Puntos mínimos para poder canjear.</summary>
    public int CanjeMinimo { get; set; } = 50;
}