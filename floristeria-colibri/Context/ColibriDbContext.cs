using Colibri.Api.Domain;
using Colibri.Api.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Colibri.Api.Context;

/// <summary>
/// Mapea el esquema PostgreSQL existente. NO hay migraciones: el SQL es la
/// fuente de verdad, porque contiene reglas que EF Core no sabe generar ni
/// preservar (CHECK, triggers, índices únicos parciales y vistas).
///
/// Si alguna vez corres Add-Migration, revisa lo que genera antes de
/// aplicarlo: intentará "corregir" la base hacia lo que cree que debería ser.
/// </summary>
public class ColibriDbContext : DbContext
{
    public ColibriDbContext(DbContextOptions<ColibriDbContext> options) : base(options) { }

    // --- Tablas ---
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<Configuracion> Configuraciones => Set<Configuracion>();
    public DbSet<Categoria> Categorias => Set<Categoria>();
    public DbSet<Producto> Productos => Set<Producto>();
    public DbSet<Receta> Recetas => Set<Receta>();
    public DbSet<MovimientoInventario> MovimientosInventario => Set<MovimientoInventario>();
    public DbSet<Merma> Mermas => Set<Merma>();
    public DbSet<Proveedor> Proveedores => Set<Proveedor>();
    public DbSet<Presentacion> Presentaciones => Set<Presentacion>();
    public DbSet<Compra> Compras => Set<Compra>();
    public DbSet<CompraItem> CompraItems => Set<CompraItem>();
    public DbSet<Lote> Lotes => Set<Lote>();
    public DbSet<Cliente> Clientes => Set<Cliente>();
    public DbSet<PuntoMovimiento> PuntosMovimientos => Set<PuntoMovimiento>();
    public DbSet<Promocion> Promociones => Set<Promocion>();
    public DbSet<Caja> Cajas => Set<Caja>();
    public DbSet<Venta> Ventas => Set<Venta>();
    public DbSet<VentaItem> VentaItems => Set<VentaItem>();
    public DbSet<VentaConsumo> VentaConsumos => Set<VentaConsumo>();
    public DbSet<Cotizacion> Cotizaciones => Set<Cotizacion>();
    public DbSet<CotizacionItem> CotizacionItems => Set<CotizacionItem>();
    public DbSet<CotizacionPago> CotizacionPagos => Set<CotizacionPago>();
    public DbSet<CotizacionCuota> CotizacionCuotas => Set<CotizacionCuota>();

    // --- Vistas (solo lectura) ---
    public DbSet<ProductoDisponible> ProductosDisponibles => Set<ProductoDisponible>();
    public DbSet<ProductoCosto> ProductoCostos => Set<ProductoCosto>();
    public DbSet<ResultadoDiario> ResultadosDiarios => Set<ResultadoDiario>();
    public DbSet<CompromisoEvento> CompromisosEventos => Set<CompromisoEvento>();
    public DbSet<LoteActivo> LotesActivos => Set<LoteActivo>();
    public DbSet<LoteRecuperado> LotesRecuperados => Set<LoteRecuperado>();
    public DbSet<CostoPromedio> CostosPromedio => Set<CostoPromedio>();
    public DbSet<EvolucionCosto> EvolucionCostos => Set<EvolucionCosto>();
    public DbSet<CotizacionSaldo> CotizacionesSaldo => Set<CotizacionSaldo>();

    /// <summary>Existencias separadas por lado: bodega y mostrador.</summary>
    public DbSet<Existencia> Existencias => Set<Existencia>();

    /// <summary>Lo que el vendedor puede vender ahora mismo.</summary>
    public DbSet<Vendible> Vendibles => Set<Vendible>();

    // --- Resultados de funciones. Solo lectura, vía FromSql ---
    public DbSet<ConsumoLote> ConsumosLote => Set<ConsumoLote>();
    public DbSet<RecepcionLote> RecepcionesLote => Set<RecepcionLote>();
    public DbSet<ValidacionLote> ValidacionesLote => Set<ValidacionLote>();
    public DbSet<ReingresoLote> ReingresosLote => Set<ReingresoLote>();
    public DbSet<ResultadoTraspaso> ResultadosTraspaso => Set<ResultadoTraspaso>();
    public DbSet<ResultadoConteo> ResultadosConteo => Set<ResultadoConteo>();


