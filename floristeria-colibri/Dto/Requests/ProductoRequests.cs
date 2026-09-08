using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Lo que entra por el body. PascalCase; si tu front manda snake_case, cambia
/// el naming policy en Program.cs en vez de renombrar propiedades una por una.
/// </summary>
public class CrearProductoRequest
{
    public string Codigo { get; set; } = string.Empty;
    public string Nombre { get; set; } = string.Empty;
    public int? CategoriaId { get; set; }
    public TipoProducto Tipo { get; set; }
    public decimal Precio { get; set; }
    public string? Emoji { get; set; }
    public int Minimo { get; set; }
    public decimal Costo { get; set; }

    /// <summary>Se ignora si ControlaLotes es true: ese stock entra por compra.</summary>
    public int Stock { get; set; }

    public bool ControlaLotes { get; set; }
    public int? DiasVida { get; set; }
}

/// <summary>
/// Sin Tipo ni Stock: el tipo es inmutable y el stock se mueve con compras,
/// ventas, mermas o ajustes, nunca editando la ficha.
/// </summary>
public class ActualizarProductoRequest
{
    public string Nombre { get; set; } = string.Empty;
    public int? CategoriaId { get; set; }
    public decimal Precio { get; set; }
    public string? Emoji { get; set; }
    public int? Minimo { get; set; }
    public decimal? Costo { get; set; }
    public int? DiasVida { get; set; }
}

public class CrearCategoriaRequest
{
    public string Nombre { get; set; } = string.Empty;
}
