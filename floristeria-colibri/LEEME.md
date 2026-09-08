# floristeria-colibri — API de inventario

.NET 8 + Dapper + PostgreSQL. Toda la lógica de negocio vive en la base; la API
llama funciones y traduce errores.

## Estructura

```
sql/         los SP. Se ejecutan a mano, en orden.
DbAccess/    una vez y no se toca más
DAL/         acá vive TODO el SQL de C#. Sin excepción.
BLL/         validaciones de forma y armado de respuestas
Endpoints/   rutas, códigos HTTP, políticas
Models/      lo que sale de la base
Dto/         lo que entra y el sobre de salida
Utils/       ResponseDto helpers, ResultadoOp
Auth/        JWT, políticas, BCrypt
```

Un módulo = un `XxxDAL` + un `XxxBLL` + un `XxxEndpoints`, mismo sustantivo en
las tres capas. Hoy solo existe Inventario.

## Puesta en marcha

**1. Los SP.** En orden, contra tu base:

```bash
psql "$COLIBRI_URL" -f sql/01_inv_productos_consulta.sql
psql "$COLIBRI_URL" -f sql/02_inv_productos_escritura.sql
```

Antes lee `sql/00_LEEME.md`: hay que verificar los tipos de `productos` y las
etiquetas de `tipo_producto`. Si algo no calza, la función no se crea y te
enteras ahí mismo.

**2. Configuración.** `appsettings.Development.json` para local. En el VPS,
Dokploy inyecta por Environment y las claves anidadas van con **doble guión
bajo**:

```
ConnectionStrings__Colibri=Host=...;Database=colibri;...
Jwt__Key=...
Postgres__CommandTimeout=30
Cors__Origenes__0=https://tu-front.cl
```

**3. Correr.**

```bash
dotnet restore && dotnet run
```

El log de arranque tiene que decir `Base OK — N productos, enum tipo_producto = X`.
Si no arranca, el mensaje dice por qué: es a propósito, ver abajo.

## El chequeo de arranque

`Program.cs` abre la conexión y lee un `tipo_producto` antes de servir. Existe
porque **un `MapEnum` mal puesto no falla al arrancar, falla en el primer
request real**: la API queda verde y devuelve 500 en cada llamada. Con el
chequeo, el contenedor no levanta y el deploy anterior sigue sirviendo.

## Cómo viajan los errores

La función hace `RAISE EXCEPTION 'No se puede desactivar: es ingrediente de Ramo Primavera.'`
→ llega como `PostgresException` con `SqlState = P0001` → `ErroresPg` lo
convierte en string → el BLL lo devuelve → el endpoint responde 400 con ese
texto **sin reformular**.

Es el mismo contrato del `@dg_resultado` de SQL Server: `""` = salió bien.

Por eso los mensajes de los `RAISE` se escriben pensando en la persona que los
va a leer en pantalla, no en el desarrollador.

## Dónde va cada validación

| Tipo | Dónde |
|---|---|
| Id en cero, string vacío, precio negativo | BLL |
| Código duplicado, categoría inexistente, es ingrediente de un ramo, tiene ventas | SP |

El BLL solo ataja lo que ahorra un viaje. Duplicar las reglas de negocio en C#
garantiza que las dos versiones se desincronicen; cuando la base diga que no se
puede, ese mensaje es el bueno.

## Endpoints

| Método | Ruta | Política |
|---|---|---|
| GET | `/api/inventario/productos` | VerInventario |
| GET | `/api/inventario/productos/{id}` | VerInventario |
| GET | `/api/inventario/productos/codigo/{codigo}` | VerInventario |
| POST | `/api/inventario/productos` | Inventario |
| PUT | `/api/inventario/productos/{id}` | Inventario |
| PATCH | `/api/inventario/productos/{id}/activar` | Inventario |
| PATCH | `/api/inventario/productos/{id}/desactivar` | Inventario |
| DELETE | `/api/inventario/productos/{id}` | Admin |
| GET | `/api/inventario/categorias` | VerInventario |
| POST | `/api/inventario/categorias` | Inventario |

La grilla acepta `?busqueda=&categoriaId=&tipo=&activo=&bajoMinimo=&pagina=1&tamano=50`.
Todos opcionales.

## Qué falta (a propósito)

Lotes, traspasos, mermas, armado de ramos, movimientos y kardex. Cada uno se
suma con su `sql/0N_inv_*.sql` y sus métodos en las tres capas, sin tocar nada
de lo que ya está.

Los otros módulos (Ventas, Cotizaciones, Clientes, Abastecimiento, Acceso)
siguen el mismo patrón: copia `InventarioDAL/BLL/Endpoints` y cambia el
sustantivo.

## Antes de tocar producción

Rama nueva, y confirma en Dokploy qué rama dispara el auto-deploy. Y saca un
`pg_dump` antes de correr los `.sql`: crean funciones nuevas, no tocan datos,
pero un backup cuesta un minuto.
