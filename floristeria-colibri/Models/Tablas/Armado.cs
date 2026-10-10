namespace Colibri.Api.Models.Tablas;

/// <summary>Un componente de la receta y cuánto hay en bodega (sp_inv_c_disponibilidad_armado).</summary>
public class ComponenteArmado
{
    public int ComponenteId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int PorUnidad { get; set; }
    public bool ControlaLotes { get; set; }
    public int DePrimera { get; set; }
    public int Recuperada { get; set; }
}

/// <summary>Un balde de flor recuperada que se puede autorizar para armar.</summary>
public class LoteRecuperadoArmado
{
    public int LoteId { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Codigo { get; set; } = string.Empty;
    public int VarasDisponibles { get; set; }
    public int DiasEnCamara { get; set; }
    public decimal CostoPorVara { get; set; }
    public string? Calidad { get; set; }
    public string Alerta { get; set; } = "normal";
}

/// <summary>Lo que muestra el modal de armado antes de confirmar.</summary>
public class DisponibilidadArmado
{
    public int Cantidad { get; set; }
    public int PosiblesConPrimera { get; set; }
    public int PosiblesConRecuperada { get; set; }
    public bool AlcanzaConPrimera { get; set; }
    public List<FaltanteArmado> Faltantes { get; set; } = [];
    public List<LoteRecuperadoArmado> LotesSugeridos { get; set; } = [];
}

public class FaltanteArmado
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int Necesita { get; set; }
    public int HayDePrimera { get; set; }
    public int HayRecuperada { get; set; }
    public int Faltan { get; set; }
}

/// <summary>Lo que devuelve armar (sp_inv_i_armar).</summary>
public class ResultadoArmado
{
    public int CostoUnitario { get; set; }
    public int CostoTotal { get; set; }
    public int Listos { get; set; }
}
