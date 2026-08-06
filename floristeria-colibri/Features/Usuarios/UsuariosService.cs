using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Usuarios.Dtos;
using Colibri.Api.Context;

namespace Colibri.Api.Features.Usuarios;

public class UsuariosService : IUsuariosService
{
    private readonly ColibriDbContext _db;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<UsuariosService> _log;

    public UsuariosService(
        ColibriDbContext db, IUsuarioActual usuarioActual, ILogger<UsuariosService> log)
    {
        _db = db;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    public async Task<ResultadoPagina<UsuarioDto>> ListarAsync(
        UsuarioFiltro filtro, CancellationToken ct = default)
    {
        var consulta = _db.Usuarios.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();
            consulta = consulta.Where(u =>
                u.Nombre.ToLower().Contains(q) || u.Email.ToLower().Contains(q));
        }

        if (filtro.Activo.HasValue)
            consulta = consulta.Where(u => u.Activo == filtro.Activo.Value);

        if (!string.IsNullOrWhiteSpace(filtro.Rol))
            consulta = consulta.Where(u => u.Rol == ARol(filtro.Rol));

        var total = await consulta.CountAsync(ct);

        // Las ventas se agregan en la misma consulta: traer los usuarios y
        // después contar boletas uno por uno sería N+1.
        var items = await consulta
            .OrderBy(u => u.Nombre)
            .Skip(filtro.Saltar)
            .Take(filtro.PorPagina)
            .Select(u => new UsuarioDto
            {
                Id = u.Id,
                Nombre = u.Nombre,
                Email = u.Email,
                Rol = u.Rol.ToString(),
                Activo = u.Activo,
                UltimoAcceso = u.UltimoAcceso,
                CreadoEn = u.CreadoEn,
                Boletas = u.Ventas.Count(v => !v.Anulada),
                Vendido = u.Ventas.Where(v => !v.Anulada).Sum(v => (long)v.Total)
            })
            .ToListAsync(ct);

        return ResultadoPagina<UsuarioDto>.Crear(items, total, filtro);
    }

    public async Task<UsuarioDto> ObtenerAsync(int id, CancellationToken ct = default)
        => await _db.Usuarios.AsNoTracking()
               .Where(u => u.Id == id)
               .Select(u => Proyeccion(u))
               .FirstOrDefaultAsync(ct)
           ?? throw new NoEncontradoException("La cuenta");

    public async Task<UsuarioDto> CrearAsync(
        CrearUsuarioRequest peticion, CancellationToken ct = default)
    {
        var email = peticion.Email.Trim().ToLowerInvariant();

        // Se comprueba acá para dar un mensaje claro. El índice único de la
        // base es el que realmente garantiza que no se dupliquen: entre esta
        // consulta y el insert alguien más podría crear el mismo correo.
        if (await _db.Usuarios.AnyAsync(u => u.Email.ToLower() == email, ct))
            throw new ExcepcionNegocio("Ya existe una cuenta con ese correo.",
                StatusCodes.Status409Conflict);

        var usuario = new Usuario
        {
            Nombre = peticion.Nombre.Trim(),
            Email = email,
            Rol = ARol(peticion.Rol),
            // workFactor 12: unos 250 ms por verificación. Suficiente para
            // encarecer un ataque por diccionario sin que el login se note lento.
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(peticion.Password, workFactor: 12),
            Activo = true
        };

        _db.Usuarios.Add(usuario);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Cuenta creada: {Email} ({Rol}) por {Autor}",
            usuario.Email, usuario.Rol, _usuarioActual.Email);

        return await ObtenerAsync(usuario.Id, ct);
    }

    public async Task<UsuarioDto> ActualizarAsync(
        int id, ActualizarUsuarioRequest peticion, CancellationToken ct = default)
    {
        var usuario = await BuscarAsync(id, ct);
        var email = peticion.Email.Trim().ToLowerInvariant();

        if (await _db.Usuarios.AnyAsync(u => u.Email.ToLower() == email && u.Id != id, ct))
            throw new ExcepcionNegocio("Ya existe otra cuenta con ese correo.",
                StatusCodes.Status409Conflict);

        usuario.Nombre = peticion.Nombre.Trim();
        usuario.Email = email;
        await _db.SaveChangesAsync(ct);

        return await ObtenerAsync(id, ct);
    }

    public async Task<UsuarioDto> CambiarRolAsync(
        int id, CambiarRolRequest peticion, CancellationToken ct = default)
    {
        var usuario = await BuscarAsync(id, ct);

        // Cambiarse el rol a uno mismo permitiría a un admin degradarse por
        // error y quedar sin acceso a la gestión.
        if (id == _usuarioActual.Id)
            throw new ExcepcionNegocio("No puedes cambiar tu propio rol.");

        usuario.Rol = ARol(peticion.Rol);

        // Si esta era la última administradora activa, el trigger
        // usuarios_proteger_admin lanza P0001 y ManejadorExcepciones lo
        // convierte en 400 con el texto del trigger.
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Rol de {Email} cambiado a {Rol} por {Autor}",
            usuario.Email, usuario.Rol, _usuarioActual.Email);

        return await ObtenerAsync(id, ct);
    }

    public async Task<UsuarioDto> CambiarEstadoAsync(
        int id, bool activo, CancellationToken ct = default)
    {
        var usuario = await BuscarAsync(id, ct);

        if (id == _usuarioActual.Id)
            throw new ExcepcionNegocio("No puedes bloquear tu propia cuenta.");

        usuario.Activo = activo;
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Cuenta {Email} {Estado} por {Autor}",
            usuario.Email, activo ? "reactivada" : "bloqueada", _usuarioActual.Email);

        return await ObtenerAsync(id, ct);
    }

    public async Task RestablecerPasswordAsync(
        int id, RestablecerPasswordRequest peticion, CancellationToken ct = default)
    {
        var usuario = await BuscarAsync(id, ct);

        // A diferencia del cambio propio, acá no se pide la contraseña
        // anterior: el sentido de este endpoint es justamente que la persona
        // la olvidó. Por eso es exclusivo de administración.
        usuario.PasswordHash = BCrypt.Net.BCrypt.HashPassword(peticion.Password, workFactor: 12);
        await _db.SaveChangesAsync(ct);

        _log.LogWarning("Contraseña de {Email} restablecida por {Autor}",
            usuario.Email, _usuarioActual.Email);
    }

    /* ------------------------------------------------------------------ */

    private async Task<Usuario> BuscarAsync(int id, CancellationToken ct)
        => await _db.Usuarios.FirstOrDefaultAsync(u => u.Id == id, ct)
           ?? throw new NoEncontradoException("La cuenta");

    private static RolUsuario ARol(string valor)
        => Enum.TryParse<RolUsuario>(valor?.Trim().ToLowerInvariant(), out var rol)
            ? rol
            : throw new ExcepcionNegocio(
                "Rol no válido. Debe ser admin, vendedor o bodega.");

    private static UsuarioDto Proyeccion(Usuario u) => new()
    {
        Id = u.Id,
        Nombre = u.Nombre,
        Email = u.Email,
        Rol = u.Rol.ToString(),
        Activo = u.Activo,
        UltimoAcceso = u.UltimoAcceso,
        CreadoEn = u.CreadoEn,
        Boletas = u.Ventas.Count(v => !v.Anulada),
        Vendido = u.Ventas.Where(v => !v.Anulada).Sum(v => (long)v.Total)
    };
}
