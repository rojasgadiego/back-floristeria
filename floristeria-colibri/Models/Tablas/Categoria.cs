namespace Colibri.Api.Models.Tablas;

public class Categoria
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;

    /// <summary>Cuántos productos activos tiene. Para no borrar a ciegas.</summary>
    public long Productos { get; set; }
}
