namespace Colibri.Api.Features.Reportes.Dtos;

/* ==================================================================
   PANEL DEL DÍA
   ================================================================== */

/// <summary>
/// Lo que se mira al llegar y antes de cerrar. Responde tres preguntas:
/// cómo va el día, qué hay que atender ahora, y qué viene.
/// </summary>
public class PanelDto
{
    public DateOnly Fecha { get; set; }

    public ResultadoDiaDto Hoy { get; set; } = new();

    /// <summary>El mismo día de la semana pasada, para tener referencia.</summary>
    public ResultadoDiaDto? SemanaPasada { get; set; }

    /// <summary>Variación de ingresos contra la semana pasada.</summary>
    public decimal? VariacionSemanal { get; set; }

    public EstadoCajaDto? Caja { get; set; }

    /// <summary>Lo que exige atención hoy, con su urgencia.</summary>
    public IReadOnlyList<AlertaDto> Alertas { get; set; } = Array.Empty<AlertaDto>();

    public IReadOnlyList<EventoProximoDto> ProximosEventos { get; set; }
        = Array.Empty<EventoProximoDto>();
}

public class ResultadoDiaDto
{
    public DateOnly Dia { get; set; }
    public long Boletas { get; set; }
    public long Bruto { get; set; }
    public long Descuentos { get; set; }

    /// <summary>Lo que efectivamente entró.</summary>
    public long Ingresos { get; set; }

    public long Neto { get; set; }
    public long Iva { get; set; }

    /// <summary>Costo real de la flor que salió, con los lotes que se usaron.</summary>
    public long CostoVendido { get; set; }

    public long Mermas { get; set; }
    public long UtilidadBruta { get; set; }

    /// <summary>Utilidad bruta menos mermas. Es el número del día.</summary>
    public long Resultado { get; set; }

    public decimal MargenPorcentaje { get; set; }
    public int TicketPromedio { get; set; }
}

public class EstadoCajaDto
{
    public int CajaId { get; set; }
    public bool Abierta { get; set; }
    public DateTimeOffset AbiertaEn { get; set; }
    public string? Responsable { get; set; }
    public int FondoInicial { get; set; }
    public long EfectivoEsperado { get; set; }
    public long TotalVendido { get; set; }
    public int Boletas { get; set; }
}

public class AlertaDto
{
    /// <summary>lote_vencido, lote_por_vencer, bajo_minimo, cobro_vencido, evento_sin_stock</summary>
    public string Tipo { get; set; } = string.Empty;

    /// <summary>alta, media o baja.</summary>
    public string Urgencia { get; set; } = string.Empty;

    public string Mensaje { get; set; } = string.Empty;
    public int Cantidad { get; set; }

    /// <summary>Dónde ir a resolverlo.</summary>
    public string? Ruta { get; set; }
}

public class EventoProximoDto
{
    public int CotizacionId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public string TipoEvento { get; set; } = string.Empty;
    public DateOnly FechaEvento { get; set; }
    public int DiasParaEvento { get; set; }
    public int Total { get; set; }
    public int Saldo { get; set; }

    /// <summary>Productos del evento que el stock actual no cubre.</summary>
    public int ProductosFaltantes { get; set; }
}

/* ==================================================================
   RESULTADO DEL PERÍODO
   ================================================================== */

public class ResultadoPeriodoDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }
    public int Dias { get; set; }

    public long Boletas { get; set; }
    public long Bruto { get; set; }
    public long Descuentos { get; set; }
    public long Ingresos { get; set; }
    public long Neto { get; set; }
    public long Iva { get; set; }
    public long CostoVendido { get; set; }
    public long Mermas { get; set; }
    public long UtilidadBruta { get; set; }
    public long Resultado { get; set; }

    public decimal MargenPorcentaje { get; set; }

    /// <summary>Pérdida sobre ingresos. Sobre 5% es señal de comprar de más.</summary>
    public decimal MermaPorcentaje { get; set; }

    public int TicketPromedio { get; set; }
    public decimal BoletasPorDia { get; set; }

    /// <summary>Serie diaria, para graficar.</summary>
    public IReadOnlyList<ResultadoDiaDto> Serie { get; set; } = Array.Empty<ResultadoDiaDto>();

    /// <summary>Qué día de la semana rinde más. Sirve para decidir turnos.</summary>
    public IReadOnlyList<PorDiaSemanaDto> PorDiaSemana { get; set; }
        = Array.Empty<PorDiaSemanaDto>();

    public IReadOnlyList<PorMedioPagoDto> PorMedioPago { get; set; }
        = Array.Empty<PorMedioPagoDto>();
}

