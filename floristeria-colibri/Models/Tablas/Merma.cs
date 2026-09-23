using System.Text.Json.Serialization;
using Colibri.Api.Models.Enums;

namespace Colibri.Api.Models.Tablas;

/// <summary>
/// Una salida de inventario que no es venta.
///
/// `Cantidad` es lo que salió; `CantidadRecuperada` lo que volvió al stock
/// rebajado. La diferencia es lo que se perdió de verdad, y por eso
/// `CostoPerdido` no es lo mismo que `CostoTotal`: un ramo desarmado con
/// todas sus varas recuperadas cuesta la rebaja, no el ramo entero.
/// </summary>
public class Merma
{
    public int Id { get; set; }

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }

    // ─── De dónde salió ───

    /// <summary>El balde de bodega. Null si salió del mostrador o del stock.</summary>
    public int? LoteId { get; set; }

    /// <summary>La partida del mostrador. La flor que se marchita en la vitrina.</summary>
    public int? PartidaId { get; set; }

    /// <summary>El código del lote o de la partida, el que corresponda.</summary>
    public string? OrigenCodigo { get; set; }

    /// <summary>'bodega', 'mostrador' o 'stock'.</summary>
    public string? Origen { get; set; }

    public int Cantidad { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }

    /// <summary>
    /// Congelado al registrar: si el proveedor sube el precio la semana que
    /// viene, la pérdida de hoy sigue valiendo lo que valía hoy.
    /// </summary>
    public int CostoUnitario { get; set; }

    public int CostoTotal { get; set; }

    public DestinoMerma Destino { get; set; }

    public int CantidadRecuperada { get; set; }
    public int CantidadPerdida { get; set; }

    public CalidadReingreso? CalidadReingreso { get; set; }

    /// <summary>El lote nuevo que nació con la flor recuperada.</summary>
    public int? LoteRecuperacionId { get; set; }
    public string? LoteRecuperacion { get; set; }

    public int? CostoRecuperadoUnitario { get; set; }

    /// <summary>
    /// Lo que efectivamente se perdió. Cero en una devolución al proveedor:
    /// sale del stock pero se abona, y contarla como merma inflaría el
    /// porcentaje del mes.
    /// </summary>
    public int CostoPerdido { get; set; }

    // ─── Control ───

    /// <summary>
    /// true si el código se leyó con la cámara, false si se tipeó a mano.
    ///
    /// Es el dato que alimenta el patrón: quien registra con el balde en la
    /// mano escanea, quien inventa la merma tipea.
    /// </summary>
    public bool Escaneado { get; set; }

    /// <summary>
    /// El NOMBRE de quien autorizó una merma sobre el umbral. Queda congelado
    /// aunque esa cuenta después se desactive.
    /// </summary>
    public string? AutorizadoPor { get; set; }

    public string? Usuario { get; set; }

    public bool Revertida { get; set; }
    public string? RevertidaPor { get; set; }
    public DateTime? RevertidaEn { get; set; }

    /// <summary>Por qué se revirtió. Antes se pegaba al final del detalle.</summary>
    public string? MotivoReversion { get; set; }

    /// <summary>
    /// Las mermas de un mismo desarme comparten grupo: una por componente.
    /// Revertir cualquiera revierte el desarme entero.
    /// </summary>
    public int? DesarmeGrupo { get; set; }

    public DateTime CreadoEn { get; set; }

    [JsonIgnore]
    public long TotalFilas { get; set; }
}

