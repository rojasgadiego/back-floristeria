namespace Colibri.Api.Dto.Requests;

/// <summary>
/// Lo que la pantalla manda para cobrar.
///
/// NO lleva montos: el servidor lee los precios de la base, recalcula la
/// promoción y arma el total de cero. El total del carrito es una
/// previsualización; si difiere del de la boleta, manda el de la boleta.
/// </summary>
public class RegistrarVentaRequest
{
    public List<VentaLineaRequest> Items { get; set; } = [];

    public MedioPago MedioPago { get; set; }

    public int? ClienteId { get; set; }
    public int? PromocionId { get; set; }
    public int? CotizacionId { get; set; }

    public int DescuentoManual { get; set; }
    public int PuntosCanjeados { get; set; }

    /// <summary>Solo en efectivo. Con esto se calcula el vuelto.</summary>
    public int? Recibido { get; set; }

    /// <summary>
    /// Correo y clave de quien autoriza un descuento sobre el umbral. Se
    /// verifican contra la base: no alcanza con que la pantalla diga que
    /// alguien autorizó.
    /// </summary>
    public AutorizacionRequest? Autorizacion { get; set; }
}

public class VentaLineaRequest
{
    /// <summary>
    /// El código de la partida escaneada. Con esto se descuenta de ese balde
    /// exacto y la anulación puede devolver las varas a su lote.
    /// </summary>
    public string? Partida { get; set; }

    /// <summary>
    /// Alternativa a Partida: se reparte por FIFO entre las partidas del
    /// mostrador. Para lo que no se escanea y para los armados.
    /// </summary>
    public int? ProductoId { get; set; }

    public int Cantidad { get; set; }
}

public class AutorizacionRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class AnularVentaRequest
{
    public string Motivo { get; set; } = string.Empty;
}

public class VentaFiltro
{
    public string? Buscar { get; set; }

    /// <summary>
    /// Lo pone el ENDPOINT desde el token, no el cliente. Un vendedor no ve
    /// lo que vendió otro.
    /// </summary>
    public int? UsuarioId { get; set; }

    public int? ClienteId { get; set; }
    public MedioPago? MedioPago { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
    public bool? IncluirAnuladas { get; set; }

    public int? Pagina { get; set; }
    public int? Tamano { get; set; }

    public int PaginaReal { get; private set; } = 1;
    public int TamanoReal { get; private set; } = 50;

    public void Normalizar()
    {
        PaginaReal = Pagina is null or < 1 ? 1 : Pagina.Value;
        TamanoReal = Tamano switch { null or < 1 => 50, > 200 => 200, _ => Tamano.Value };
        Buscar = string.IsNullOrWhiteSpace(Buscar) ? null : Buscar.Trim();
    }
}
