using Colibri.Api.Auth;
using Colibri.Api.BLL;
using Colibri.Api.DAL;
using Colibri.Api.DbAccess;
using Colibri.Api.Endpoints;
using Colibri.Api.Models.Enums;
using Colibri.Api.Utils;
using Dapper;
using Microsoft.OpenApi.Models;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

// ─── 1. Data source con los enums mapeados ───────────────────
// Un enum sin mapear NO falla al arrancar: falla en el primer request que
// devuelva esa columna, con un error de Npgsql que no menciona el enum.
// Por eso el chequeo del punto 4 lee uno de cada módulo.
var cadena = builder.Configuration.GetConnectionString("Colibri")
    ?? throw new InvalidOperationException("Falta ConnectionStrings:Colibri.");

var dsb = new NpgsqlDataSourceBuilder(cadena);

dsb.MapEnum<TipoProducto>("tipo_producto");
dsb.MapEnum<RolUsuario>("rol_usuario");
dsb.MapEnum<TipoMovimiento>("tipo_movimiento");
dsb.MapEnum<EstadoCompra>("estado_compra");
dsb.MapEnum<TipoPresentacion>("tipo_presentacion");
dsb.MapEnum<EstadoLote>("estado_lote");
dsb.MapEnum<CalidadReingreso>("calidad_reingreso");

// Ventas
dsb.MapEnum<EstadoCaja>("estado_caja");
dsb.MapEnum<MedioPago>("medio_pago");
dsb.MapEnum<TipoConsumo>("tipo_consumo");
dsb.MapEnum<TipoPromocion>("tipo_promocion");
dsb.MapEnum<AlcancePromocion>("alcance_promocion");
dsb.MapEnum<DestinoMerma>("destino_merma");

// Pendientes, cuando lleguen sus módulos:
// dsb.MapEnum<MedioPago>("medio_pago");                 ← Ventas
// dsb.MapEnum<EstadoCaja>("estado_caja");
// dsb.MapEnum<TipoConsumo>("tipo_consumo");        ← Mermas
// dsb.MapEnum<CalidadReingreso>("calidad_reingreso");
// dsb.MapEnum<TipoPromocion>("tipo_promocion");         ← Promociones
// dsb.MapEnum<AlcancePromocion>("alcance_promocion");
// dsb.MapEnum<EstadoCotizacion>("estado_cotizacion");   ← Cotizaciones

builder.Services.AddSingleton(dsb.Build());

DefaultTypeMap.MatchNamesWithUnderscores = true;

SqlMapper.AddTypeHandler(DateOnlyHandler.Instancia);
SqlMapper.AddTypeHandler(DateOnlyNullableHandler.Instancia);
SqlMapper.AddTypeHandler(TimeOnlyHandler.Instancia);

// ─── 3. Capas ────────────────────────────────────────────────
builder.Services.AddSingleton<IAccesoDatos, AccesoDatosPostgres>();

builder.Services.AddScoped<AccesoDAL>();
builder.Services.AddScoped<AccesoBLL>();
builder.Services.AddScoped<InventarioDAL>();
builder.Services.AddScoped<InventarioBLL>();
builder.Services.AddScoped<AbastecimientoDAL>();
builder.Services.AddScoped<AbastecimientoBLL>();
builder.Services.AddScoped<LotesDAL>();
builder.Services.AddScoped<LotesBLL>();
//ventas
builder.Services.AddScoped<CajaDAL>();
builder.Services.AddScoped<CajaBLL>();
builder.Services.AddScoped<MostradorDAL>();
builder.Services.AddScoped<MostradorBLL>();
builder.Services.AddScoped<VentasDAL>();
builder.Services.AddScoped<VentasBLL>();

//mermas
builder.Services.AddScoped<MermasDAL>();
builder.Services.AddScoped<MermasBLL>();

//configuracion
builder.Services.AddScoped<ConfiguracionDAL>();
builder.Services.AddScoped<ConfiguracionBLL>();

//clientes
builder.Services.AddScoped<ClientesDAL>();
builder.Services.AddScoped<ClientesBLL>();

// IJwtTokenService se registra dentro de AgregarSeguridad().
builder.Services.AgregarSeguridad(builder.Configuration);

// ─── 4. JSON ─────────────────────────────────────────────────
// Sin esto los enums salen como número: el front recibe rol: 0 en vez de
// "admin", y cualquier comparación contra el nombre falla en silencio.
// ConfigureHttpJsonOptions es el de minimal API; AddJsonOptions es para
// controllers y acá no haría nada.
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(
        new System.Text.Json.Serialization.JsonStringEnumConverter());
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(o =>
{
    o.SwaggerDoc("v1", new OpenApiInfo { Title = "Colibrí API", Version = "v1" });

    // El candadito. Sin esto hay que pegar el header a mano en cada request,
    // que con cuarenta endpoints se vuelve insufrible.
    o.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Pega solo el token, sin escribir 'Bearer'."
    });

    o.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        [new OpenApiSecurityScheme
        {
            Reference = new OpenApiReference
            {
                Type = ReferenceType.SecurityScheme,
                Id = "Bearer"
            }
        }] = Array.Empty<string>()
    });
});

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origenes").Get<string[]>() ?? [])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