/// <summary>
/// Los números del período, con lo perdido separado de lo recuperado.
///
/// Sin esa distinción, un arreglo desarmado con todas sus varas útiles
/// aparecería como pérdida total y el porcentaje dejaría de servir para
/// decidir cuánto comprar.
/// </summary>
public class ResumenMermas
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long UnidadesPerdidas { get; set; }
    public long UnidadesRecuperadas { get; set; }

    public long CostoTotal { get; set; }

    /// <summary>Lo que de verdad se perdió, sumando botado y desvalorizado.</summary>
    public long CostoPerdido { get; set; }

    /// <summary>Lo que sigue valiendo algo porque volvió al stock.</summary>
    public long CostoRecuperado { get; set; }

    /// <summary>Se botó: no volvió nada.</summary>
    public long CostoBotado { get; set; }

    /// <summary>Volvió pero vale menos: solo se perdió la rebaja.</summary>
    public long CostoDesvalorizado { get; set; }

    /// <summary>El proveedor lo abona. NO es costo.</summary>
    public long CostoDevuelto { get; set; }

    public long? VentasPeriodo { get; set; }

    /// <summary>Sobre 5% en una florería es señal de que se compra de más.</summary>
    public decimal? PorcentajeSobreVentas { get; set; }

    public IReadOnlyList<MermaPorDestino> PorDestino { get; set; } = Array.Empty<MermaPorDestino>();
    public IReadOnlyList<MermaPorProducto> PorProducto { get; set; } = Array.Empty<MermaPorProducto>();
    public IReadOnlyList<MermaPorMotivo> PorMotivo { get; set; } = Array.Empty<MermaPorMotivo>();
    public IReadOnlyList<MermaPorCategoria> PorCategoria { get; set; } = Array.Empty<MermaPorCategoria>();
}

public class MermaPorDestino
{
    public DestinoMerma Destino { get; set; }
    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long CostoPerdido { get; set; }
}

public class MermaPorProducto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long UnidadesRecuperadas { get; set; }
    public long CostoPerdido { get; set; }
}

/// <summary>
/// El motivo que más plata cuesta es la pregunta que sigue: si es
/// "marchita", se compra de más; si es "quebrada", el problema es el manejo.
/// </summary>
public class MermaPorMotivo
{
    public string Motivo { get; set; } = string.Empty;
    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long CostoPerdido { get; set; }
}

/// <summary>
/// Un motivo del catálogo. La administradora los crea, renombra y apaga;
/// apagarlo lo saca de la lista sin tocar las mermas que ya lo usan.
/// </summary>
public class MotivoMerma
{
    public int Id { get; set; }
    public string Motivo { get; set; } = string.Empty;

    /// <summary>
    /// natural, accidente, operacional, proveedor, comercial, faltante u otro.
    /// Es lo que separa "se marchitó por mal cuidado" de "no se alcanzó a
    /// vender": problemas distintos con soluciones distintas.
    /// </summary>
    public string Categoria { get; set; } = "otro";

    /// <summary>"Otro", "Robo" o "Siniestro" no se entienden sin contexto.</summary>
    public bool RequiereDetalle { get; set; }

    /// <summary>El destino que el formulario propone al elegirlo.</summary>
    public DestinoMerma? DestinoSugerido { get; set; }

    public bool Activo { get; set; } = true;
    public int Orden { get; set; }
    public long Usos { get; set; }
}

public class MermaPorCategoria
{
    public string Categoria { get; set; } = string.Empty;
    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long CostoPerdido { get; set; }
}

/// <summary>Una línea del plan de desarme, pre-llenada desde la receta.</summary>
public class LineaDesarme
{
    public int ComponenteId { get; set; }
    public string Componente { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public bool ControlaLotes { get; set; }

    /// <summary>Cuántas varas lleva una unidad, según la receta.</summary>
    public int PorUnidad { get; set; }

    /// <summary>PorUnidad × cuántas unidades se desarman.</summary>
    public int Total { get; set; }

    public decimal CostoUnitario { get; set; }
    public int Precio { get; set; }
    public int? DiasVida { get; set; }
}

public class ResultadoDesarme
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public int Desarmados { get; set; }
    public int Mermas { get; set; }
    public int Recuperadas { get; set; }
    public int Perdidas { get; set; }
}

// ═══════════════════════════════════════════════════════════════
// CONTROL
// ═══════════════════════════════════════════════════════════════

