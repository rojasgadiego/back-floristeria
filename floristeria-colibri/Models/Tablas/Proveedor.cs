using System.Text.Json.Serialization;

namespace Colibri.Api.Models.Tablas;

public class Proveedor
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Rut { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
    public bool Activo { get; set; }

    // ─── Derivados. Solo cuentan compras recibidas: un borrador todavía
    //     no es plata gastada. ───

    public long Compras { get; set; }
    public DateOnly? UltimaCompra { get; set; }
    public long TotalComprado { get; set; }

    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}