    protected override void OnModelCreating(ModelBuilder mb)
    {
        base.OnModelCreating(mb);

        // Los tipos ENUM de PostgreSQL deben declararse en el modelo.
        // Si falta alguno, Npgsql no sabe convertirlo y falla cualquier
        // consulta que toque esa columna.
        mb.HasPostgresEnum<RolUsuario>("rol_usuario");
        mb.HasPostgresEnum<TipoProducto>("tipo_producto");
        mb.HasPostgresEnum<TipoMovimiento>("tipo_movimiento");
        mb.HasPostgresEnum<MedioPago>("medio_pago");
        mb.HasPostgresEnum<EstadoCaja>("estado_caja");
        mb.HasPostgresEnum<TipoPromocion>("tipo_promocion");
        mb.HasPostgresEnum<AlcancePromocion>("alcance_promocion");
        mb.HasPostgresEnum<EstadoCotizacion>("estado_cotizacion");
        mb.HasPostgresEnum<TipoConsumo>("tipo_consumo");
        mb.HasPostgresEnum<TipoPresentacion>("tipo_presentacion");
        mb.HasPostgresEnum<EstadoLote>("estado_lote");
        mb.HasPostgresEnum<EstadoCompra>("estado_compra");
        mb.HasPostgresEnum<DestinoMerma>("destino_merma");
        mb.HasPostgresEnum<CalidadReingreso>("calidad_reingreso");

        // Bodega o mostrador. Sin esta línea, cualquier consulta que toque
        // lotes.ubicacion falla en tiempo de ejecución con un error de
        // Npgsql que no menciona la causa.
        mb.HasPostgresEnum<Ubicacion>("ubicacion_inventario");

        ConfigurarAcceso(mb);
        ConfigurarCatalogo(mb);
        ConfigurarAbastecimiento(mb);
        ConfigurarClientes(mb);
        ConfigurarVentas(mb);
        ConfigurarCotizaciones(mb);
        ConfigurarVistas(mb);

        AplicarNombresSnakeCase(mb);
    }

