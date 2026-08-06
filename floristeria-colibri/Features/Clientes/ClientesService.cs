using Microsoft.EntityFrameworkCore;

using Colibri.Api.Common;
using Colibri.Api.Common.Ajustes;
using Colibri.Api.Common.Paginacion;
using Colibri.Api.Common.Seguridad;
using Colibri.Api.Common.Validacion;
using Colibri.Api.Context;
using Colibri.Api.Domain.Entities;
using Colibri.Api.Features.Clientes.Dtos;

namespace Colibri.Api.Features.Clientes;

public class ClientesService : IClientesService
{
    private static readonly string[] Meses =
    {
        "enero", "febrero", "marzo", "abril", "mayo", "junio",
        "julio", "agosto", "septiembre", "octubre", "noviembre", "diciembre"
    };

    private readonly ColibriDbContext _db;
    private readonly IAjustesService _ajustes;
    private readonly IUsuarioActual _usuarioActual;
    private readonly ILogger<ClientesService> _log;

    public ClientesService(
        ColibriDbContext db,
        IAjustesService ajustes,
        IUsuarioActual usuarioActual,
        ILogger<ClientesService> log)
    {
        _db = db;
        _ajustes = ajustes;
        _usuarioActual = usuarioActual;
        _log = log;
    }

    /* ==================================================================
       CONSULTA
       ================================================================== */

