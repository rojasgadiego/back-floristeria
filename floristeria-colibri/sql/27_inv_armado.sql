-- =====================================================================
-- Inventario: armar ramos
-- =====================================================================
-- La pantalla de Inventario tenía "Armar unidades", pero la API y la base
-- nunca lo implementaron: ningún ramo podía tener unidades listas, y con
-- eso la vitrina de ramos, su venta, el desarme y su conteo quedaban sin
-- uso real.
--
-- Armar descuenta de BODEGA los componentes de la receta y suma unidades
-- listas al ramo:
--   · Flores: balde por balde, lo que vence antes primero. Solo baldes
--     "de primera" (sin rebaja) y sin vencer.
--   · La flor recuperada (baldes rebajados) solo entra si se autoriza
--     balde por balde, y se usa antes que la de primera: tiene precio y
--     costo propios, y un ramo de matrimonio quizás no debería llevarla.
--   · Lo que no se maneja por lotes (cintas, papel…): de su stock.
--   · Si falta algo, no se arma nada.
--
-- El costo del ramo (costo_armado) queda como promedio ponderado entre lo
-- que ya había armado y el costo real de lo consumido ahora.
--
-- Idempotente.
-- =====================================================================

-- ═══ Qué hay para armar ═══

CREATE OR REPLACE FUNCTION sp_inv_c_disponibilidad_armado(p_producto_id integer)
RETURNS TABLE (
    componente_id integer, producto text, emoji text, por_unidad integer,
    controla_lotes boolean, de_primera integer, recuperada integer
)
LANGUAGE sql
STABLE
AS $$
    SELECT r.componente_id, p.nombre, p.emoji, r.cantidad, p.controla_lotes,
           CASE WHEN p.controla_lotes THEN
               COALESCE((SELECT sum(l.varas_disponibles) FROM lotes l
                          WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0
                            AND l.calidad IS NULL
                            AND COALESCE(l.fecha_vencimiento >= CURRENT_DATE, true)), 0)::int
           ELSE COALESCE(p.stock, 0) END,
           CASE WHEN p.controla_lotes THEN
               COALESCE((SELECT sum(l.varas_disponibles) FROM lotes l
                          WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0
                            AND l.calidad IS NOT NULL), 0)::int
           ELSE 0 END
      FROM recetas r
      JOIN productos p ON p.id = r.componente_id
     WHERE r.producto_id = p_producto_id
     ORDER BY p.nombre;
$$;

-- Los baldes de flor recuperada que se podrían autorizar.
CREATE OR REPLACE FUNCTION sp_inv_c_lotes_recuperados_armado(p_producto_id integer)
RETURNS TABLE (
    lote_id integer, producto_id integer, producto text, codigo text,
    varas_disponibles integer, dias_en_camara integer, costo_por_vara numeric,
    calidad calidad_reingreso, alerta text
)
LANGUAGE sql
STABLE
AS $$
    SELECT l.id, p.id, p.nombre, l.codigo, l.varas_disponibles,
           (CURRENT_DATE - l.fecha_ingreso)::int, l.costo_por_vara, l.calidad,
           CASE WHEN l.fecha_vencimiento < CURRENT_DATE THEN 'vencido'
                WHEN l.fecha_vencimiento <= CURRENT_DATE + 1 THEN 'por_vencer'
                ELSE 'normal' END
      FROM recetas r
      JOIN productos p ON p.id = r.componente_id
      JOIN lotes l ON l.producto_id = p.id
     WHERE r.producto_id = p_producto_id
       AND p.controla_lotes AND l.estado = 'activo' AND l.varas_disponibles > 0
       AND l.calidad IS NOT NULL
     ORDER BY l.fecha_vencimiento NULLS LAST, l.id;
$$;


-- ═══ Armar ═══

CREATE OR REPLACE FUNCTION sp_inv_i_armar(
    p_producto_id       integer,
    p_cantidad          integer,
    p_lotes_autorizados integer[] DEFAULT '{}',
    p_usuario_id        integer   DEFAULT NULL
)
RETURNS TABLE (costo_unitario integer, costo_total integer, listos integer)
LANGUAGE plpgsql
AS $$
DECLARE
    v_prod     record;
    r          record;
    l          record;
    v_falta    int;
    v_toma     int;
    v_costo    numeric := 0;
    v_costo_c  numeric;
    v_stock    int;
    v_listo    int;