public class PorDiaSemanaDto
{
    public short Dia { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public long Boletas { get; set; }
    public long Ingresos { get; set; }
    public int Promedio { get; set; }
}

public class PorMedioPagoDto
{
    public string MedioPago { get; set; } = string.Empty;
    public long Boletas { get; set; }
    public long Total { get; set; }
    public decimal Porcentaje { get; set; }
}

/* ==================================================================
   PRODUCTOS
   ================================================================== */

public class RendimientoProductosDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    /// <summary>Lo que más deja, no lo que más se vende: son cosas distintas.</summary>
    public IReadOnlyList<ProductoRendimientoDto> Top { get; set; }
        = Array.Empty<ProductoRendimientoDto>();

    /// <summary>
    /// Lo que ocupa cámara sin moverse. Es donde está la plata dormida.
    /// </summary>
    public IReadOnlyList<ProductoSinMovimientoDto> SinMovimiento { get; set; }
        = Array.Empty<ProductoSinMovimientoDto>();

    public IReadOnlyList<CategoriaRendimientoDto> PorCategoria { get; set; }
        = Array.Empty<CategoriaRendimientoDto>();
}

public class ProductoRendimientoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public string Categoria { get; set; } = string.Empty;

    public int Unidades { get; set; }
    public long Ingresos { get; set; }
    public long Costo { get; set; }
    public long Utilidad { get; set; }
    public decimal MargenPorcentaje { get; set; }

    /// <summary>Unidades perdidas en el período.</summary>
    public int Mermado { get; set; }

    /// <summary>Boletas en que apareció. Distingue lo constante de lo puntual.</summary>
    public int Apariciones { get; set; }
}

public class ProductoSinMovimientoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Stock { get; set; }

    /// <summary>Dinero inmovilizado en este producto.</summary>
    public int ValorInmovilizado { get; set; }

    public DateTimeOffset? UltimaVenta { get; set; }
    public int? DiasSinVender { get; set; }
}

public class CategoriaRendimientoDto
{
    public int CategoriaId { get; set; }
    public string Categoria { get; set; } = string.Empty;
    public long Ingresos { get; set; }
    public long Utilidad { get; set; }
    public decimal ParticipacionPorcentaje { get; set; }
}

/* ==================================================================
   INVENTARIO
   ================================================================== */

/// <summary>
/// Cuánta plata hay dormida en la cámara ahora mismo, y en qué estado.
/// </summary>
public class ValorInventarioDto
{
    public DateOnly Fecha { get; set; }

    public long ValorTotal { get; set; }

    /// <summary>Valor de lo que está en buen estado.</summary>
    public long ValorSano { get; set; }

    /// <summary>Valor en lotes por vencer: hay que moverlo esta semana.</summary>
    public long ValorPorVencer { get; set; }

    /// <summary>Valor ya vencido: pérdida casi segura.</summary>
    public long ValorVencido { get; set; }

    /// <summary>Valor en flor recuperada, que se vende más barata.</summary>
    public long ValorRecuperado { get; set; }

    public int LotesActivos { get; set; }
    public int VarasEnCamara { get; set; }

    public IReadOnlyList<ValorPorProductoDto> PorProducto { get; set; }
        = Array.Empty<ValorPorProductoDto>();

