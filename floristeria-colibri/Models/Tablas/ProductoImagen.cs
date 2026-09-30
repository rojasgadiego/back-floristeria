namespace Colibri.Api.Models.Tablas;

/// <summary>Qué productos tienen foto y en qué versión (sp_inv_c_producto_imagenes).</summary>
public class ProductoImagenVersion
{
    public int ProductoId { get; set; }

    /// <summary>Cambia en cada reemplazo. Va en la URL para romper la caché.</summary>
    public long Version { get; set; }
}

/// <summary>El archivo a servir (sp_inv_c_producto_imagen).</summary>
public class ProductoImagenArchivo
{
    public string Archivo { get; set; } = string.Empty;
    public long Version { get; set; }
}

/// <summary>Una tarjeta del catálogo público de la landing (sp_pub_c_catalogo).</summary>
public class ProductoCatalogo
{
    public int Id { get; set; }
    public string Nombre { get; set; } = string.Empty;
    public string? Emoji { get; set; }
    public string? Categoria { get; set; }
    public int Precio { get; set; }
    public long Version { get; set; }
}
