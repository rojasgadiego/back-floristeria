namespace Colibri.Api.Dto.Requests;

public class ClienteRequest
{
    /// <summary>
    /// Obligatorio: es la llave con que el vendedor busca la ficha en el
    /// mesón. Se compara normalizado, así que da lo mismo con puntos, sin
    /// puntos o con guion.
    /// </summary>
    public string Rut { get; set; } = string.Empty;

    public string Nombre { get; set; } = string.Empty;
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }

    /// <summary>Día y mes van juntos: uno sin el otro no sirve para la campaña.</summary>
    public int? CumpleMes { get; set; }
    public int? CumpleDia { get; set; }

    public string? Notas { get; set; }
}

public class AjustePuntosRequest
{
    /// <summary>Con signo: positiva regala, negativa descuenta.</summary>
    public int Cantidad { get; set; }

    /// <summary>
    /// Obligatorio. Los puntos son dinero: el local le debe al cliente lo
    /// que valen, y un saldo que no cuadra tiene que poder explicarse.
    /// </summary>
    public string Motivo { get; set; } = string.Empty;
}

public class ClienteFiltro
{
    public string? Buscar { get; set; }
    public bool? Activo { get; set; } = true;
    public bool? ConPuntos { get; set; }
    public int? CumpleMes { get; set; }

    /// <summary>Para campañas de recuperación: quiénes se alejaron.</summary>
    public int? SinComprarDias { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 100;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;
        TamanoReal = Tamano switch { null or < 1 => 100, > 300 => 300, _ => Tamano.Value };
        Buscar = string.IsNullOrWhiteSpace(Buscar) ? null : Buscar.Trim();
    }
}

public class PaginaFiltro
{
    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal => Pagina is null or < 1 ? 1 : Pagina.Value;
    public int TamanoReal => Tamano switch { null or < 1 => 20, > 100 => 100, _ => Tamano.Value };
}