// ─── 5. Chequeo de arranque ──────────────────────────────────
// Confirma que la conexión abre y que cada enum está donde el modelo cree.
// Si esto lanza, el contenedor no arranca y Dokploy mantiene el anterior.
//
// Reintenta porque si Postgres arranca después que la API —mismo VPS, orden
// no garantizado— fallar al primer intento deja el contenedor en
// crash-loop. Un enum mal mapeado tampoco se arregla esperando, así que a
// los cinco intentos muere igual, que es lo correcto.
for (var intento = 1; ; intento++)
{
    try
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IAccesoDatos>();

        var productos = await db.Escalar<long>("SELECT count(*) FROM productos");
        var tipo = await db.Escalar<TipoProducto>("SELECT tipo FROM productos LIMIT 1");
        var rol = await db.Escalar<RolUsuario>("SELECT rol FROM usuarios LIMIT 1");

        // Con COALESCE por si las tablas están vacías: leer NULL no prueba el
        // mapeo, pero tampoco tumba el arranque de una instalación nueva.
        var estado = await db.Escalar<EstadoCompra>(
            "SELECT COALESCE((SELECT estado FROM compras LIMIT 1), 'borrador'::estado_compra)");
        var lote = await db.Escalar<EstadoLote>(
            "SELECT COALESCE((SELECT estado FROM lotes LIMIT 1), 'activo'::estado_lote)");
        var pres = await db.Escalar<TipoPresentacion>(
            "SELECT COALESCE((SELECT tipo FROM presentaciones LIMIT 1), 'paquete'::tipo_presentacion)");
        var mov = await db.Escalar<TipoMovimiento>(
            "SELECT COALESCE((SELECT tipo FROM movimientos_inventario LIMIT 1), 'alta'::tipo_movimiento)");
        var caja = await db.Escalar<EstadoCaja>(
            "SELECT COALESCE((SELECT estado FROM cajas LIMIT 1), 'abierta'::estado_caja)");

        app.Logger.LogInformation(
            "Base OK — {N} productos · enums: {Tipo}, {Rol}, {Estado}, {Lote}, {Pres}, {Mov}, {Caja}",
            productos, tipo, rol, estado, lote, pres, mov, caja);
        break;
    }
    catch (Exception ex) when (intento < 5)
    {
        app.Logger.LogWarning("Base no responde (intento {N}/5): {Msg}", intento, ex.Message);
        await Task.Delay(2000);
    }
}
// ─── 6. Red de seguridad ─────────────────────────────────────
// Los DAL ya traducen los errores de negocio. Esto atrapa lo que se escape:
// una PostgresException que llegue acá es un bug y el log tiene que verlo.
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (PostgresException ex)
    {
        var log = ctx.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Postgres");
        log.LogError(ex, "Error PG {SqlState} en {Ruta}", ex.SqlState, ctx.Request.Path);

        ctx.Response.StatusCode = ErroresPg.HttpStatus(ex);
        await ctx.Response.WriteAsJsonAsync(CustomUtilz.CreateResponse(
            ctx.Response.StatusCode, ErroresPg.Mensaje(ex), null));
    }
});

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();

// ─── 7. Endpoints ────────────────────────────────────────────
// Solo el logger va en el constructor. El BLL entra por parámetro de cada
// handler, para no dejar un servicio Scoped vivo desde el arranque.
ILogger<T> Log<T>() => app.Services.GetRequiredService<ILogger<T>>();

new AccesoEndpoints(Log<AccesoEndpoints>()).MapEndpoints(app);
new InventarioEndpoints(Log<InventarioEndpoints>()).MapEndpoints(app);
new AbastecimientoEndpoints(Log<AbastecimientoEndpoints>()).MapEndpoints(app);
new LotesEndpoints(Log<LotesEndpoints>()).MapEndpoints(app);
new MostradorEndpoints(Log<MostradorEndpoints>()).MapEndpoints(app);
new CajaEndpoints(Log<CajaEndpoints>()).MapEndpoints(app);
new VentasEndpoints(Log<VentasEndpoints>()).MapEndpoints(app);
new ConfiguracionEndpoints(Log<ConfiguracionEndpoints>()).MapEndpoints(app);
new MermasEndpoints(Log<MermasEndpoints>()).MapEndpoints(app);
new ClientesEndpoints(Log<ClientesEndpoints>()).MapEndpoints(app);
app.Run();