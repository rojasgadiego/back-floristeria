using Colibri.Api.Features.Auth;
using Colibri.Api.Features.Usuarios;
using Colibri.Api.Startup;
using Colibri.Api.Features.Inventario;
using Colibri.Api.Features.Compras;
using Colibri.Api.Features.Lotes;
using Colibri.Api.Features.Ventas;
using Colibri.Api.Features.Mermas;
using Colibri.Api.Features.Clientes;
using Colibri.Api.Features.Configuracion;
using Colibri.Api.Features.Promociones;
using Colibri.Api.Features.Cotizaciones;
using Colibri.Api.Features.Reportes;
using Colibri.Api.Features.InventarioVenta;

var builder = WebApplication.CreateBuilder(args);

// No anunciar el servidor ni su versi�n: no ayuda a nadie salvo a quien
// busca vulnerabilidades conocidas de una versi�n concreta.
builder.WebHost.ConfigureKestrel(o => o.AddServerHeader = false);

// Infraestructura
builder.Services
    .AgregarProxyInverso(builder.Configuration)
    .AgregarHsts(builder.Configuration)
    .AgregarBaseDeDatos(builder.Configuration, builder.Environment)
    .AgregarAutenticacion(builder.Configuration)
    .AgregarControladores()
    .AgregarDocumentacion()
    .AgregarCors(builder.Configuration)
    .AgregarLimitesDePeticiones();

// M�dulos de negocio
builder.Services
    .AgregarAuth(builder.Configuration)
    .AgregarUsuarios()
    .AgregarInventario()
    .AgregarInventarioVenta() 
    .AgregarCompras()
    .AgregarLotes(builder.Configuration)
    .AgregarVentas()
    .AgregarMermas()
    .AgregarClientes()
    .AgregarConfiguracion()
    .AgregarPromociones()
    .AgregarCotizaciones()
    .AgregarReportes();

var app = builder.Build();

app.VerificarConfiguracion();
app.ConfigurarTuberia();
await app.VerificarBaseDeDatosAsync();

app.Run();