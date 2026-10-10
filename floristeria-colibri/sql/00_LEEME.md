# Convención de nombres

```
sp_{modulo}_{accion}_{objeto}
```

| Parte | Valores |
|---|---|
| módulo | `inv` inventario · `ven` ventas · `cot` cotizaciones · `cli` clientes · `abs` abastecimiento · `cat` catálogo · `acc` acceso |
| acción | `c` consulta · `i` inserta · `u` actualiza · `d` elimina |
| objeto | plural si devuelve lista, singular si devuelve uno |

Ejemplos: `sp_inv_c_productos`, `sp_inv_c_producto`, `sp_inv_i_producto`,
`sp_inv_u_producto_estado`, `sp_inv_d_producto`.

## Son FUNCTIONS, no PROCEDURES

En Postgres un `PROCEDURE` se invoca con `CALL` y no puede devolver filas. Todo
lo que necesite retornar algo —y eso es casi todo— tiene que ser `FUNCTION` y se
llama con `SELECT`:

```sql
SELECT * FROM sp_inv_c_productos(NULL, NULL, NULL, true, false, 1, 50);
```

El prefijo `sp_` es solo convención de nombre, para que se lean como los del
otro repo. No cambia lo que son.

**Por eso el DAL nunca usa `CommandType.StoredProcedure`.** Npgsql lo traduce a
`CALL` y falla contra una función con un error que no dice por qué.

## Errores de negocio

Cada función valida y lanza `RAISE EXCEPTION` con el mensaje ya redactado para el
usuario final. Eso llega a C# como `PostgresException` con `SqlState = 'P0001'`,
y `ErroresPg` lo convierte en el string que el DAL devuelve.

Es el reemplazo exacto del `@dg_resultado` de SQL Server: `""` = salió bien,
texto = error de negocio que se muestra tal cual.

## Antes de correr nada: verifica los tipos

Escribí estas funciones asumiendo los tipos de `productos`. Corre esto y ajusta
los `RETURNS TABLE` si algo no calza —un tipo mal declarado falla al crear la
función, así que te vas a enterar al toque:

```sql
SELECT column_name, data_type, udt_name, is_nullable
FROM information_schema.columns
WHERE table_name = 'productos'
ORDER BY ordinal_position;

-- Y las etiquetas del enum:
SELECT enumlabel FROM pg_enum e
JOIN pg_type t ON t.oid = e.enumtypid
WHERE t.typname = 'tipo_producto' ORDER BY enumsortorder;
```

## Orden de ejecución

1. `01_inv_productos_consulta.sql`
2. `02_inv_productos_escritura.sql`

Desde ahí, en orden de número. **En producción están aplicados todos, hasta
el `20`** (al 2026-10-10).

## La fuente de verdad es `esquema_produccion.sql`

La base nació de un dump y muchas funciones nunca tuvieron script acá
(escaneo, desarme, reportes de mermas, ventas…). Además, varios scripts
quedaron atrás porque uno posterior reemplazó sus funciones:

| Script | Quedó atrás en | Lo reemplaza |
|---|---|---|
| `02` | `sp_inv_i_producto`, `sp_inv_u_producto` (sin precio de ramo) | `12` |
| `08` | `sp_mer_i_merma`, `sp_mer_u_revertir` | `15`, `16`, `18`, `19`, `20` |
| `09` | parcha `sp_mer_i_merma` y `sp_mer_i_desarme` en caliente | `15`, `16`, `18` |
| `11` | `sp_ven_i_venta` de 12 argumentos | `13` |

**No vuelvas a correr `02`, `08` ni `09` sobre una base existente**: crearían
sobrecargas viejas o pisarían las versiones nuevas.

`esquema_produccion.sql` es un `pg_dump --schema-only` de producción: tablas,
funciones y triggers tal como están, sin datos. Para regenerarlo:

```bash
ssh root@187.127.55.127 'docker exec $(docker ps -qf name=colibriapp-database) \
  pg_dump -U app_user -d app_colibridb --schema-only --no-owner --no-privileges' \
  > esquema_produccion.sql
```

## Para cambiar una función que ya existe

1. Saca la versión **vigente** de producción, no la del repo:
   `SELECT pg_get_functiondef('sp_x'::regproc);`
2. El script nuevo parte de esa copia y cambia solo lo necesario.
3. Pruébalo en seco: `BEGIN; \i script.sql; …pruebas…; ROLLBACK;`
4. Si cambia la firma, borra la versión vieja (`DROP FUNCTION … (tipos)`) y
   deja los parámetros nuevos con default: así el backend que está corriendo
   sigue funcionando hasta que se redespliegue. Primero el SQL, después el push.
5. Regenera `esquema_produccion.sql`.
