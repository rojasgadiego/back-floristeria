-- ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━
-- 11 · Despacho a domicilio y códigos de autorización de descuento
-- ━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━

-- ─── 1. Despacho a domicilio ──────────────────────────────────────────
ALTER TABLE ventas
    ADD COLUMN IF NOT EXISTS es_despacho        BOOLEAN NOT NULL DEFAULT FALSE,
    ADD COLUMN IF NOT EXISTS direccion_despacho TEXT;

-- ─── 2. Códigos temporales de autorización de descuento ───────────────
-- Un código de 6 dígitos, válido 10 minutos, de un solo uso.
-- El administrador lo recibe por correo y se lo dicta al vendedor.
CREATE TABLE IF NOT EXISTS codigos_autorizacion_descuento (
    id          SERIAL      PRIMARY KEY,
    codigo      CHAR(6)     NOT NULL,
    descuento   INT         NOT NULL CHECK (descuento > 0),
    vendedor_id INT         NOT NULL REFERENCES usuarios(id),
    usado       BOOLEAN     NOT NULL DEFAULT FALSE,
    creado_en   TIMESTAMPTZ NOT NULL DEFAULT now(),
    expira_en   TIMESTAMPTZ NOT NULL DEFAULT now() + INTERVAL '10 minutes'
);

CREATE INDEX IF NOT EXISTS ix_cod_aut_codigo
    ON codigos_autorizacion_descuento(codigo)
    WHERE NOT usado;

-- ─── 3. SP actualizado: acepta es_despacho y dir_despacho ─────────────
CREATE OR REPLACE FUNCTION public.sp_ven_i_venta(
    p_items            jsonb,
    p_medio_pago       medio_pago,
    p_usuario_id       integer,
    p_cliente_id       integer  DEFAULT NULL::integer,
    p_promocion_id     integer  DEFAULT NULL::integer,
    p_descuento_manual integer  DEFAULT 0,
    p_puntos_canjeados integer  DEFAULT 0,
    p_recibido         integer  DEFAULT NULL::integer,
    p_autorizado_por   text     DEFAULT NULL::text,
    p_cotizacion_id    integer  DEFAULT NULL::integer,
    p_es_despacho      boolean  DEFAULT FALSE,
    p_dir_despacho     text     DEFAULT NULL::text
)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_caja        int;
    v_venta       int;
    v_folio       text;
    v_atencion    text;
    it            jsonb;
    v_n           int := 0;
    v_prod        record;
    v_part        record;
    v_cant        int;
    v_precio      int;
    v_pendiente   int;
    v_toma        int;
    v_bruto       int := 0;
    v_promo       int := 0;
    v_manual      int := 0;
    v_canje       int := 0;
    v_total_desc  int;
    v_iva_tasa    numeric;
    v_neto        int;
    v_iva         int;
    v_total       int;
    v_umbral      int;
    v_club        boolean;
    v_valor_punto int;
    v_canje_min   int;
    v_por_peso    int;
    v_puntos_gana int := 0;
    v_saldo       int;
    v_items_calc  jsonb := '[]'::jsonb;
    v_cot         record;
    v_abono       int := 0;
