# Lo que tienes que verificar antes de que esto corra

Cuatro cosas que asumí y no puedo saber desde acá. Ninguna falla al compilar.

## 1. Los tipos de `productos`

Escribí los `RETURNS TABLE` asumiendo: `codigo`/`nombre`/`emoji` = `text`,
`precio`/`costo`/`costo_armado` = `numeric`, `minimo`/`stock`/`stock_listo`/
`dias_vida` = `int`, `controla_lotes`/`activo` = `boolean`, fechas =
`timestamptz`.

```sql
SELECT column_name, data_type, udt_name, is_nullable
FROM information_schema.columns
WHERE table_name = 'productos' ORDER BY ordinal_position;
```

Si `codigo` es `varchar(n)` y no `text`, Postgres se queja al crear la función.
Ajusta el `RETURNS TABLE` y listo. **Esto sí falla ruidosamente**, así que es el
problema menos peligroso de los cuatro.

## 2. Las etiquetas de `tipo_producto`

Puse `simple` y `armado` en `Models/Enums/TipoProducto.cs`.

```sql
SELECT enumlabel FROM pg_enum e
JOIN pg_type t ON t.oid = e.enumtypid
WHERE t.typname = 'tipo_producto' ORDER BY enumsortorder;
```

Si no calzan, el chequeo de arranque de `Program.cs` lo caza. Sin ese chequeo,
esto se descubre en el primer request.

## 3. El claim del rol en tu JWT

`Auth/JwtConfig.cs` usa `RequireRole("admin", "bodega")`, que busca el claim
`role` (o `ClaimTypes.Role`). Si tu login emite el rol con otro nombre,
**RequireRole no encuentra nada y todo responde 403 sin explicar por qué**.

Revisa tu generador de tokens y ajusta las etiquetas de rol a las de tu enum
`rol_usuario`.

## 4. `stock_listo` en el INSERT

`sp_inv_i_producto` no lo escribe: asumí que es 0 por defecto y que solo sube
armando ramos. Si tu tabla no tiene default en esa columna y es `NOT NULL`, el
insert falla. Agrégalo al `INSERT` o ponle default en la tabla.

---

## Y una decisión que quizá quieras cambiar

Las rutas quedaron en `/api/inventario/productos`, no en `/api/productos` como
tu `ProductosController`. Si el front ya consume las viejas, cambia el
`MapGroup` a `/api` y ajusta las rutas de cada endpoint — es un solo archivo.