    /// <summary>Lotes que exigen decisión hoy.</summary>
    public IReadOnlyList<LoteCriticoDto> Criticos { get; set; }
        = Array.Empty<LoteCriticoDto>();
}

public class ValorPorProductoDto
{
    public int ProductoId { get; set; }
    public string Producto { get; set; } = string.Empty;
    public string Emoji { get; set; } = string.Empty;
    public int Varas { get; set; }
    public decimal CostoPromedio { get; set; }
    public long Valor { get; set; }
    public int Lotes { get; set; }
}

public class LoteCriticoDto
{
    public int LoteId { get; set; }
    public string Codigo { get; set; } = string.Empty;
    public string Producto { get; set; } = string.Empty;
    public int VarasDisponibles { get; set; }
    public int DiasEnCamara { get; set; }
    public int? DiasParaVencer { get; set; }
    public int ValorRestante { get; set; }
    public string Alerta { get; set; } = string.Empty;
}

/* ==================================================================
   EQUIPO
   ================================================================== */

/// <summary>
/// Cómo le va a cada persona en el mesón. El dato que más rinde no es cuánto
/// vendió, sino la diferencia acumulada de caja: una diferencia aislada es un
/// error de conteo, un patrón es otra cosa.
/// </summary>
public class RendimientoEquipoDto
{
    public DateOnly Desde { get; set; }
    public DateOnly Hasta { get; set; }

    public IReadOnlyList<VendedorDto> Vendedores { get; set; } = Array.Empty<VendedorDto>();
}

public class VendedorDto
{
    public int UsuarioId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string Rol { get; set; } = string.Empty;

    public int Turnos { get; set; }
    public long Boletas { get; set; }
    public long TotalVendido { get; set; }
    public int TicketPromedio { get; set; }

    /// <summary>Cuánto rebajó en descuentos manuales.</summary>
    public long DescuentosOtorgados { get; set; }

    public int BoletasAnuladas { get; set; }

    /// <summary>
    /// Diferencias de caja acumuladas. Negativo es faltante.
    /// Un turno descuadrado es normal; un patrón merece conversación.
    /// </summary>
    public int DiferenciaCajaAcumulada { get; set; }

    public int TurnosDescuadrados { get; set; }
}

/* ==================================================================
   CIERRE DE TURNO
   ================================================================== */

/// <summary>
/// Desglose del turno, separando la venta de mostrador de los abonos de
/// eventos. Son plata distinta: una ya entregó flor, la otra es un
/// compromiso pendiente.
/// </summary>
public class DesgloseTurnoDto
{
    public int CajaId { get; set; }
    public DateTimeOffset AbiertaEn { get; set; }
    public DateTimeOffset? CerradaEn { get; set; }
    public string? Responsable { get; set; }
    public string Estado { get; set; } = string.Empty;

    public int FondoInicial { get; set; }
    public long TotalVendido { get; set; }
    public int Boletas { get; set; }

    /// <summary>Venta de productos entregados.</summary>
    public long Mostrador { get; set; }

    /// <summary>Abonos de eventos: plata recibida por flor que aún no sale.</summary>
    public long AbonosEventos { get; set; }

    /// <summary>Traslados, montajes y otros servicios.</summary>
    public long Servicios { get; set; }

    public IReadOnlyList<PorMedioPagoDto> PorMedioPago { get; set; }
        = Array.Empty<PorMedioPagoDto>();

    /// <summary>Qué eventos recibieron abonos en este turno.</summary>
    public IReadOnlyList<AbonoTurnoDto> Abonos { get; set; } = Array.Empty<AbonoTurnoDto>();

    public long EfectivoEsperado { get; set; }
    public int? EfectivoContado { get; set; }
    public int? Diferencia { get; set; }
}

public class AbonoTurnoDto
{
    public int CotizacionId { get; set; }
    public string Folio { get; set; } = string.Empty;
    public string Cliente { get; set; } = string.Empty;
    public int Monto { get; set; }
    public string MedioPago { get; set; } = string.Empty;
    public string VentaFolio { get; set; } = string.Empty;
}