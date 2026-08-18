// Domain/Entities/ProyeccionesInventario.cs
// Entidades sin clave: no son tablas, son la forma del resultado de una
// función o de una vista. EF las necesita declaradas para poder materializar
// lo que devuelve FromSqlInterpolated.

namespace Colibri.Api.Domain.Entities;

/// <summary>Una partida en el mostrador. Mapea la vista vw_vendibles.</summary>
public class Vendible
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int CategoriaId { get; set; }
    public int Disponible { get; set; }

    /// <summary>optima · buena · limitada. Null en flor de primera.</summary>
    public string? Calidad { get; set; }

    /// <summary>
    /// Ya resuelto por fn_precio_lote: precio propio del lote, si no la
    /// calidad, si no el precio de lista. Viene calculado de la base para que
    /// el mesón, esta vista y el ticket no puedan responder distinto.
    /// </summary>
    public int Precio { get; set; }

    public decimal CostoPorVara { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public bool RequiereEscaneo { get; set; }
    public bool Vencido { get; set; }
}

/// <summary>Lo que devuelve fn_traspasar: una fila por lote tocado.</summary>
public class ResultadoTraspaso
{
    public int LoteVentaId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public int Cantidad { get; set; }
}

/// <summary>Lo que devuelve fn_conteo.</summary>
public class ResultadoConteo
{
    public int SegunSistema { get; set; }
    public int Contado { get; set; }
    public int Diferencia { get; set; }
}