/// <summary>
/// Lo que devuelve el escaneo. Sale de sp_mer_c_escanear, que acepta un
/// código de lote o de partida indistintamente: el prefijo LOT-/PAR- dice
/// cuál es, así que quien registra apunta la cámara sin tener que saber de
/// dónde viene lo que tiene en la mano.
///
/// El costo viene de acá y no se escribe a mano: es parte del control, y sin
/// eso cualquiera podría declarar que la flor cara valía poco.
/// </summary>
public class OrigenMerma
{
    /// <summary>'lote' (bodega) o 'partida' (mostrador).</summary>
    public string Origen { get; set; } = string.Empty;

    public int? LoteId { get; set; }
    public int? PartidaId { get; set; }
    public string Codigo { get; set; } = string.Empty;

    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public bool ControlaLotes { get; set; }

    public int Disponible { get; set; }
    public decimal CostoUnitario { get; set; }
    public int Precio { get; set; }

    public DateOnly? FechaIngreso { get; set; }
    public DateOnly? FechaVencimiento { get; set; }
    public int? DiasParaVencer { get; set; }

    /// <summary>Lo que vale lo que queda ahí.</summary>
    public decimal ValorTotal { get; set; }

    public string? Ubicacion { get; set; }
    public string? Proveedor { get; set; }

    public bool PuedeMermar { get; set; }
    public string? MotivoBloqueo { get; set; }

    /// <summary>Lo que conviene hacer, según lo que se escaneó.</summary>
    public string? Sugerencia { get; set; }
}

/// <summary>
/// Quién merma cuánto.
///
/// La columna que importa no es el total: es PorcentajeSinEscanear. Quien
/// registra con el balde en la mano escanea; quien inventa, tipea.
///
/// Esto NO acusa a nadie: una florería con una sola persona en bodega va a
/// mostrar 100% para ella y eso no significa nada. Pone los números a la
/// vista para que alguien pueda mirarlos.
/// </summary>
public class PatronUsuario
{
    public int? UsuarioId { get; set; }
    public string? Usuario { get; set; }
    public RolUsuario? Rol { get; set; }

    public long Registros { get; set; }
    public long Unidades { get; set; }
    public long CostoPerdido { get; set; }

    public long SinEscanear { get; set; }
    public decimal PorcentajeSinEscanear { get; set; }

    public decimal MontoPromedio { get; set; }

    /// <summary>Lo que más merma esa persona.</summary>
    public string? ProductoTop { get; set; }

    public string? MotivoTop { get; set; }
}

/// <summary>
/// A qué hora se merma.
///
/// Las mermas legítimas se concentran cuando se revisa la cámara: temprano o
/// al cerrar. Un bloque a media tarde, cuando hay una sola persona en el
/// local, es una pregunta que vale la pena hacer.
/// </summary>
public class PatronHorario
{
    public int Hora { get; set; }
    public long Registros { get; set; }
    public long CostoPerdido { get; set; }
    public long SinEscanear { get; set; }
}

/// <summary>
/// Las que se registraron a mano. No todas son sospechosas —una etiqueta
/// rota obliga a tipear— pero la lista permite revisar de a una en vez de
/// sospechar en general.
/// </summary>
public class MermaSinEscanear
{
    public int Id { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public int Cantidad { get; set; }
    public int CostoPerdido { get; set; }
    public string Motivo { get; set; } = string.Empty;
    public string? Detalle { get; set; }
    public string? LoteCodigo { get; set; }
    public string? Usuario { get; set; }
    public DateTime CreadoEn { get; set; }
}

/// <summary>Los tres patrones juntos, más el umbral vigente.</summary>
public class PatronesMerma
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    /// <summary>Desde cuánto hace falta autorización.</summary>
    public int UmbralAutorizacion { get; set; }

    public IReadOnlyList<PatronUsuario> PorUsuario { get; set; } = Array.Empty<PatronUsuario>();
    public IReadOnlyList<PatronHorario> PorHora { get; set; } = Array.Empty<PatronHorario>();
    public IReadOnlyList<MermaSinEscanear> SinEscanear { get; set; } = Array.Empty<MermaSinEscanear>();
}
