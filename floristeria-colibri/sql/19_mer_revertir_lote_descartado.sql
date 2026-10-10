-- =====================================================================
-- Mermas: no revertir hacia un lote dado de baja
-- =====================================================================
-- Revertir devolvía las varas a su lote de origen aunque ese lote ya se
-- hubiera descartado: quedaban en un lote muerto y el stock del producto
-- subía sin que nadie pudiera venderlas. Ahora se rechaza con el porqué.
--
-- Las partidas no tienen estado de cierre: devolverles varas solo las
-- reabre, y si ya vencieron el POS igual no las vende.
--
-- Copia la versión vigente en producción (con lo de sql/18).
--
-- Idempotente.
-- =====================================================================

CREATE OR REPLACE FUNCTION public.sp_mer_u_revertir(p_id integer, p_motivo text, p_usuario_id integer)
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_m      record;
    v_rec    record;
    v_prod   record;
    v_x      record;
    v_stock  int;
    v_motivo text := btrim(coalesce(p_motivo, ''));
BEGIN
    IF v_motivo = '' THEN
        RAISE EXCEPTION 'Explica por qué se revierte.';
    END IF;

    SELECT * INTO v_m FROM mermas WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La merma no existe.';
    END IF;

    IF v_m.revertida THEN
        RAISE EXCEPTION 'Esta merma ya fue revertida.';
    END IF;

    -- ═════════════════════════════════════════════════════════════
    -- Un desarme se revierte ENTERO: las mermas de sus componentes no
    -- existen por separado. Se anulan los lotes recuperados, sale del stock
    -- de cada componente lo que había vuelto, y el armado regresa listo.
    -- ═════════════════════════════════════════════════════════════
    IF v_m.desarme_grupo IS NOT NULL THEN
        FOR v_x IN
            SELECT me.*, l.codigo AS rec_codigo,
                   l.varas_iniciales AS rec_ini, l.varas_disponibles AS rec_disp
              FROM mermas me
              LEFT JOIN lotes l ON l.id = me.lote_recuperacion_id
             WHERE me.desarme_grupo = v_m.desarme_grupo
             ORDER BY me.id
               FOR UPDATE OF me
        LOOP
            IF v_x.revertida THEN
                CONTINUE;
            END IF;

            IF v_x.lote_recuperacion_id IS NOT NULL THEN
                IF v_x.rec_disp < v_x.rec_ini THEN
                    RAISE EXCEPTION
                        'No se puede revertir el desarme: ya se vendieron % varas del lote recuperado %.',
                        v_x.rec_ini - v_x.rec_disp, v_x.rec_codigo;
                END IF;

                UPDATE lotes
                   SET estado = 'descartado',
                       varas_disponibles = 0,
                       notas = COALESCE(notas || ' · ', '') || 'Anulado al revertir el desarme de la merma ' || p_id,
                       actualizado_en = now()
                 WHERE id = v_x.lote_recuperacion_id;
            END IF;

            -- Lo que había vuelto a bodega sale otra vez: esas varas regresan
            -- al armado. Lo perdido no se toca, nunca había entrado.
            IF v_x.cantidad_recuperada > 0 THEN
                UPDATE productos
                   SET stock = COALESCE(stock, 0) - v_x.cantidad_recuperada,
                       actualizado_en = now()
                 WHERE id = v_x.producto_id
                RETURNING stock INTO v_stock;

                INSERT INTO movimientos_inventario (
                    producto_id, tipo, cantidad, stock_resultante,
                    motivo, detalle, usuario_id, referencia_tipo, referencia_id
                )
                VALUES (
                    v_x.producto_id, 'ajuste', -v_x.cantidad_recuperada, v_stock,
                    'Reversa de desarme', v_motivo,
                    p_usuario_id, 'merma_reversa', v_x.id
                );
            END IF;

            UPDATE mermas SET
                revertida        = true,
                revertida_por    = p_usuario_id,
                revertida_en     = now(),
                motivo_reversion = v_motivo
            WHERE id = v_x.id;
        END LOOP;

        UPDATE productos
           SET stock_listo = COALESCE(stock_listo, 0) + v_m.desarme_cantidad,
               actualizado_en = now()
         WHERE id = v_m.desarme_producto_id
        RETURNING stock_listo INTO v_stock;

        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            v_m.desarme_producto_id, 'ajuste', v_m.desarme_cantidad, v_stock,
            'Reversa de desarme', v_motivo,
            p_usuario_id, 'merma_reversa', p_id
        );

        RETURN p_id;
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = v_m.producto_id FOR UPDATE;

    -- Un lote dado de baja ya no existe para el inventario: devolverle varas
    -- las dejaría en un lote muerto mientras el stock del producto sube, y
    -- nadie podría venderlas ni mermarlas. Si de verdad quedaron flores, se
    -- ingresan como entrada nueva.
    IF v_m.lote_id IS NOT NULL
       AND EXISTS (SELECT 1 FROM lotes WHERE id = v_m.lote_id AND estado = 'descartado') THEN
        RAISE EXCEPTION
            'No se puede revertir: el lote % ya fue dado de baja. Si quedaron flores, ingrésalas como entrada.',
            (SELECT codigo FROM lotes WHERE id = v_m.lote_id);
    END IF;

    -- ═══ El lote de recuperación, si lo hubo ═══
    IF v_m.lote_recuperacion_id IS NOT NULL THEN
        SELECT * INTO v_rec FROM lotes WHERE id = v_m.lote_recuperacion_id FOR UPDATE;

        IF v_rec.varas_disponibles < v_rec.varas_iniciales THEN
            RAISE EXCEPTION
                'No se puede revertir: ya se vendieron % varas del lote recuperado %.',
                v_rec.varas_iniciales - v_rec.varas_disponibles, v_rec.codigo;
        END IF;

        -- Se descarta en vez de borrarse: el código pudo imprimirse en una
        -- etiqueta y quedar pegado a un balde. Un código que desaparece y
        -- después se reutiliza es peor que uno muerto.
        UPDATE lotes
           SET estado = 'descartado',
               varas_disponibles = 0,
               notas = COALESCE(notas || ' · ', '') || 'Anulado al revertir la merma ' || p_id,
               actualizado_en = now()
         WHERE id = v_m.lote_recuperacion_id;
    END IF;

    -- ═══ Devolver al origen ═══
    IF v_m.partida_id IS NOT NULL THEN
        -- Del mostrador: las varas vuelven a la partida.
        UPDATE partidas_mostrador
           SET cantidad_disponible = cantidad_disponible + v_m.cantidad,
               agotada_en = NULL
         WHERE id = v_m.partida_id;

    ELSIF v_m.lote_id IS NOT NULL THEN
        UPDATE lotes
           SET varas_disponibles = varas_disponibles + v_m.cantidad,
               estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
               actualizado_en = now()
         WHERE id = v_m.lote_id;
    END IF;

    -- ═══ El stock del producto, el espejo exacto del registro ═══
    IF v_prod.tipo = 'armado' THEN
        UPDATE productos
           SET stock_listo = COALESCE(stock_listo, 0) + v_m.cantidad,
               actualizado_en = now()
         WHERE id = v_m.producto_id
        RETURNING stock_listo INTO v_stock;

    ELSIF v_m.partida_id IS NOT NULL THEN
        -- Una merma del mostrador no descontó el stock de bodega: solo le
        -- sumó lo recuperado. Revertirla es sacar eso, nada más.
        IF v_m.cantidad_recuperada > 0 THEN
            UPDATE productos
               SET stock = COALESCE(stock, 0) - v_m.cantidad_recuperada,
                   actualizado_en = now()
             WHERE id = v_m.producto_id
            RETURNING stock INTO v_stock;
        END IF;

    ELSE
        -- Entra lo mermado, sale lo que se había recuperado.
        UPDATE productos
           SET stock = COALESCE(stock, 0) + v_m.cantidad - v_m.cantidad_recuperada,
               actualizado_en = now()
         WHERE id = v_m.producto_id
        RETURNING stock INTO v_stock;
    END IF;

    UPDATE mermas SET
        revertida        = true,
        revertida_por    = p_usuario_id,
        revertida_en     = now(),
        motivo_reversion = v_motivo
    WHERE id = p_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        v_m.producto_id, v_m.lote_id, 'ajuste', v_m.cantidad,
        v_stock + v_m.cantidad_recuperada,
        'Reversa de merma', v_motivo,
        p_usuario_id, 'merma_reversa', p_id
    );

    -- El lote recuperado se anuló: esas varas salen. Es el espejo de la
    -- entrada que dejó el registro.
    IF v_m.cantidad_recuperada > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, lote_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            v_m.producto_id, v_m.lote_recuperacion_id, 'ajuste', -v_m.cantidad_recuperada, v_stock,
            'Reversa de merma', 'Anula lo recuperado · ' || v_motivo,
            p_usuario_id, 'merma_reversa', p_id
        );
    END IF;

    RETURN p_id;
END;
$function$
;