    private static void ConfigurarAcceso(ModelBuilder mb)
    {
        mb.Entity<Usuario>(e =>
        {
            e.ToTable("usuarios");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");
            // El índice es sobre lower(email); acá solo se documenta la intención
            e.HasIndex(x => x.Email).HasDatabaseName("usuarios_email_unico");
        });

        mb.Entity<Configuracion>(e =>
        {
            e.ToTable("configuracion");
            e.HasKey(x => x.Clave);
            e.Property(x => x.Valor).HasColumnType("jsonb");
            e.HasOne(x => x.UsuarioActualizo)
             .WithMany()
             .HasForeignKey(x => x.ActualizadoPor)
             .OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigurarCatalogo(ModelBuilder mb)
    {
        mb.Entity<Categoria>(e =>
        {
            e.ToTable("categorias");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Nombre).IsUnique();
        });

        mb.Entity<Producto>(e =>
        {
            e.ToTable("productos");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Codigo).IsUnique();
            e.Property(x => x.Emoji).HasDefaultValue("🌿");
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Categoria)
             .WithMany(c => c.Productos)
             .HasForeignKey(x => x.CategoriaId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Receta>(e =>
        {
            e.ToTable("recetas");
            e.HasKey(x => new { x.ProductoId, x.ComponenteId });

            // Dos relaciones a la misma tabla: el ramo y el tallo.
            e.HasOne(x => x.Producto)
             .WithMany(p => p.Receta)
             .HasForeignKey(x => x.ProductoId)
             .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Componente)
             .WithMany(p => p.UsadoEn)
             .HasForeignKey(x => x.ComponenteId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<MovimientoInventario>(e =>
        {
            e.ToTable("movimientos_inventario");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");

            // De qué lado ocurrió. Sin esto un traspaso se ve igual que una
            // salida y el libro deja de cuadrar por ubicación.
            e.Property(x => x.Ubicacion).HasDefaultValue(Ubicacion.bodega);

            e.HasOne(x => x.Producto)
             .WithMany(p => p.Movimientos)
             .HasForeignKey(x => x.ProductoId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Usuario)
             .WithMany()
             .HasForeignKey(x => x.UsuarioId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Lote)
             .WithMany()
             .HasForeignKey(x => x.LoteId)
             .OnDelete(DeleteBehavior.SetNull);

            e.HasIndex(x => new { x.ProductoId, x.CreadoEn });
        });

        mb.Entity<Merma>(e =>
        {
            e.ToTable("mermas");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");

            // Generada por PostgreSQL: EF la lee pero nunca la escribe.
            // De ella dependen todos los reportes de merma, y si se pudiera
            // escribir a mano, una merma parcial mal registrada inflaría la
            // pérdida del mes.
            e.Property(x => x.CostoPerdido)
             .HasComputedColumnSql(
                 "CASE WHEN destino = 'devolucion_proveedor' THEN 0 ELSE " +
                 "costo_unitario * (cantidad - cantidad_recuperada) + " +
                 "(costo_unitario - COALESCE(costo_recuperado_unitario, costo_unitario)) " +
                 "* cantidad_recuperada END",
                 stored: true);

            e.HasOne(x => x.Producto).WithMany()
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Usuario).WithMany(u => u.Mermas)
             .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne<Usuario>().WithMany()
             .HasForeignKey(x => x.RevertidaPor).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Lote).WithMany()
             .HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.SetNull);

            // Lote donde quedaron las varas recuperadas. Puede ser el mismo
            // de origen (flor óptima) o uno de recuperación.
            e.HasOne<Lote>().WithMany()
             .HasForeignKey(x => x.LoteRecuperacionId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigurarAbastecimiento(ModelBuilder mb)
    {
        mb.Entity<Proveedor>(e =>
        {
            e.ToTable("proveedores");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");
        });

        mb.Entity<Presentacion>(e =>
        {
            e.ToTable("presentaciones");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.ProductoId, x.Nombre }).IsUnique();

            // Columna generada en PostgreSQL: EF la lee pero nunca la escribe
            e.Property(x => x.VarasTotales)
             .HasComputedColumnSql("paquetes * varas_por_paquete", stored: true);

            e.HasOne(x => x.Producto).WithMany(p => p.Presentaciones)
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });

        mb.Entity<Compra>(e =>
        {
            e.ToTable("compras");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Folio).IsUnique();
            e.Property(x => x.Fecha).HasDefaultValueSql("CURRENT_DATE");
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Proveedor).WithMany(p => p.Compras)
             .HasForeignKey(x => x.ProveedorId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Usuario).WithMany()
             .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<CompraItem>(e =>
        {
            e.ToTable("compra_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.CostoPorVara).HasColumnType("numeric(12,4)");

            e.HasOne(x => x.Compra).WithMany(c => c.Items)
             .HasForeignKey(x => x.CompraId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Producto).WithMany()
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Presentacion).WithMany()
             .HasForeignKey(x => x.PresentacionId).OnDelete(DeleteBehavior.Restrict);
        });

        mb.Entity<Lote>(e =>
        {
            e.ToTable("lotes");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Codigo).IsUnique();   // es lo que va en el QR
            e.Property(x => x.CostoPorVara).HasColumnType("numeric(12,4)");
            e.Property(x => x.FechaIngreso).HasDefaultValueSql("CURRENT_DATE");
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");

            // Bodega o mostrador. Un lote nace en bodega: al frente solo se
            // llega traspasando, que es lo que lo vuelve vendible.
            e.Property(x => x.Ubicacion).HasDefaultValue(Ubicacion.bodega);

            // Dónde está guardado el balde: 'Cámara 1, estante 3'. Es una nota
            // para encontrarlo, NO tiene que ver con bodega/mostrador. Se
            // llamaba Ubicacion, y por eso se renombró: dos conceptos con el
            // mismo nombre se confunden solos.
            e.Property(x => x.UbicacionFisica).HasColumnName("ubicacion_fisica");

            e.HasOne(x => x.Producto).WithMany(p => p.Lotes)
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Compra).WithMany(c => c.Lotes)
             .HasForeignKey(x => x.CompraId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Proveedor).WithMany()
             .HasForeignKey(x => x.ProveedorId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Presentacion).WithMany()
             .HasForeignKey(x => x.PresentacionId).OnDelete(DeleteBehavior.SetNull);

            // Autorreferencia: un lote de recuperación apunta al que le dio
            // origen, y una partida del mostrador al lote de bodega del que
            // bajó. De ahí hereda fecha de ingreso, vencimiento y costo.
            e.HasOne(x => x.OrigenLote).WithMany()
             .HasForeignKey(x => x.OrigenLoteId).OnDelete(DeleteBehavior.SetNull);

            // El índice que hace barato el FIFO. Lleva la ubicación porque la
            // fila de consumo es propia de cada lado.
            e.HasIndex(x => new { x.ProductoId, x.Ubicacion, x.FechaIngreso, x.Id })
             .HasDatabaseName("lotes_fifo_idx");
        });
    }

    private static void ConfigurarClientes(ModelBuilder mb)
    {
        mb.Entity<Cliente>(e =>
        {
            e.ToTable("clientes");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.CumpleMes, x.CumpleDia });
        });

        mb.Entity<PuntoMovimiento>(e =>
        {
            e.ToTable("puntos_movimientos");
            e.HasKey(x => x.Id);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Cliente).WithMany(c => c.MovimientosPuntos)
             .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Venta).WithMany()
             .HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Usuario).WithMany()
             .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<Promocion>(e =>
        {
            e.ToTable("promociones");
            e.HasKey(x => x.Id);
            e.Property(x => x.Dias).HasColumnType("smallint[]");
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Categoria).WithMany()
             .HasForeignKey(x => x.CategoriaId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Producto).WithMany()
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarVentas(ModelBuilder mb)
    {
        mb.Entity<Caja>(e =>
        {
            e.ToTable("cajas");
            e.HasKey(x => x.Id);
            e.Property(x => x.AbiertaEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.UsuarioApertura).WithMany()
             .HasForeignKey(x => x.AbiertaPor).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.UsuarioCierre).WithMany()
             .HasForeignKey(x => x.CerradaPor).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<Venta>(e =>
        {
            e.ToTable("ventas");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Folio).IsUnique();
            e.Property(x => x.IvaTasa).HasColumnType("numeric(5,2)").HasDefaultValue(19m);
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Caja).WithMany(c => c.Ventas)
             .HasForeignKey(x => x.CajaId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Usuario).WithMany(u => u.Ventas)
             .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Cliente).WithMany(c => c.Compras)
             .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Promocion).WithMany()
             .HasForeignKey(x => x.PromocionId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.UsuarioAnulacion).WithMany()
             .HasForeignKey(x => x.AnuladaPor).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<VentaItem>(e =>
        {
            e.ToTable("venta_items");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Venta).WithMany(v => v.Items)
             .HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Producto).WithMany()
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<VentaConsumo>(e =>
        {
            e.ToTable("venta_consumos");
            e.HasKey(x => x.Id);
            e.Property(x => x.CostoUnitario).HasColumnType("numeric(12,4)");

            // Incluye el lote: una sola línea de boleta puede consumir dos
            // lotes distintos del mismo producto.
            e.HasIndex(x => new { x.VentaId, x.ProductoId, x.Tipo, x.LoteId }).IsUnique();

            e.HasOne(x => x.Venta).WithMany(v => v.Consumos)
             .HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Producto).WithMany()
             .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);

            e.HasOne(x => x.Lote).WithMany()
             .HasForeignKey(x => x.LoteId).OnDelete(DeleteBehavior.SetNull);
        });
    }

    private static void ConfigurarCotizaciones(ModelBuilder mb)
    {
        mb.Entity<Cotizacion>(e =>
        {
            e.ToTable("cotizaciones");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Folio).IsUnique();
            e.Property(x => x.CreadoEn).HasDefaultValueSql("now()");
            e.Property(x => x.ActualizadoEn).HasDefaultValueSql("now()");

            e.HasOne(x => x.Cliente).WithMany()
             .HasForeignKey(x => x.ClienteId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.UsuarioCreador).WithMany()
             .HasForeignKey(x => x.CreadaPor).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Venta).WithMany()
             .HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<CotizacionItem>(e =>
        {
            e.ToTable("cotizacion_items");
            e.HasKey(x => x.Id);

            e.HasOne(x => x.Cotizacion).WithMany(c => c.Items)
            .HasForeignKey(x => x.CotizacionId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Producto).WithMany()
            .HasForeignKey(x => x.ProductoId).OnDelete(DeleteBehavior.Restrict);
        });


        // ventas.cotizacion_id se declara desde este lado para evitar el ciclo
        mb.Entity<Venta>()
          .HasOne(v => v.Cotizacion).WithMany()
          .HasForeignKey(v => v.CotizacionId)
          .OnDelete(DeleteBehavior.SetNull);

        mb.Entity<CotizacionPago>(e =>
        {
            e.ToTable("cotizacion_pagos");
            e.HasKey(x => x.Id);
            e.Property(x => x.Fecha).HasDefaultValueSql("now()");
            e.HasIndex(x => new { x.CotizacionId, x.Fecha });

            e.HasOne(x => x.Cotizacion).WithMany(c => c.Pagos)
             .HasForeignKey(x => x.CotizacionId).OnDelete(DeleteBehavior.Cascade);

            e.HasOne(x => x.Venta).WithMany()
             .HasForeignKey(x => x.VentaId).OnDelete(DeleteBehavior.SetNull);

            e.HasOne(x => x.Usuario).WithMany()
             .HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.SetNull);
        });

        mb.Entity<CotizacionCuota>(e =>
        {
            e.ToTable("cotizacion_cuotas");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.CotizacionId, x.Numero }).IsUnique();

            e.HasOne(x => x.Cotizacion).WithMany(c => c.Cuotas)
             .HasForeignKey(x => x.CotizacionId).OnDelete(DeleteBehavior.Cascade);
        });
    }

    private static void ConfigurarVistas(ModelBuilder mb)
    {
        mb.Entity<ProductoDisponible>().HasNoKey().ToView("vw_productos_disponibles");
        mb.Entity<ProductoCosto>().HasNoKey().ToView("vw_producto_costos");
        mb.Entity<ResultadoDiario>().HasNoKey().ToView("vw_resultado_diario");
        mb.Entity<CompromisoEvento>().HasNoKey().ToView("vw_compromiso_eventos");
        mb.Entity<LoteActivo>().HasNoKey().ToView("vw_lotes_activos");
        mb.Entity<LoteRecuperado>().HasNoKey().ToView("vw_lotes_recuperados");
        mb.Entity<CostoPromedio>().HasNoKey().ToView("vw_costo_promedio");
        mb.Entity<EvolucionCosto>().HasNoKey().ToView("vw_evolucion_costo");
        mb.Entity<CotizacionSaldo>().HasNoKey().ToView("vw_cotizaciones_saldo");

        // El desglose por lado. productos.stock sigue siendo el total —la flor
        // del mesón no dejó de ser tuya—; acá está partido en bodega y venta.
        mb.Entity<Existencia>().HasNoKey().ToView("vw_existencias");
        mb.Entity<Vendible>().HasNoKey().ToView("vw_vendibles");

        // ToView(null): no hay tabla ni vista detrás. Solo se consultan con
        // FromSql sobre las funciones de negocio.
        mb.Entity<ConsumoLote>().HasNoKey().ToView((string?)null);
        mb.Entity<RecepcionLote>().HasNoKey().ToView((string?)null);
        mb.Entity<ValidacionLote>().HasNoKey().ToView((string?)null);
        mb.Entity<ReingresoLote>().HasNoKey().ToView((string?)null);
        mb.Entity<ResultadoTraspaso>().HasNoKey().ToView((string?)null);
        mb.Entity<ResultadoConteo>().HasNoKey().ToView((string?)null);
    }

    /// <summary>
    /// Convierte PascalCase a snake_case para tablas, columnas e índices.
    /// Evita escribir HasColumnName en las 162 columnas del esquema.
    /// </summary>
    private static void AplicarNombresSnakeCase(ModelBuilder mb)
    {
        foreach (var entidad in mb.Model.GetEntityTypes())
        {
            foreach (var propiedad in entidad.GetProperties())
            {
                // Respeta el nombre si ya se fijó explícitamente
                if (propiedad.GetColumnName() == propiedad.Name)
                    propiedad.SetColumnName(ASnakeCase(propiedad.Name));
            }

            foreach (var clave in entidad.GetKeys())
                clave.SetName(ASnakeCase(clave.GetName()!));

            foreach (var fk in entidad.GetForeignKeys())
                fk.SetConstraintName(ASnakeCase(fk.GetConstraintName()!));

            foreach (var indice in entidad.GetIndexes())
                indice.SetDatabaseName(ASnakeCase(indice.GetDatabaseName()!));
        }
    }

    private static string ASnakeCase(string texto)
    {
        if (string.IsNullOrEmpty(texto)) return texto;

        var sb = new System.Text.StringBuilder(texto.Length + 8);
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && texto[i - 1] != '_') sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }
}