BEGIN
    IF COALESCE(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'Indica cuántas unidades armar.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.tipo <> 'armado' THEN
        RAISE EXCEPTION '% no es un producto armado.', v_prod.nombre;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_producto_id) THEN
        RAISE EXCEPTION '% no tiene receta: no se sabe qué lleva.', v_prod.nombre;
    END IF;

    FOR r IN
        SELECT rc.componente_id, rc.cantidad * p_cantidad AS necesita, p.nombre, p.controla_lotes
          FROM recetas rc JOIN productos p ON p.id = rc.componente_id
         WHERE rc.producto_id = p_producto_id
         ORDER BY p.nombre
    LOOP
        PERFORM 1 FROM productos WHERE id = r.componente_id FOR UPDATE;
        v_falta   := r.necesita;
        v_costo_c := 0;

        IF r.controla_lotes THEN
            -- Primero la recuperada autorizada, después la de primera sin
            -- vencer; en cada grupo, lo que vence antes.
            FOR l IN
                SELECT lo.id, lo.varas_disponibles, lo.costo_por_vara
                  FROM lotes lo
                 WHERE lo.producto_id = r.componente_id
                   AND lo.estado = 'activo' AND lo.varas_disponibles > 0
                   AND (   (lo.calidad IS NOT NULL AND lo.id = ANY (COALESCE(p_lotes_autorizados, '{}')))
                        OR (lo.calidad IS NULL AND COALESCE(lo.fecha_vencimiento >= CURRENT_DATE, true)))
                 ORDER BY (lo.calidad IS NULL), lo.fecha_vencimiento NULLS LAST, lo.fecha_ingreso, lo.id
                 FOR UPDATE
            LOOP
                EXIT WHEN v_falta = 0;
                v_toma := LEAST(v_falta, l.varas_disponibles);

                UPDATE lotes
                   SET varas_disponibles = varas_disponibles - v_toma,
                       estado = CASE WHEN varas_disponibles - v_toma = 0
                                     THEN 'agotado'::estado_lote ELSE estado END,
                       actualizado_en = now()
                 WHERE id = l.id;

                v_costo_c := v_costo_c + v_toma * l.costo_por_vara;
                v_falta   := v_falta - v_toma;
            END LOOP;

            IF v_falta > 0 THEN
                RAISE EXCEPTION 'Faltan % de % en bodega para armar % unidad(es).',
                    v_falta, r.nombre, p_cantidad;
            END IF;
        ELSE
            IF (SELECT COALESCE(stock, 0) FROM productos WHERE id = r.componente_id) < r.necesita THEN
                RAISE EXCEPTION 'Faltan % de % en bodega para armar % unidad(es).',
                    r.necesita - (SELECT COALESCE(stock, 0) FROM productos WHERE id = r.componente_id),
                    r.nombre, p_cantidad;
            END IF;

            v_costo_c := r.necesita * fn_mer_costo_ref(r.componente_id);
        END IF;

        UPDATE productos
           SET stock = COALESCE(stock, 0) - r.necesita, actualizado_en = now()
         WHERE id = r.componente_id
        RETURNING stock INTO v_stock;

        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            r.componente_id, 'consumo', -r.necesita, v_stock,
            'Armado de ' || v_prod.nombre, p_cantidad || ' unidad(es)',
            p_usuario_id, 'armado', p_producto_id
        );

        v_costo := v_costo + v_costo_c;
    END LOOP;

    -- El costo del ramo: promedio ponderado con lo que ya estaba armado.
    UPDATE productos SET
        costo_armado = ROUND((COALESCE(stock_listo, 0) * COALESCE(costo_armado, 0) + v_costo)
                             / (COALESCE(stock_listo, 0) + p_cantidad)),
        stock_listo  = COALESCE(stock_listo, 0) + p_cantidad,
        actualizado_en = now()
    WHERE id = p_producto_id
    RETURNING productos.stock_listo INTO v_listo;

    INSERT INTO movimientos_inventario (
        producto_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, 'armado', p_cantidad, v_listo,
        'Armado', 'Costo ' || ROUND(v_costo / p_cantidad) || ' c/u',
        p_usuario_id, 'armado', p_producto_id
    );

    RETURN QUERY SELECT ROUND(v_costo / p_cantidad)::int, ROUND(v_costo)::int, v_listo;
END;
$$;
