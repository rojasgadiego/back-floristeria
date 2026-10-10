-- =====================================================================
-- Inventario: productos con precio por definir ($0)
-- =====================================================================
-- Al cargar el inventario real hay productos contados (bases, cajas,
-- bolsas) que todavía no tienen precio. Exigir uno inventado para poder
-- registrarlos es peor que dejarlos en 0: un precio falso se termina
-- cobrando.
--
-- Por eso $0 significa "por definir", y la regla es:
--   · el producto existe y puede tener stock en bodega;
--   · NO baja al mesón ni se vende hasta que tenga precio.
--
-- La traba va en la base y no solo en la pantalla: el mesón congela el
-- precio de cada partida al bajarla, y una partida en $0 terminaría
-- cobrándose en $0 aunque después se le ponga precio a la ficha.
--
-- Trae también sp_inv_i/u_producto: NO volver a correr 02 después, que
-- tiene versiones viejas (sin precio de ramo) y dejaría sobrecargas.
--
-- Idempotente.
-- =====================================================================

-- ═══ La ficha acepta 0 ═══

ALTER TABLE productos DROP CONSTRAINT IF EXISTS productos_precio_positivo;
ALTER TABLE productos DROP CONSTRAINT IF EXISTS productos_precio_no_negativo;
ALTER TABLE productos ADD CONSTRAINT productos_precio_no_negativo CHECK (precio >= 0);

-- ═══ El lote no copia un precio por definir ═══

-- Al recibir una compra el lote guarda el precio del producto. Si en ese
-- momento es 0, se deja NULL: así toma el precio de la ficha al bajar al
-- mesón (COALESCE en sp_ven_i_traspaso) en vez de quedar en 0 para
-- siempre. Además lotes_precio_positivo rechazaría el 0 con un error que
-- nadie entendería.
CREATE OR REPLACE FUNCTION fn_lot_precio_por_definir()
RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    NEW.precio_unitario := NULLIF(NEW.precio_unitario, 0);
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tg_lot_precio_por_definir ON lotes;
CREATE TRIGGER tg_lot_precio_por_definir
    BEFORE INSERT OR UPDATE OF precio_unitario ON lotes
    FOR EACH ROW EXECUTE FUNCTION fn_lot_precio_por_definir();

-- ═══ Sin precio no baja al mesón ═══

CREATE OR REPLACE FUNCTION fn_ven_partida_con_precio()
RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF coalesce(NEW.precio_unitario, 0) <= 0 THEN
        RAISE EXCEPTION '% no tiene precio. Defínelo antes de bajarlo al mesón.',
            (SELECT nombre FROM productos WHERE id = NEW.producto_id);
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tg_ven_partida_con_precio ON partidas_mostrador;
CREATE TRIGGER tg_ven_partida_con_precio
    BEFORE INSERT ON partidas_mostrador
    FOR EACH ROW EXECUTE FUNCTION fn_ven_partida_con_precio();

-- ═══ Ni se vende ═══

-- Última barrera: vendiendo por producto (sin escanear) el precio sale de
-- la ficha, que pudo pasar a 0 con la partida ya en el mesón. Los
-- servicios de una cotización quedan fuera: ahí el precio lo pone quien
-- cotiza.
CREATE OR REPLACE FUNCTION fn_ven_item_con_precio()
RETURNS trigger
LANGUAGE plpgsql AS $$
BEGIN
    IF NOT NEW.es_servicio AND NEW.precio_unitario <= 0 THEN
        RAISE EXCEPTION '% no tiene precio. Defínelo en inventario antes de venderlo.', NEW.nombre;
    END IF;
    RETURN NEW;
END;
$$;

DROP TRIGGER IF EXISTS tg_ven_item_con_precio ON venta_items;
CREATE TRIGGER tg_ven_item_con_precio
    BEFORE INSERT ON venta_items
    FOR EACH ROW EXECUTE FUNCTION fn_ven_item_con_precio();

-- ═══ Alta y edición aceptan 0 ═══

-- Copia de las versiones vigentes en producción (las con precio de ramo y
-- de liquidación; 02 quedó atrás). Solo cambia la validación del precio.
CREATE OR REPLACE FUNCTION public.sp_inv_i_producto(p_codigo text, p_nombre text, p_categoria_id integer, p_tipo tipo_producto, p_precio numeric, p_precio_ramo numeric DEFAULT NULL::numeric, p_precio_liquidacion numeric DEFAULT NULL::numeric, p_emoji text DEFAULT NULL::text, p_minimo integer DEFAULT 0, p_costo numeric DEFAULT 0, p_stock integer DEFAULT 0, p_controla_lotes boolean DEFAULT false, p_dias_vida integer DEFAULT NULL::integer)
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_id     int;
    v_lotes  boolean := coalesce(p_controla_lotes, false);
