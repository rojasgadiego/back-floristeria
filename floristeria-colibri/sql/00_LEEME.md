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
