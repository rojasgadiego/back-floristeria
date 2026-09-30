-- =====================================================================
-- Inventario: foto del producto
-- =====================================================================
-- Hasta ahora un producto se reconocía por su emoji. La foto se suma sin
-- reemplazarlo: donde no hay foto se sigue viendo el emoji.
--
-- Va en una tabla aparte y no como columna de productos para no tocar
-- sp_inv_c_productos ni el resto de las funciones que ya leen la ficha: el
-- front pide el mapa producto → versión una vez y arma la URL.
--
-- El archivo vive en el disco del servidor (volumen de Docker); acá solo
-- queda su nombre. `version` cambia en cada reemplazo y va en la URL, así
-- la foto se puede cachear para siempre y aun así se ve la nueva al
-- cambiarla.
--
-- Idempotente.
-- =====================================================================

CREATE TABLE IF NOT EXISTS producto_imagenes (
    producto_id    integer PRIMARY KEY REFERENCES productos (id) ON DELETE CASCADE,
    archivo        text NOT NULL,
    version        bigint NOT NULL,
    actualizado_en timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT producto_imagenes_archivo_no_vacio CHECK (length(btrim(archivo)) > 0)
);

-- ═══ Escritura ═══

-- Guarda o reemplaza la foto. Devuelve el archivo anterior (para borrarlo
-- del disco) o NULL si no había.
CREATE OR REPLACE FUNCTION sp_inv_u_producto_imagen(p_producto_id integer, p_archivo text, p_version bigint)
RETURNS text
LANGUAGE plpgsql AS $$
DECLARE
    v_anterior text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_producto_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    SELECT archivo INTO v_anterior FROM producto_imagenes WHERE producto_id = p_producto_id;

    INSERT INTO producto_imagenes (producto_id, archivo, version, actualizado_en)
    VALUES (p_producto_id, p_archivo, p_version, now())
    ON CONFLICT (producto_id) DO UPDATE
        SET archivo = EXCLUDED.archivo,
            version = EXCLUDED.version,
            actualizado_en = now();

    RETURN v_anterior;
END;
$$;

-- Quita la foto. Devuelve el archivo que hay que borrar, o NULL.
CREATE OR REPLACE FUNCTION sp_inv_d_producto_imagen(p_producto_id integer)
RETURNS text
LANGUAGE sql AS $$
    DELETE FROM producto_imagenes WHERE producto_id = p_producto_id RETURNING archivo;
$$;

-- ═══ Consulta ═══

-- El mapa que usa el front: qué productos tienen foto y en qué versión.
CREATE OR REPLACE FUNCTION sp_inv_c_producto_imagenes()
RETURNS TABLE (producto_id integer, version bigint)
LANGUAGE sql STABLE AS $$
    SELECT producto_id, version FROM producto_imagenes;
$$;

-- El archivo a servir. Sin filas si el producto no tiene foto.
CREATE OR REPLACE FUNCTION sp_inv_c_producto_imagen(p_producto_id integer)
RETURNS TABLE (archivo text, version bigint)
LANGUAGE sql STABLE AS $$
    SELECT archivo, version FROM producto_imagenes WHERE producto_id = p_producto_id;
$$;

-- El catálogo público de la landing: solo lo activo y con foto. Subir la
-- foto es lo que decide qué se muestra a los clientes.
CREATE OR REPLACE FUNCTION sp_pub_c_catalogo()
RETURNS TABLE (id integer, nombre text, emoji text, categoria text, precio integer, version bigint)
LANGUAGE sql STABLE AS $$
    SELECT p.id, p.nombre, p.emoji, c.nombre, p.precio, i.version
    FROM productos p
    JOIN producto_imagenes i ON i.producto_id = p.id
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE p.activo
    ORDER BY c.nombre NULLS LAST, p.nombre;
$$;
