using Colibri.Api.DAL;
using Colibri.Api.Models.Tablas;
using Colibri.Api.Utils;

namespace Colibri.Api.BLL;

/// <summary>
/// Fotos de productos. El navegador ya las achica antes de subir (~1200 px,
/// JPG liviano), así que acá no se procesan: se valida que sean de verdad
/// una imagen, se guardan en disco y se registran en la base.
///
/// La carpeta es Imagenes__Carpeta; por defecto datos/imagenes junto a la
/// app. En el VPS esa ruta es un volumen de Docker: sin él, las fotos se
/// perderían en cada despliegue.
/// </summary>
public class ImagenesBLL
{
    /// <summary>Una foto ya reducida pesa ~200 KB. 5 MB deja margen sin abrir la puerta a cualquier cosa.</summary>
    public const int PesoMaximo = 5 * 1024 * 1024;

    private readonly ImagenesDAL _dal;
    private readonly string _carpeta;
    private readonly ILogger<ImagenesBLL> _log;

    public ImagenesBLL(ImagenesDAL dal, IConfiguration config, IWebHostEnvironment env, ILogger<ImagenesBLL> log)
    {
        _dal = dal;
        _log = log;
        var raiz = config["Imagenes:Carpeta"];
        _carpeta = Path.Combine(
            string.IsNullOrWhiteSpace(raiz) ? Path.Combine(env.ContentRootPath, "datos", "imagenes") : raiz,
            "productos");
    }

    public async Task<ResultadoOp<ProductoImagenVersion>> Guardar(
        int productoId, Stream cuerpo, CancellationToken ct = default)
    {
        if (productoId <= 0) return ResultadoOp<ProductoImagenVersion>.Error("Indica el producto.");

        // Se lee con tope: un cuerpo sin Content-Length no puede llenar la memoria.
        using var ms = new MemoryStream();
        var buffer = new byte[81920];
        int leidos;
        while ((leidos = await cuerpo.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + leidos > PesoMaximo)
                return ResultadoOp<ProductoImagenVersion>.Error("La foto pesa demasiado. El máximo es 5 MB.");
            ms.Write(buffer, 0, leidos);
        }

        if (ms.Length == 0) return ResultadoOp<ProductoImagenVersion>.Error("No llegó ninguna foto.");

        // El tipo se decide por los primeros bytes, no por lo que diga el
        // request: así no se puede colar otra cosa con extensión .jpg.
        var extension = Extension(ms.GetBuffer().AsSpan(0, (int)Math.Min(ms.Length, 16)));
        if (extension is null)
            return ResultadoOp<ProductoImagenVersion>.Error("Formato no soportado. Usa una foto JPG, PNG o WebP.");

        var version = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var archivo = $"{productoId}-{version}{extension}";

        Directory.CreateDirectory(_carpeta);
        var destino = Path.Combine(_carpeta, archivo);
        var temporal = destino + ".tmp";

        ms.Position = 0;
        await using (var fs = File.Create(temporal))
            await ms.CopyToAsync(fs, ct);
        File.Move(temporal, destino, overwrite: true);

        var (anterior, error) = await _dal.Guardar(productoId, archivo, version, ct);
        if (!string.IsNullOrWhiteSpace(error))
        {
            Borrar(archivo);
            return ResultadoOp<ProductoImagenVersion>.Error(error);
        }

        if (!string.IsNullOrWhiteSpace(anterior) && anterior != archivo) Borrar(anterior);

        return ResultadoOp<ProductoImagenVersion>.Exito(
            new ProductoImagenVersion { ProductoId = productoId, Version = version });
    }

    public async Task<ResultadoOp<bool>> Quitar(int productoId, CancellationToken ct = default)
    {
        if (productoId <= 0) return ResultadoOp<bool>.Error("Indica el producto.");

        var archivo = await _dal.Quitar(productoId, ct);
        if (!string.IsNullOrWhiteSpace(archivo)) Borrar(archivo);

        return ResultadoOp<bool>.Exito(true);
    }

    public Task<IEnumerable<ProductoImagenVersion>> Versiones(CancellationToken ct = default)
        => _dal.Versiones(ct);

    public Task<IEnumerable<ProductoCatalogo>> Catalogo(CancellationToken ct = default)
        => _dal.Catalogo(ct);

    /// <summary>La ruta en disco y su tipo, o null si no hay foto o el archivo desapareció.</summary>
    public async Task<(string Ruta, string Tipo)?> Abrir(int productoId, CancellationToken ct = default)
    {
        if (productoId <= 0) return null;

        var registro = await _dal.Archivo(productoId, ct);
        if (registro is null) return null;

        var ruta = Path.Combine(_carpeta, Path.GetFileName(registro.Archivo));
        if (!File.Exists(ruta))
        {
            _log.LogWarning("La foto {Archivo} del producto {Id} no está en disco", registro.Archivo, productoId);
            return null;
        }

        var tipo = Path.GetExtension(ruta) switch
        {
            ".png" => "image/png",
            ".webp" => "image/webp",
            _ => "image/jpeg"
        };
        return (ruta, tipo);
    }

    private void Borrar(string archivo)
    {
        try
        {
            var ruta = Path.Combine(_carpeta, Path.GetFileName(archivo));
            if (File.Exists(ruta)) File.Delete(ruta);
        }
        catch (Exception ex)
        {
            // Un archivo huérfano no rompe nada; solo ocupa espacio.
            _log.LogWarning(ex, "No se pudo borrar la foto {Archivo}", archivo);
        }
    }

    private static string? Extension(ReadOnlySpan<byte> b)
    {
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return ".jpg";
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return ".png";
        if (b.Length >= 12 && b[0] == 'R' && b[1] == 'I' && b[2] == 'F' && b[3] == 'F'
            && b[8] == 'W' && b[9] == 'E' && b[10] == 'B' && b[11] == 'P') return ".webp";
        return null;
    }
}
