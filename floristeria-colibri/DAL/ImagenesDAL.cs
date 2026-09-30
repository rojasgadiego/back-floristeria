using Colibri.Api.DbAccess;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;
using Npgsql;

namespace Colibri.Api.DAL;

/// <summary>Fotos de productos. Solo el registro: los archivos los maneja ImagenesBLL.</summary>
public class ImagenesDAL
{
    private readonly IAccesoDatos _db;

    public ImagenesDAL(IAccesoDatos db) => _db = db;

    /// <summary>sp_inv_u_producto_imagen. Devuelve el archivo anterior, si había.</summary>
    public async Task<(string? Anterior, string Error)> Guardar(
        int productoId, string archivo, long version, CancellationToken ct = default)
    {
        try
        {
            var anterior = await _db.Escalar<string?>(
                "SELECT sp_inv_u_producto_imagen(@productoId, @archivo, @version)",
                new { productoId, archivo, version }, ct);
            return (anterior, string.Empty);
        }
        catch (PostgresException ex) when (ErroresPg.EsDeNegocio(ex))
        {
            return (null, ErroresPg.Mensaje(ex));
        }
    }

    /// <summary>sp_inv_d_producto_imagen. Devuelve el archivo a borrar, o null.</summary>
    public async Task<string?> Quitar(int productoId, CancellationToken ct = default)
        => await _db.Escalar<string?>(
            "SELECT sp_inv_d_producto_imagen(@productoId)", new { productoId }, ct);

    public async Task<IEnumerable<ProductoImagenVersion>> Versiones(CancellationToken ct = default)
        => await _db.ConsultarLista<ProductoImagenVersion>(
            "SELECT * FROM sp_inv_c_producto_imagenes()", null, ct);

    public async Task<ProductoImagenArchivo?> Archivo(int productoId, CancellationToken ct = default)
        => await _db.ConsultarUno<ProductoImagenArchivo>(
            "SELECT * FROM sp_inv_c_producto_imagen(@productoId)", new { productoId }, ct);

    public async Task<IEnumerable<ProductoCatalogo>> Catalogo(CancellationToken ct = default)
        => await _db.ConsultarLista<ProductoCatalogo>(
            "SELECT * FROM sp_pub_c_catalogo()", null, ct);
}