    public async Task<ResultadoPagina<ClienteDto>> ListarAsync(
        ClienteFiltro filtro, CancellationToken ct = default)
    {
        var club = await _ajustes.ClubAsync(ct);
        var consulta = Proyectar();

        if (!string.IsNullOrWhiteSpace(filtro.Buscar))
        {
            var q = filtro.Buscar.Trim().ToLower();

            // El RUT se busca normalizado: quien atiende lo tipea con puntos,
            // sin puntos o con guion, y las tres formas deben encontrar.
            var rut = Rut.Normalizar(filtro.Buscar);

            consulta = consulta.Where(c =>
                c.Nombre.ToLower().Contains(q) ||
                c.RutPlano.Contains(rut) ||
                (c.Telefono != null && c.Telefono.Contains(q)) ||
                (c.Correo != null && c.Correo.ToLower().Contains(q)));
        }

        if (filtro.Activo.HasValue)
            consulta = consulta.Where(c => c.Activo == filtro.Activo.Value);

        if (filtro.ConPuntos)
            consulta = consulta.Where(c => c.Puntos > 0);

        if (filtro.CumpleMes.HasValue)
            consulta = consulta.Where(c => c.CumpleMes == filtro.CumpleMes.Value);

        if (filtro.SinComprarDias.HasValue)
        {
            var corte = DateTimeOffset.UtcNow.AddDays(-filtro.SinComprarDias.Value);
            consulta = consulta.Where(c => c.UltimaCompra == null || c.UltimaCompra < corte);
        }

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .OrderBy(c => c.Nombre)
            .Skip(filtro.Saltar).Take(filtro.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<ClienteDto>.Crear(
            Presentar(items, club.ValorPunto), total, filtro);
    }

    public async Task<ClienteDetalleDto> ObtenerAsync(int id, CancellationToken ct = default)
    {
        var club = await _ajustes.ClubAsync(ct);

        var b = await Proyectar().FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("El cliente");

        var compras = await ConsultarCompras(id)
            .OrderByDescending(v => v.Fecha).Take(10).ToListAsync(ct);

        var movimientos = await ConsultarPuntos(id)
            .OrderByDescending(m => m.Fecha).Take(20).ToListAsync(ct);

        // Lo que más compra: sirve para sugerir en el mesón sin preguntar
        var frecuentes = await _db.VentaItems.AsNoTracking()
            .Where(i => i.Venta.ClienteId == id && !i.Venta.Anulada && i.ProductoId != null)
            .GroupBy(i => new { i.ProductoId, i.Producto!.Nombre, i.Producto.Emoji })
            .Select(g => new ProductoFrecuenteDto
            {
                ProductoId = g.Key.ProductoId!.Value,
                Producto = g.Key.Nombre,
                Emoji = g.Key.Emoji,
                Veces = g.Count(),
                Unidades = g.Sum(x => x.Cantidad)
            })
            .OrderByDescending(p => p.Veces).ThenByDescending(p => p.Unidades)
            .Take(5)
            .ToListAsync(ct);

        var dto = Presentar(new[] { b }, club.ValorPunto).First();

        return new ClienteDetalleDto
        {
            Id = dto.Id,
            Rut = dto.Rut,
            Nombre = dto.Nombre,
            Telefono = dto.Telefono,
            Correo = dto.Correo,
            Direccion = dto.Direccion,
            CumpleMes = dto.CumpleMes,
            CumpleDia = dto.CumpleDia,
            Cumpleanos = dto.Cumpleanos,
            Notas = dto.Notas,
            Puntos = dto.Puntos,
            ValorPuntos = dto.ValorPuntos,
            Activo = dto.Activo,
            CreadoEn = dto.CreadoEn,
            Compras = dto.Compras,
            TotalComprado = dto.TotalComprado,
            UltimaCompra = dto.UltimaCompra,
            DiasSinComprar = dto.DiasSinComprar,

            TicketPromedio = dto.Compras > 0
                ? (int)(dto.TotalComprado / dto.Compras)
                : 0,
            UltimasCompras = compras,
            MovimientosPuntos = movimientos,
            ProductosFrecuentes = frecuentes
        };
    }

    /// <summary>
    /// Búsqueda por RUT para el punto de venta. Devuelve null si no existe,
    /// en vez de lanzar: en el mesón lo normal es que el cliente no esté
    /// registrado, y eso no es un error.
    /// </summary>
    public async Task<ClienteDto?> BuscarPorRutAsync(string rut, CancellationToken ct = default)
    {
        var plano = Rut.Normalizar(rut);
        if (plano.Length < 2) return null;

        var club = await _ajustes.ClubAsync(ct);

        var encontrado = await Proyectar()
            .FirstOrDefaultAsync(c => c.RutPlano == plano, ct);

        return encontrado is null
            ? null
            : Presentar(new[] { encontrado }, club.ValorPunto).First();
    }

    /* ==================================================================
       ESCRITURA
       ================================================================== */

    public async Task<ClienteDto> CrearAsync(
        GuardarClienteRequest peticion, CancellationToken ct = default)
    {
        ValidarRut(peticion.Rut);
        ValidarCumpleanos(peticion.CumpleMes, peticion.CumpleDia);

        var plano = Rut.Normalizar(peticion.Rut);

        // Se comprueba acá para dar un mensaje con el nombre de quien ya
        // existe. El índice único de la base es el que realmente lo garantiza.
        var existente = await _db.Clientes.AsNoTracking()
            .Where(c => c.Rut.Replace(".", "").Replace("-", "").ToUpper() == plano)
            .Select(c => new { c.Id, c.Nombre, c.Activo })
            .FirstOrDefaultAsync(ct);

        if (existente is not null)
        {
            throw new ExcepcionNegocio(
                existente.Activo
                    ? $"Ese RUT ya está registrado a nombre de {existente.Nombre}."
                    : $"Ese RUT pertenece a {existente.Nombre}, que está desactivado. " +
                      "Reactívalo en vez de crear una ficha nueva.",
                StatusCodes.Status409Conflict);
        }

        var cliente = new Cliente
        {
            Rut = Rut.Formatear(peticion.Rut),
            Nombre = peticion.Nombre.Trim(),
            Telefono = Limpiar(peticion.Telefono),
            Correo = Limpiar(peticion.Correo)?.ToLowerInvariant(),
            Direccion = Limpiar(peticion.Direccion),
            CumpleMes = peticion.CumpleMes,
            CumpleDia = peticion.CumpleDia,
            Notas = Limpiar(peticion.Notas),
            Puntos = 0,
            Activo = true
        };

        _db.Clientes.Add(cliente);
        await _db.SaveChangesAsync(ct);

        _log.LogInformation("Cliente creado: {Rut} · {Nombre} por {Autor}",
            cliente.Rut, cliente.Nombre, _usuarioActual.Email);

        return await ResumenAsync(cliente.Id, ct);
    }

    public async Task<ClienteDto> ActualizarAsync(
        int id, GuardarClienteRequest peticion, CancellationToken ct = default)
    {
        ValidarRut(peticion.Rut);
        ValidarCumpleanos(peticion.CumpleMes, peticion.CumpleDia);

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("El cliente");

        var plano = Rut.Normalizar(peticion.Rut);

        var duplicado = await _db.Clientes.AsNoTracking()
            .AnyAsync(c => c.Id != id &&
                           c.Rut.Replace(".", "").Replace("-", "").ToUpper() == plano, ct);

        if (duplicado)
            throw new ExcepcionNegocio("Ese RUT ya pertenece a otro cliente.",
                StatusCodes.Status409Conflict);

        cliente.Rut = Rut.Formatear(peticion.Rut);
        cliente.Nombre = peticion.Nombre.Trim();
        cliente.Telefono = Limpiar(peticion.Telefono);
        cliente.Correo = Limpiar(peticion.Correo)?.ToLowerInvariant();
        cliente.Direccion = Limpiar(peticion.Direccion);
        cliente.CumpleMes = peticion.CumpleMes;
        cliente.CumpleDia = peticion.CumpleDia;
        cliente.Notas = Limpiar(peticion.Notas);

        // Los puntos NO se tocan acá: se mueven con AjustarPuntos, que exige
        // motivo y deja registro. Editarlos en la ficha sería un agujero.
        await _db.SaveChangesAsync(ct);

        return await ResumenAsync(id, ct);
    }

    /// <summary>
    /// Desactiva la ficha. No se elimina: sus ventas históricas la
    /// referencian, y borrarla dejaría boletas apuntando a la nada.
    /// </summary>
    public async Task<ClienteDto> CambiarEstadoAsync(
        int id, bool activo, CancellationToken ct = default)
    {
        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("El cliente");

        if (!activo && cliente.Puntos > 0)
        {
            _log.LogWarning(
                "Se desactivó a {Nombre} con {Puntos} puntos sin canjear. Autor: {Autor}",
                cliente.Nombre, cliente.Puntos, _usuarioActual.Email);
        }

        cliente.Activo = activo;
        await _db.SaveChangesAsync(ct);

        return await ResumenAsync(id, ct);
    }

    /// <summary>
    /// Regala o descuenta puntos a mano.
    ///
    /// Los puntos no se editan: se mueven, y cada movimiento deja motivo y
    /// responsable. Sin ese libro, un saldo que no cuadra no se puede
    /// explicar, y los puntos son dinero.
    /// </summary>
    public async Task<ClienteDto> AjustarPuntosAsync(
        int id, AjustarPuntosRequest peticion, CancellationToken ct = default)
    {
        if (peticion.Cantidad == 0)
            throw new ExcepcionNegocio("La cantidad debe ser distinta de cero.");

        var club = await _ajustes.ClubAsync(ct);
        if (!club.Activo)
            throw new ExcepcionNegocio("El club de puntos está desactivado.");

        var cliente = await _db.Clientes.FirstOrDefaultAsync(c => c.Id == id, ct)
            ?? throw new NoEncontradoException("El cliente");

        var resultante = cliente.Puntos + peticion.Cantidad;

        if (resultante < 0)
            throw new ExcepcionNegocio(
                $"{cliente.Nombre} tiene {cliente.Puntos} puntos y se intentan " +
                $"descontar {Math.Abs(peticion.Cantidad)}.");

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        cliente.Puntos = resultante;

        _db.PuntosMovimientos.Add(new PuntoMovimiento
        {
            ClienteId = id,
            Cantidad = peticion.Cantidad,
            SaldoResultante = resultante,
            Motivo = peticion.Motivo.Trim(),
            UsuarioId = _usuarioActual.Id
        });

        await _db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        _log.LogWarning(
            "Ajuste manual de puntos: {Cantidad} a {Cliente} (saldo {Saldo}). " +
            "Motivo: {Motivo}. Autor: {Autor}",
            peticion.Cantidad, cliente.Nombre, resultante,
            peticion.Motivo, _usuarioActual.Email);

        return await ResumenAsync(id, ct);
    }

    /* ==================================================================
       HISTORIAL
       ================================================================== */

    public async Task<ResultadoPagina<CompraClienteDto>> HistorialComprasAsync(
        int id, ParametrosPagina parametros, CancellationToken ct = default)
    {
        if (!await _db.Clientes.AnyAsync(c => c.Id == id, ct))
            throw new NoEncontradoException("El cliente");

        var consulta = ConsultarCompras(id).OrderByDescending(v => v.Fecha);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .Skip(parametros.Saltar).Take(parametros.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<CompraClienteDto>.Crear(items, total, parametros);
    }

    public async Task<ResultadoPagina<MovimientoPuntosDto>> MovimientosPuntosAsync(
        int id, ParametrosPagina parametros, CancellationToken ct = default)
    {
        if (!await _db.Clientes.AnyAsync(c => c.Id == id, ct))
            throw new NoEncontradoException("El cliente");

        var consulta = ConsultarPuntos(id).OrderByDescending(m => m.Fecha);

        var total = await consulta.CountAsync(ct);
        var items = await consulta
            .Skip(parametros.Saltar).Take(parametros.PorPagina)
            .ToListAsync(ct);

        return ResultadoPagina<MovimientoPuntosDto>.Crear(items, total, parametros);
    }

    /// <summary>
    /// Quiénes cumplen años este mes. Es la campaña que más rinde en una
    /// florería, y por eso el cumpleaños se guarda como mes y día separados:
    /// así la consulta es un filtro directo sobre un índice.
    /// </summary>
    public async Task<IReadOnlyList<CumpleanosDto>> CumpleanosDelMesAsync(
        short? mes, CancellationToken ct = default)
    {
        var hoy = DateOnly.FromDateTime(DateTime.Today);
        var elMes = mes ?? (short)hoy.Month;

        if (elMes is < 1 or > 12)
            throw new ExcepcionNegocio("El mes debe estar entre 1 y 12.");

        var clientes = await _db.Clientes.AsNoTracking()
            .Where(c => c.Activo && c.CumpleMes == elMes && c.CumpleDia != null)
            .Select(c => new
            {
                c.Id,
                c.Nombre,
                c.Telefono,
                c.Correo,
                c.Notas,
                c.Puntos,
                Dia = c.CumpleDia!.Value,
                TotalComprado = c.Compras.Where(v => !v.Anulada).Sum(v => (long)v.Total)
            })
            .ToListAsync(ct);

        return clientes
            .Select(c => new CumpleanosDto
            {
                ClienteId = c.Id,
                Nombre = c.Nombre,
                Telefono = c.Telefono,
                Correo = c.Correo,
                Dia = c.Dia,
                Mes = elMes,
                // Negativo si ya pasó: quien mira la lista a mitad de mes
                // necesita distinguir a quién ya saludó
                DiasFaltantes = elMes == hoy.Month ? c.Dia - hoy.Day : c.Dia,
                Puntos = c.Puntos,
                TotalComprado = c.TotalComprado,
                Notas = c.Notas
            })
            .OrderBy(c => c.Dia)
            .ToList();
    }

    /* ==================================================================
       INTERNO
       ================================================================== */

    private sealed class ClienteBase
    {
        public int Id { get; init; }
        public string Rut { get; init; } = string.Empty;
        public string RutPlano { get; init; } = string.Empty;
        public string Nombre { get; init; } = string.Empty;
        public string? Telefono { get; init; }
        public string? Correo { get; init; }
        public string? Direccion { get; init; }
        public short? CumpleMes { get; init; }
        public short? CumpleDia { get; init; }
        public string? Notas { get; init; }
        public int Puntos { get; init; }
        public bool Activo { get; init; }
        public DateTimeOffset CreadoEn { get; init; }
        public int Compras { get; init; }
        public long TotalComprado { get; init; }
        public DateTimeOffset? UltimaCompra { get; init; }
    }

    /// <summary>
    /// El RUT plano se calcula en la consulta para que la búsqueda encuentre
    /// escriba quien escriba: con puntos, sin puntos o con guion.
    /// </summary>
    private IQueryable<ClienteBase> Proyectar()
        => _db.Clientes.AsNoTracking().Select(c => new ClienteBase
        {
            Id = c.Id,
            Rut = c.Rut,
            RutPlano = c.Rut.Replace(".", "").Replace("-", "").ToUpper(),
            Nombre = c.Nombre,
            Telefono = c.Telefono,
            Correo = c.Correo,
            Direccion = c.Direccion,
            CumpleMes = c.CumpleMes,
            CumpleDia = c.CumpleDia,
            Notas = c.Notas,
            Puntos = c.Puntos,
            Activo = c.Activo,
            CreadoEn = c.CreadoEn,
            Compras = c.Compras.Count(v => !v.Anulada),
            TotalComprado = c.Compras.Where(v => !v.Anulada).Sum(v => (long)v.Total),
            UltimaCompra = c.Compras.Where(v => !v.Anulada)
                                    .Max(v => (DateTimeOffset?)v.CreadoEn)
        });

    /// <summary>
    /// Da formato al RUT y arma los textos para mostrar. Se hace en memoria
    /// porque son cálculos de presentación que PostgreSQL no tiene por qué
    /// resolver.
    /// </summary>
    private static List<ClienteDto> Presentar(IEnumerable<ClienteBase> filas, int valorPunto)
    {
        var ahora = DateTimeOffset.UtcNow;

        return filas.Select(c => new ClienteDto
        {
            Id = c.Id,
            Rut = Rut.Formatear(c.Rut),
            Nombre = c.Nombre,
            Telefono = c.Telefono,
            Correo = c.Correo,
            Direccion = c.Direccion,
            CumpleMes = c.CumpleMes,
            CumpleDia = c.CumpleDia,
            Cumpleanos = c.CumpleMes.HasValue && c.CumpleDia.HasValue
                ? $"{c.CumpleDia} de {Meses[c.CumpleMes.Value - 1]}"
                : null,
            Notas = c.Notas,
            Puntos = c.Puntos,
            // Los puntos valen dinero: mostrarlo evita que el cliente
            // pregunte "¿y eso cuánto es?" en cada visita.
            ValorPuntos = c.Puntos * valorPunto,
            Activo = c.Activo,
            CreadoEn = c.CreadoEn,
            Compras = c.Compras,
            TotalComprado = c.TotalComprado,
            UltimaCompra = c.UltimaCompra,
            DiasSinComprar = c.UltimaCompra.HasValue
                ? (int)(ahora - c.UltimaCompra.Value).TotalDays
                : null
        }).ToList();
    }

    private IQueryable<CompraClienteDto> ConsultarCompras(int clienteId)
        => _db.Ventas.AsNoTracking()
            .Where(v => v.ClienteId == clienteId)
            .Select(v => new CompraClienteDto
            {
                VentaId = v.Id,
                Folio = v.Folio,
                Fecha = v.CreadoEn,
                Total = v.Total,
                DescuentoTotal = v.DescuentoTotal,
                MedioPago = v.MedioPago.ToString(),
                PuntosGanados = v.PuntosGanados,
                PuntosCanjeados = v.PuntosCanjeados,
                Anulada = v.Anulada,
                Lineas = v.Items.Count
            });

    private IQueryable<MovimientoPuntosDto> ConsultarPuntos(int clienteId)
        => _db.PuntosMovimientos.AsNoTracking()
            .Where(m => m.ClienteId == clienteId)
            .Select(m => new MovimientoPuntosDto
            {
                Id = m.Id,
                Fecha = m.CreadoEn,
                Cantidad = m.Cantidad,
                SaldoResultante = m.SaldoResultante,
                Motivo = m.Motivo,
                Folio = m.Venta != null ? m.Venta.Folio : null,
                Usuario = m.Usuario != null ? m.Usuario.Nombre : null
            });

    private async Task<ClienteDto> ResumenAsync(int id, CancellationToken ct)
    {
        var club = await _ajustes.ClubAsync(ct);
        var fila = await Proyectar().FirstAsync(c => c.Id == id, ct);
        return Presentar(new[] { fila }, club.ValorPunto).First();
    }

    private static void ValidarRut(string rut)
    {
        if (Rut.EsValido(rut)) return;

        var plano = Rut.Normalizar(rut);

        // Si el cuerpo es numérico se puede decir cuál era el dígito correcto:
        // casi siempre el error es ese, no el número.
        if (plano.Length >= 2 && plano[..^1].All(char.IsDigit))
        {
            var correcto = Rut.CalcularDv(plano[..^1]);
            throw new ExcepcionNegocio(
                $"El RUT {Rut.Formatear(rut)} no es válido: el dígito verificador " +
                $"debería ser {correcto}.");
        }

        throw new ExcepcionNegocio($"El RUT «{rut}» no tiene un formato válido.");
    }

    /// <summary>
    /// El día tiene que existir en ese mes. La base valida los rangos, pero
    /// aceptaría un 31 de febrero.
    /// </summary>
    private static void ValidarCumpleanos(short? mes, short? dia)
    {
        if (mes is null && dia is null) return;

        if (mes is null || dia is null)
            throw new ExcepcionNegocio("Indica el mes y el día de cumpleaños, o ninguno.");

        // Año bisiesto a propósito: el 29 de febrero es un cumpleaños válido
        var maximo = DateTime.DaysInMonth(2024, mes.Value);

        if (dia.Value > maximo)
            throw new ExcepcionNegocio(
                $"{Meses[mes.Value - 1]} tiene {maximo} días: el {dia} no existe.");
    }

    private static string? Limpiar(string? texto)
        => string.IsNullOrWhiteSpace(texto) ? null : texto.Trim();
}