using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>Una fila del libro mayor.</summary>
public class Movimiento
{
    public int Id { get; set; }
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int? LoteId { get; set; }
    public TipoMovimiento Tipo { get; set; }

    /// <summary>Negativa en las salidas: el signo dice la dirección.</summary>
    public int Cantidad { get; set; }

    public int? StockResultante { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public int? UsuarioId { get; set; }
    public string? Usuario { get; set; }
    public string? ReferenciaTipo { get; set; }
    public int? ReferenciaId { get; set; }
    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}