BEGIN
    IF btrim(coalesce(p_codigo, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el código del producto.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio < 0 THEN
        RAISE EXCEPTION 'El precio no puede ser negativo.';
    END IF;

    IF p_precio_ramo IS NOT NULL AND p_precio_ramo > p_precio THEN
        RAISE EXCEPTION 'El precio en ramo no puede superar al precio suelto.';
    END IF;

    IF p_precio_liquidacion IS NOT NULL AND p_precio_liquidacion > p_precio THEN
        RAISE EXCEPTION 'El precio de liquidación no puede superar al precio normal.';
    END IF;

    IF EXISTS (SELECT 1 FROM productos WHERE lower(codigo) = lower(btrim(p_codigo))) THEN
        RAISE EXCEPTION 'Ya existe un producto con el código %.', btrim(p_codigo);
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    -- Reglas de forma. Los CHECK las rechazarían igual, pero con un 23514
    -- que el usuario no puede interpretar.
    IF p_tipo = 'armado' THEN
        IF coalesce(p_costo, 0) > 0 THEN
            RAISE EXCEPTION 'Un producto armado no lleva costo propio: se calcula desde su receta.';
        END IF;
        IF coalesce(p_stock, 0) > 0 THEN
            RAISE EXCEPTION 'Un producto armado no lleva stock inicial: las unidades entran armándolas.';
        END IF;
        IF v_lotes THEN
            RAISE EXCEPTION 'Solo los productos simples controlan lotes.';
        END IF;
    END IF;

    INSERT INTO productos (
        codigo, nombre, categoria_id, tipo, emoji,
        precio, precio_ramo, precio_liquidacion, minimo,
        costo, stock, costo_armado, stock_listo,
        controla_lotes, dias_vida, activo
    )
    VALUES (
        btrim(p_codigo), btrim(p_nombre), p_categoria_id, p_tipo,
        coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), '🌿'),
        p_precio, p_precio_ramo, p_precio_liquidacion, coalesce(p_minimo, 0),

        -- Cada tipo llena su par. El otro va NULL explícito, no cero:
        -- los CHECK productos_forma_* exigen NULL.
        CASE WHEN p_tipo = 'simple' THEN coalesce(p_costo, 0) END,
        -- El stock inicial se ignora si controla lotes: esas existencias
        -- entran recibiendo una compra, que es lo que les da procedencia
        -- y vencimiento.
        CASE WHEN p_tipo = 'simple'
             THEN CASE WHEN v_lotes THEN 0 ELSE coalesce(p_stock, 0) END END,
        CASE WHEN p_tipo = 'armado' THEN 0 END,
        CASE WHEN p_tipo = 'armado' THEN 0 END,

        v_lotes, p_dias_vida, true
    )
    RETURNING id INTO v_id;

    -- Alta con stock: deja la huella igual que cualquier otro ingreso, para
    -- que el libro empiece cuadrado desde la primera fila.
    IF p_tipo = 'simple' AND NOT v_lotes AND coalesce(p_stock, 0) > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante, motivo, referencia_tipo
        )
        VALUES (v_id, 'alta', p_stock, p_stock, 'Stock inicial', 'producto');
    END IF;

    RETURN v_id;
END;
$function$
;

CREATE OR REPLACE FUNCTION public.sp_inv_u_producto(p_id integer, p_nombre text, p_categoria_id integer, p_precio numeric, p_precio_ramo numeric DEFAULT NULL::numeric, p_precio_liquidacion numeric DEFAULT NULL::numeric, p_emoji text DEFAULT NULL::text, p_minimo integer DEFAULT NULL::integer, p_costo numeric DEFAULT NULL::numeric, p_dias_vida integer DEFAULT NULL::integer)
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_tipo tipo_producto;
BEGIN
    SELECT tipo INTO v_tipo FROM productos WHERE id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio < 0 THEN
        RAISE EXCEPTION 'El precio no puede ser negativo.';
    END IF;

    IF p_precio_ramo IS NOT NULL AND p_precio_ramo > p_precio THEN
        RAISE EXCEPTION 'El precio en ramo no puede superar al precio suelto.';
    END IF;

    IF p_precio_liquidacion IS NOT NULL AND p_precio_liquidacion > p_precio THEN
        RAISE EXCEPTION 'El precio de liquidación no puede superar al precio normal.';
    END IF;

    IF v_tipo = 'armado' AND coalesce(p_costo, 0) > 0 THEN
        RAISE EXCEPTION 'Un producto armado no lleva costo propio: se calcula desde su receta.';
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    UPDATE productos SET
        nombre             = btrim(p_nombre),
        categoria_id       = p_categoria_id,
        precio             = p_precio,
        -- Los precios secundarios SÍ se sobrescriben con NULL: dejar de
        -- tener precio de ramo es una decisión válida, y con coalesce sería
        -- imposible borrarlo una vez puesto.
        precio_ramo        = p_precio_ramo,
        precio_liquidacion = p_precio_liquidacion,
        emoji              = coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), emoji),
        minimo             = coalesce(p_minimo, minimo),
        -- En un armado el costo se queda NULL: lo pone la receta.
        costo              = CASE WHEN tipo = 'simple'
                                  THEN coalesce(p_costo, costo) END,
        dias_vida          = coalesce(p_dias_vida, dias_vida),
        actualizado_en     = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$function$
;

