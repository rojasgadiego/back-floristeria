using Colibri.Api.Models.Enums;

namespace Colibri.Api.Dto.Requests;

// ═══════════════ Filtros ═══════════════
//
// Los tipos de valor van NULABLES: con [AsParameters], un int no nulable
// se vuelve OBLIGATORIO en el query string aunque tenga inicializador, y
// el request falla con 400 si el front no lo manda.

public class ProveedorFiltro
{
    public string? Buscar { get; set; }
    public bool? Activo { get; set; }
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

public class CompraFiltro
{
    public string? Buscar { get; set; }
    public int? ProveedorId { get; set; }
    public EstadoCompra? Estado { get; set; }
    public DateOnly? Desde { get; set; }
    public DateOnly? Hasta { get; set; }
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

// ═══════════════ Proveedores ═══════════════

public class ProveedorRequest
{
    public string Nombre { get; set; } = string.Empty;
    public string? Rut { get; set; }
    public string? Contacto { get; set; }
    public string? Telefono { get; set; }
    public string? Correo { get; set; }
    public string? Direccion { get; set; }
    public string? Notas { get; set; }
}

// ═══════════════ Presentaciones ═══════════════

public class PresentacionRequest
{
    public int ProductoId { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public TipoPresentacion Tipo { get; set; }

    /// <summary>Cuántos baldes trae. Define cuántos QR salen por unidad comprada.</summary>
    public int Paquetes { get; set; } = 1;

    public int VarasPorPaquete { get; set; }
    public bool Predeterminada { get; set; }
}

// ═══════════════ Compras ═══════════════

public class CrearCompraRequest
{
    public int ProveedorId { get; set; }
    public DateOnly? Fecha { get; set; }
    public string? Documento { get; set; }
    public string? Notas { get; set; }
}

public class ActualizarCompraRequest
{
    public int ProveedorId { get; set; }
    public DateOnly? Fecha { get; set; }
    public string? Documento { get; set; }
    public string? Notas { get; set; }
}

public class CompraItemRequest
{
    public int ProductoId { get; set; }
    public int PresentacionId { get; set; }

    /// <summary>Unidades de la presentación: 3 cajas, 10 paquetes.</summary>
    public int Cantidad { get; set; }

    /// <summary>Lo que cuesta UNA unidad de la presentación, no una vara.</summary>
    public int CostoUnitario { get; set; }
}

public class RecibirCompraRequest
{
    /// <summary>
    /// Cuándo entró la mercadería. Null = hoy. Se puede fechar ayer si la
    /// recepción se registra al día siguiente, que pasa seguido; de ahí sale
    /// el vencimiento de los lotes.
    /// </summary>
    public DateOnly? FechaIngreso { get; set; }
}

public class AnularCompraRequest
{
    public string? Motivo { get; set; }
}


/// <summary>
/// La compra entera en un solo POST: cabecera y líneas.
///
/// Un borrador sin líneas no sirve para nada, y guardarlo a medias deja
/// basura si alguien cierra la pestaña. Además la operación queda atómica:
/// o entra toda la compra o no entra ninguna.
/// </summary>
public class GuardarCompraRequest
{
    public int ProveedorId { get; set; }
    public DateOnly? Fecha { get; set; }
    public string? Documento { get; set; }
    public string? Notas { get; set; }

    /// <summary>
    /// Porcentaje, no fracción: 19 y no 0.19.
    ///
    /// ⚠️ NO se guarda en la tabla —solo el iva ya calculado—, así que al
    /// reabrir el borrador el formulario vuelve a mostrar 19% aunque se
    /// haya guardado con otra tasa. Si aparecen compras exentas, hay que
    /// agregar la columna.
    /// </summary>
    public decimal IvaTasa { get; set; } = 19;

    public List<CompraLineaRequest> Items { get; set; } = [];
}

public class CompraLineaRequest
{
    public int ProductoId { get; set; }
    public int PresentacionId { get; set; }

    /// <summary>Unidades de la presentación: 3 cajas, 10 paquetes.</summary>
    public int Cantidad { get; set; }

    /// <summary>Lo que cuesta UNA unidad de la presentación, no una vara.</summary>
    public int CostoUnitario { get; set; }
}