BEGIN
    -- ═══ 1. Caja abierta ═══
    SELECT id INTO v_caja FROM cajas WHERE estado = 'abierta';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay caja abierta. Abre el turno antes de vender.';
    END IF;

    -- ═══ 1b. Cobro final de una cotización ═══
    IF p_cotizacion_id IS NOT NULL THEN
        SELECT * INTO v_cot FROM cotizaciones WHERE id = p_cotizacion_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'La cotización no existe.';
        END IF;

        IF v_cot.estado <> 'aprobada' THEN
            RAISE EXCEPTION 'Solo se cobra una cotización aprobada: % está %.', v_cot.folio, v_cot.estado;
        END IF;

        SELECT abono INTO v_abono FROM vw_cot_resumen WHERE id = p_cotizacion_id;

        p_cliente_id := COALESCE(p_cliente_id, v_cot.cliente_id);
    END IF;

    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        IF p_cotizacion_id IS NULL THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;

        IF v_cot.traslado = 0 AND v_cot.montaje = 0
           AND NOT EXISTS (SELECT 1 FROM cotizacion_items
                            WHERE cotizacion_id = p_cotizacion_id AND a_medida) THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;
    END IF;

    -- ═══ 2. La cabecera, para tener el id ═══
    SELECT 'B-' || to_char(CURRENT_DATE, 'YYYY') || '-' ||
           lpad((count(*) + 1)::text, 5, '0')
      INTO v_folio
      FROM ventas WHERE extract(year FROM creado_en) = extract(year FROM CURRENT_DATE);

    SELECT lpad((count(*) + 1)::text, 3, '0') INTO v_atencion
      FROM ventas WHERE creado_en::date = CURRENT_DATE;

    v_iva_tasa := fn_ven_config('venta', 'iva', 19);

    INSERT INTO ventas (
        folio, numero_atencion, caja_id, usuario_id, cliente_id,
        promocion_id, cotizacion_id, iva_tasa, medio_pago,
        bruto, descuento_promo, descuento_manual, descuento_canje,
        descuento_total, neto, iva_monto, total,
        es_despacho, direccion_despacho
    )
    VALUES (
        v_folio, v_atencion, v_caja, p_usuario_id, p_cliente_id,
        p_promocion_id, p_cotizacion_id, v_iva_tasa, p_medio_pago,
        0, 0, 0, 0, 0, 0, 0, 0,
        COALESCE(p_es_despacho, FALSE),
        NULLIF(BTRIM(COALESCE(p_dir_despacho, '')), '')
    )
    RETURNING id INTO v_venta;

    -- ═══ 3. Las líneas ═══
    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n    := v_n + 1;
        v_cant := COALESCE((it->>'cantidad')::int, 0);

        IF v_cant < 1 THEN
            RAISE EXCEPTION 'Línea %: la cantidad debe ser al menos 1.', v_n;
        END IF;

        -- ─── Con partida escaneada ───
        IF it ? 'partida' AND nullif(btrim(it->>'partida'), '') IS NOT NULL THEN

            SELECT pm.*, p.nombre AS producto, p.activo
              INTO v_part
              FROM partidas_mostrador pm
              JOIN productos p ON p.id = pm.producto_id
             WHERE pm.codigo = upper(btrim(it->>'partida'))
               FOR UPDATE OF pm;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: no existe la partida %.', v_n, it->>'partida';
            END IF;

            IF NOT v_part.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_part.producto;
            END IF;

            IF v_part.fecha_vencimiento IS NOT NULL
               AND v_part.fecha_vencimiento < CURRENT_DATE THEN
                RAISE EXCEPTION 'Línea %: la partida % venció el %.',
                    v_n, v_part.codigo, to_char(v_part.fecha_vencimiento, 'DD-MM-YYYY');
            END IF;

            IF v_part.cantidad_disponible < v_cant THEN
                RAISE EXCEPTION 'Línea %: la partida % tiene % y estás vendiendo %.',
                    v_n, v_part.codigo, v_part.cantidad_disponible, v_cant;
            END IF;

            v_precio := v_part.precio_unitario;

            UPDATE partidas_mostrador
               SET cantidad_disponible = cantidad_disponible - v_cant,
                   agotada_en = CASE WHEN cantidad_disponible - v_cant = 0
                                     THEN now() ELSE agotada_en END
             WHERE id = v_part.id;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.producto,
                v_precio, v_cant, v_precio * v_cant, false
            );

            INSERT INTO venta_consumos (
                venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.lote_id, 'simple',
                v_cant, COALESCE(v_part.costo_por_vara, 0)
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_part.producto_id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);

        -- ─── Sin escanear: por producto ───
        ELSE
            SELECT * INTO v_prod
              FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
            END IF;

            IF NOT v_prod.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_prod.nombre;
            END IF;

            v_precio := v_prod.precio;

            IF v_prod.tipo = 'armado' THEN
                IF COALESCE(v_prod.stock_listo, 0) < v_cant THEN
                    RAISE EXCEPTION 'Línea %: solo hay % de % armados.',
                        v_n, COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
                END IF;

                UPDATE productos
                   SET stock_listo = stock_listo - v_cant, actualizado_en = now()
                 WHERE id = v_prod.id;

                INSERT INTO venta_consumos (
                    venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                )
                VALUES (
                    v_venta, v_prod.id, NULL, 'listo',
                    v_cant, COALESCE(v_prod.costo_armado, 0)
                );

            ELSE
                v_pendiente := v_cant;

                FOR v_part IN
                    SELECT pm.id, pm.codigo, pm.lote_id, pm.cantidad_disponible,
                           pm.costo_por_vara
                    FROM partidas_mostrador pm
                    WHERE pm.producto_id = v_prod.id
                      AND pm.cantidad_disponible > 0
                      AND COALESCE(pm.fecha_vencimiento >= CURRENT_DATE, true)
                    ORDER BY pm.fecha_vencimiento NULLS LAST, pm.traspasado_en, pm.id
                    FOR UPDATE
                LOOP
                    EXIT WHEN v_pendiente = 0;

                    v_toma := LEAST(v_pendiente, v_part.cantidad_disponible);

                    UPDATE partidas_mostrador
                       SET cantidad_disponible = cantidad_disponible - v_toma,
                           agotada_en = CASE WHEN cantidad_disponible - v_toma = 0
                                             THEN now() ELSE agotada_en END
                     WHERE id = v_part.id;

                    INSERT INTO venta_consumos (
                        venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                    )
                    VALUES (
                        v_venta, v_prod.id, v_part.lote_id, 'simple',
                        v_toma, COALESCE(v_part.costo_por_vara, 0)
                    );

                    v_pendiente := v_pendiente - v_toma;
                END LOOP;

                IF v_pendiente > 0 THEN
                    RAISE EXCEPTION 'Línea %: faltan % de % en el mostrador. Baja más de bodega.',
                        v_n, v_pendiente, v_prod.nombre;
                END IF;
            END IF;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_prod.id, v_prod.nombre,
                v_precio, v_cant, v_precio * v_cant, false
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_prod.id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);
        END IF;
    END LOOP;

    -- ═══ 3b. Los servicios de la cotización ═══
    IF p_cotizacion_id IS NOT NULL THEN
        INSERT INTO venta_items (
            venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
        )
        SELECT v_venta, NULL, s.nombre, s.precio, s.cantidad, s.precio * s.cantidad, true
        FROM (
            SELECT i.nombre, i.precio, i.cantidad, 1 AS orden, i.id
              FROM cotizacion_items i
             WHERE i.cotizacion_id = p_cotizacion_id AND i.a_medida
            UNION ALL
            SELECT 'Traslado', v_cot.traslado, 1, 2, 0 WHERE v_cot.traslado > 0
            UNION ALL
            SELECT 'Montaje', v_cot.montaje, 1, 3, 0 WHERE v_cot.montaje > 0
        ) s
        ORDER BY s.orden, s.id;

        v_bruto := v_bruto + COALESCE((
            SELECT sum(subtotal) FROM venta_items
             WHERE venta_id = v_venta AND es_servicio), 0);
    END IF;

    -- ═══ 4. Descuentos ═══
    IF p_promocion_id IS NOT NULL THEN
        v_promo := fn_ven_descuento_promo(p_promocion_id, v_items_calc);

        IF v_promo = 0 THEN
            RAISE EXCEPTION 'La promoción ya no aplica a este carrito.';
        END IF;

        UPDATE promociones SET usos = usos + 1, actualizado_en = now()
         WHERE id = p_promocion_id;
    END IF;

    v_manual := GREATEST(COALESCE(p_descuento_manual, 0), 0);
    v_umbral := fn_ven_config('venta', 'descuentoSinAutorizacion', 2000)::int;

    IF v_manual > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION 'Un descuento sobre $% necesita autorización.', v_umbral;
    END IF;

    -- ═══ 5. Canje de puntos ═══
    v_club        := fn_ven_config_bool('club', 'activo', false);
    v_valor_punto := fn_ven_config('club', 'valorPunto', 10)::int;
    v_canje_min   := fn_ven_config('club', 'canjeMinimo', 50)::int;
    v_por_peso    := fn_ven_config('club', 'puntosPorPeso', 1000)::int;

    IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
        IF NOT v_club THEN
            RAISE EXCEPTION 'El club de clientes está desactivado.';
        END IF;

        IF p_cliente_id IS NULL THEN
            RAISE EXCEPTION 'Para canjear puntos hay que identificar al cliente.';
        END IF;

        IF p_puntos_canjeados < v_canje_min THEN
            RAISE EXCEPTION 'El canje mínimo es de % puntos.', v_canje_min;
        END IF;

        SELECT puntos INTO v_saldo FROM clientes WHERE id = p_cliente_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'El cliente no existe.';
        END IF;

        IF v_saldo < p_puntos_canjeados THEN
            RAISE EXCEPTION 'El cliente tiene % puntos y quieres canjear %.',
                v_saldo, p_puntos_canjeados;
        END IF;

        v_canje := p_puntos_canjeados * v_valor_punto;
    END IF;

    v_total_desc := LEAST(v_promo + v_manual + v_canje, v_bruto);
    v_total      := v_bruto - v_total_desc;

    IF v_abono > v_total THEN
        RAISE EXCEPTION 'Ya se abonaron $% y esta boleta suma $%. Revisa las líneas o anula un abono.',
            v_abono, v_total;
    END IF;

    v_total := v_total - v_abono;

    v_neto := ROUND(v_total / (1 + v_iva_tasa / 100.0));
    v_iva  := v_total - v_neto;

    IF p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL AND p_recibido < v_total THEN
        RAISE EXCEPTION 'Recibiste $% y el total es $%.', p_recibido, v_total;
    END IF;

    -- ═══ 6. Puntos ganados ═══
    IF v_club AND p_cliente_id IS NOT NULL AND v_por_peso > 0 THEN
        v_puntos_gana := floor((v_total + v_abono) / v_por_peso)::int;
    END IF;

    -- ═══ 7. Cerrar la boleta ═══
    UPDATE ventas SET
        bruto            = v_bruto,
        descuento_promo  = v_promo,
        descuento_manual = v_manual,
        descuento_canje  = v_canje,
        descuento_total  = v_total_desc,
        neto             = v_neto,
        iva_monto        = v_iva,
        total            = v_total,
        recibido         = p_recibido,
        vuelto           = CASE WHEN p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL
                                THEN p_recibido - v_total END,
        autorizado_por   = nullif(btrim(coalesce(p_autorizado_por, '')), ''),
        puntos_ganados   = v_puntos_gana,
        puntos_canjeados = COALESCE(p_puntos_canjeados, 0),
        abono_previo     = v_abono
    WHERE id = v_venta;

    -- ═══ 8. Puntos del cliente ═══
    IF p_cliente_id IS NOT NULL
       AND (v_puntos_gana > 0 OR COALESCE(p_puntos_canjeados, 0) > 0) THEN

        UPDATE clientes
           SET puntos = puntos + v_puntos_gana - COALESCE(p_puntos_canjeados, 0),
               actualizado_en = now()
         WHERE id = p_cliente_id
        RETURNING puntos INTO v_saldo;

        IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, -p_puntos_canjeados, v_saldo,
                    'Canje en boleta ' || v_folio, v_venta, p_usuario_id);
        END IF;

        IF v_puntos_gana > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, v_puntos_gana, v_saldo,
                    'Compra ' || v_folio, v_venta, p_usuario_id);
        END IF;
    END IF;

    -- ═══ 9. Los movimientos de inventario ═══
    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, motivo, detalle,
        usuario_id, referencia_tipo, referencia_id
    )
    SELECT vc.producto_id, NULL, 'venta', -sum(vc.cantidad),
           'Venta ' || v_folio, NULL,
           p_usuario_id, 'venta', v_venta
    FROM venta_consumos vc
    WHERE vc.venta_id = v_venta
    GROUP BY vc.producto_id;

    -- ═══ 10. La cotización queda cobrada ═══
    IF p_cotizacion_id IS NOT NULL THEN
        UPDATE cotizaciones SET
            estado = 'cobrada', venta_id = v_venta,
            cobrada_en = now(), actualizado_en = now()
        WHERE id = p_cotizacion_id;
    END IF;

    RETURN v_venta;
END;
$function$;
