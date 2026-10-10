-- =====================================================================
-- Compras: recibir productos sin lotes sin crear lotes fantasma
-- =====================================================================
-- La recepción creaba un lote por línea, también para productos que no
-- se manejan por lotes (bolsas, cintas, cajas). Ese lote nunca se
-- descontaba —esos productos se venden del stock— y el inventario lo
-- contaba dos veces. Es el mismo problema que sql/16 cerró en mermas.
--
-- Ahora, sin lotes, la línea solo suma al stock y deja su movimiento.
-- Además el costo del producto se actualiza con el promedio ponderado de
-- lo que había y lo que llega: es la única fuente de costo de esos
-- productos (se cargaron en $0) y la merma se valoriza con él.
--
-- Copia la versión vigente en producción.
--
-- Idempotente.
-- =====================================================================

CREATE OR REPLACE FUNCTION public.sp_abs_u_compra_recibir(p_compra_id integer, p_usuario_id integer DEFAULT NULL::integer, p_fecha_ingreso date DEFAULT NULL::date)
 RETURNS TABLE(o_compra_id integer, o_folio text, o_lotes_generados integer, o_varas_ingresadas integer, o_productos integer)
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_estado  estado_compra;
    v_folio   text;
    v_prov    int;
    v_fecha   date := coalesce(p_fecha_ingreso, CURRENT_DATE);
    r         record;
    v_codigo  text;
    v_vence   date;
    v_varas   int;
    v_stock   int;
    v_lotes   int := 0;
    v_total   int := 0;
    v_prods   int := 0;
BEGIN
    -- FOR UPDATE: dos personas dando "recibir" al mismo tiempo generarían
    -- los lotes dos veces y duplicarían el stock.
    SELECT c.estado, c.folio, c.proveedor_id
      INTO v_estado, v_folio, v_prov
      FROM compras c WHERE c.id = p_compra_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La compra no existe.';
    END IF;

    IF v_estado = 'recibida' THEN
        RAISE EXCEPTION 'La compra % ya fue recibida.', v_folio;
    END IF;

    IF v_estado = 'anulada' THEN
        RAISE EXCEPTION 'La compra % está anulada.', v_folio;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM compra_items WHERE compra_id = p_compra_id) THEN
        RAISE EXCEPTION 'La compra no tiene líneas: no hay nada que recibir.';
    END IF;

    FOR r IN
        SELECT ci.id AS item_id, ci.producto_id, ci.presentacion_id,
               ci.cantidad, ci.varas_totales, ci.costo_por_vara,
               pe.paquetes, pe.varas_por_paquete,
               p.dias_vida, p.precio, p.controla_lotes
        FROM compra_items ci
        JOIN presentaciones pe ON pe.id = ci.presentacion_id
        JOIN productos p       ON p.id  = ci.producto_id
        WHERE ci.compra_id = p_compra_id
        ORDER BY ci.id
    LOOP
        -- varas_totales ya viene calculado desde la línea:
        -- cantidad × paquetes × varas_por_paquete.
        v_varas := r.varas_totales;

        v_vence := CASE WHEN r.dias_vida IS NOT NULL THEN v_fecha + r.dias_vida END;

        IF r.controla_lotes THEN
            v_codigo := fn_abs_codigo_lote();

            INSERT INTO lotes (
                codigo, producto_id, compra_id, compra_item_id, proveedor_id,
                presentacion_id, fecha_ingreso, fecha_vencimiento,
                varas_iniciales, varas_disponibles, costo_por_vara,
                estado, precio_unitario,
                -- Queda anotado cómo llegó: sirve al contar el balde y para
                -- entender un descuadre meses después.
                notas
            )
            VALUES (
                v_codigo, r.producto_id, p_compra_id, r.item_id, v_prov,
                r.presentacion_id, v_fecha, v_vence,
                v_varas, v_varas, r.costo_por_vara,
                'activo', r.precio,
                r.cantidad || ' × ' || r.paquetes || ' paquete(s) de ' ||
                r.varas_por_paquete || ' varas'
            );

            UPDATE productos
               SET stock = coalesce(stock, 0) + v_varas,
                   actualizado_en = now()
             WHERE id = r.producto_id
            RETURNING stock INTO v_stock;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.producto_id,
                (SELECT id FROM lotes WHERE codigo = v_codigo),
                'entrada', v_varas, v_stock,
                'Recepción de compra ' || v_folio,
                'Lote ' || v_codigo,
                p_usuario_id, 'compra', p_compra_id
            );

            v_lotes := v_lotes + 1;
        ELSE
            -- Sin lotes: entra al stock y nada más. Antes se creaba un lote
            -- igual, que quedaba fantasma (se vende del stock, nunca del
            -- lote) y el inventario lo contaba dos veces.
            --
            -- Y el costo pasa a ser el promedio ponderado entre lo que había
            -- y lo que llega: es la única fuente de costo de estos productos,
            -- y sin ella las mermas se valorizaban en cero.
            UPDATE productos
               SET costo = CASE
                       WHEN r.costo_por_vara <= 0 THEN costo
                       WHEN coalesce(stock, 0) <= 0 OR coalesce(costo, 0) <= 0
                           THEN ROUND(r.costo_por_vara)
                       ELSE ROUND((stock * costo + v_varas * r.costo_por_vara)
                                  / (stock + v_varas))
                   END,
                   stock = coalesce(stock, 0) + v_varas,
                   actualizado_en = now()
             WHERE id = r.producto_id
            RETURNING stock INTO v_stock;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.producto_id, NULL, 'entrada', v_varas, v_stock,
                'Recepción de compra ' || v_folio,
                r.cantidad || ' × ' || r.paquetes || ' × ' || r.varas_por_paquete,
                p_usuario_id, 'compra', p_compra_id
            );
        END IF;

        v_total := v_total + v_varas;
        v_prods := v_prods + 1;
    END LOOP;

    UPDATE compras SET
        estado         = 'recibida',
        recibida_en    = now(),
        usuario_id     = coalesce(p_usuario_id, usuario_id),
        actualizado_en = now()
    WHERE id = p_compra_id;

    RETURN QUERY
    SELECT p_compra_id AS compra_id, v_folio AS folio,
           v_lotes AS lotes_generados, v_total AS varas_ingresadas,
           v_prods AS productos;
END;
$function$
;
