namespace Colibri.Api.Dto.Requests;

public class AbrirCajaRequest
{
    /// <summary>
    /// Contra esto se calcula la diferencia al cerrar, así que hay que
    /// contarlo antes de escribirlo. Cero es válido, pero conviene que sea
    /// una decisión y no un descuido.
    /// </summary>
    public int FondoInicial { get; set; }
}

public class CerrarCajaRequest
{
    /// <summary>
    /// Solo se informa lo CONTADO. El esperado lo calcula el sistema desde
    /// las boletas: si se pudiera dictar, la diferencia dejaría de
    /// significar algo.
    /// </summary>
    public int EfectivoContado { get; set; }

    public string? Nota { get; set; }
}

public class CajaFiltro
{
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }

    /// <summary>
    /// Lo pone el endpoint desde el token, NO el cliente. Un vendedor solo ve
    /// los turnos que abrió o cerró; el admin los ve todos.
    /// </summary>
    public int? UsuarioId { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 30;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;
        TamanoReal = Tamano switch { null or < 1 => 30, > 200 => 200, _ => Tamano.Value };
    }
}
