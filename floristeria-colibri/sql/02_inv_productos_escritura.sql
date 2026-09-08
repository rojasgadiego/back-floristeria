-- ═══════════════════════════════════════════════════════════════
-- INVENTARIO · Escritura de productos
--
-- Todas devuelven el id afectado. El detalle completo lo trae después
-- sp_inv_c_producto: así la forma de salida se define en UN solo lugar
-- y no hay cinco RETURNS TABLE que mantener sincronizados.
--
-- Los RAISE EXCEPTION llevan el mensaje ya redactado para el usuario:
-- ese texto viaja tal cual hasta la pantalla.
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- sp_inv_i_producto
--
-- El stock inicial se ignora a propósito cuando el producto controla
-- lotes: las existencias de una flor entran recibiendo una compra, que
-- es lo que les da procedencia y vencimiento. Sumarlas acá dejaría
-- stock sin lote y el inventario deja de cuadrar.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_i_producto(
    p_codigo          text,
    p_nombre          text,
    p_categoria_id    int,
    p_tipo            tipo_producto,
    p_precio          numeric,
    p_emoji           text    DEFAULT NULL,
    p_minimo          int     DEFAULT 0,
    p_costo           numeric DEFAULT 0,
    p_stock           int     DEFAULT 0,
    p_controla_lotes  boolean DEFAULT false,
    p_dias_vida       int     DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    v_id           int;
    v_stock_real   int;
BEGIN
    IF btrim(coalesce(p_codigo, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el código del producto.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio <= 0 THEN
        RAISE EXCEPTION 'El precio debe ser mayor a 0.';
    END IF;

    IF EXISTS (SELECT 1 FROM productos WHERE lower(codigo) = lower(btrim(p_codigo))) THEN
        RAISE EXCEPTION 'Ya existe un producto con el código %.', btrim(p_codigo);
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    -- Ver comentario de cabecera.
    v_stock_real := CASE WHEN p_controla_lotes THEN 0 ELSE coalesce(p_stock, 0) END;

    INSERT INTO productos (
        codigo, nombre, categoria_id, tipo, emoji, precio,
        minimo, costo, stock, controla_lotes, dias_vida, activo
    )
    VALUES (
        btrim(p_codigo), btrim(p_nombre), p_categoria_id, p_tipo,
        coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), '🌿'),
        p_precio, coalesce(p_minimo, 0), coalesce(p_costo, 0),
        v_stock_real, coalesce(p_controla_lotes, false), p_dias_vida, true
    )
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_u_producto
--
-- El tipo NO se toca: cambiar un simple a armado dejaría un producto
-- sin receta y con costo que ya no significa nada. El stock tampoco:
-- se mueve con compras, ventas, mermas o ajustes, nunca editando la
-- ficha.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_u_producto(
    p_id            int,
    p_nombre        text,
    p_categoria_id  int,
    p_precio        numeric,
    p_emoji         text    DEFAULT NULL,
    p_minimo        int     DEFAULT NULL,
    p_costo         numeric DEFAULT NULL,
    p_dias_vida     int     DEFAULT NULL
)
RETURNS int
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio <= 0 THEN
        RAISE EXCEPTION 'El precio debe ser mayor a 0.';
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    UPDATE productos SET
        nombre         = btrim(p_nombre),
        categoria_id   = p_categoria_id,
        precio         = p_precio,
        emoji          = coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), emoji),
        minimo         = coalesce(p_minimo, minimo),
        costo          = coalesce(p_costo, costo),
        dias_vida      = coalesce(p_dias_vida, dias_vida),
        actualizado_en = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_u_producto_estado — activar / desactivar
--
-- Desactivar lo saca del punto de venta conservando su historial. Se
-- rechaza si es ingrediente de un ramo activo: el ramo quedaría sin
-- poder armarse y nadie sabría por qué.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_u_producto_estado(
    p_id      int,
    p_activo  boolean
)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    v_ramos text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF NOT p_activo THEN
        SELECT string_agg(pr.nombre, ', ' ORDER BY pr.nombre)
          INTO v_ramos
          FROM recetas r
          JOIN productos pr ON pr.id = r.producto_id
         WHERE r.componente_id = p_id
           AND pr.activo;

        IF v_ramos IS NOT NULL THEN
            RAISE EXCEPTION 'No se puede desactivar: es ingrediente de %.', v_ramos;
        END IF;
    END IF;

    UPDATE productos
       SET activo = p_activo, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_d_producto — borrado definitivo
--
-- Solo para lo creado por error. Si tiene cualquier rastro —lotes,
-- movimientos, ventas o recetas— se rechaza y hay que desactivarlo.
-- El mensaje dice cuál es el impedimento: "no se puede" a secas obliga
-- a adivinar.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_d_producto(p_id int)
RETURNS int
LANGUAGE plpgsql
AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF EXISTS (SELECT 1 FROM lotes WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene lotes registrados. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM movimientos_inventario WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene movimientos de inventario. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM venta_items WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene ventas registradas. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_id OR componente_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto participa en una receta.';
    END IF;

    DELETE FROM productos WHERE id = p_id;
    RETURN p_id;
END;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_i_categoria
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_i_categoria(p_nombre text)
RETURNS int
LANGUAGE plpgsql
AS $$
DECLARE
    v_id int;
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre de la categoría.';
    END IF;

    IF EXISTS (SELECT 1 FROM categorias WHERE lower(nombre) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ya existe la categoría %.', btrim(p_nombre);
    END IF;

    INSERT INTO categorias (nombre) VALUES (btrim(p_nombre))
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;
