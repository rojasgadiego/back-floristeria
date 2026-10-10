-- =====================================================================
-- Mermas: "escaneado" solo si el código es el del origen
-- =====================================================================
-- El control de "mermas sin escanear" confía en lo que manda el teléfono.
-- La diferencia entre lector y tipeo solo se ve allá; lo que la base sí
-- puede exigir es que el código declarado como escaneado sea el del lote o
-- la partida de donde sale la merma. Sin eso, una merma a mano podía
-- llegar marcada como escaneada.
--
-- Copia la versión vigente en producción (con lo de sql/15, 16 y 18).
--
-- Idempotente.
-- =====================================================================

CREATE OR REPLACE FUNCTION public.sp_mer_i_merma(p_producto_id integer, p_lote_id integer, p_partida_id integer, p_cantidad integer, p_motivo text, p_detalle text DEFAULT NULL::text, p_destino destino_merma DEFAULT 'perdida'::destino_merma, p_cantidad_recuperada integer DEFAULT 0, p_calidad calidad_reingreso DEFAULT NULL::calidad_reingreso, p_codigo_escaneado text DEFAULT NULL::text, p_escaneado boolean DEFAULT false, p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer)
 RETURNS integer
 LANGUAGE plpgsql
AS $function$
DECLARE
    v_prod        record;
    v_lote        record;
    v_part        record;
    v_id          int;
    v_costo_u     int;
    v_recup       int := GREATEST(COALESCE(p_cantidad_recuperada, 0), 0);
    v_costo_rec_u int := 0;
    v_lote_rec    int;
    v_codigo      text;
    v_precio_rec  int;
    v_stock       int;
    v_umbral      int;
    v_valor       int;
    v_precio_ref  int;
    v_valor_aut   int;
    v_origen      text;
    -- Lo que el lote de recuperación hereda del origen. Van en variables y
    -- no leídos de v_lote / v_part: el registro que no se cargó (el de la
    -- otra rama) hace caer la función entera al tocarlo.
    v_proveedor   int;
    v_mot         motivos_merma;
    v_orig_lote   int;
    v_orig_codigo text;
BEGIN
    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'La cantidad debe ser al menos 1.';
    END IF;

    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica el motivo de la merma.';
    END IF;

    -- Del catálogo, y con detalle si el motivo lo pide.
    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    IF p_lote_id IS NOT NULL AND p_partida_id IS NOT NULL THEN
        RAISE EXCEPTION 'La merma sale de un lote o de una partida, no de los dos.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    -- ═══ De dónde salen las varas ═══

    IF p_partida_id IS NOT NULL THEN
        -- ─── Del mostrador ───
        v_origen := 'partida';

        SELECT * INTO v_part
          FROM partidas_mostrador WHERE id = p_partida_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'La partida no existe.';
        END IF;

        IF v_part.producto_id <> p_producto_id THEN
            RAISE EXCEPTION 'Esa partida es de otro producto.';
        END IF;

        IF v_part.cantidad_disponible < p_cantidad THEN
            RAISE EXCEPTION 'La partida % tiene % y estás mermando %.',
                v_part.codigo, v_part.cantidad_disponible, p_cantidad;
        END IF;

        v_costo_u := ROUND(COALESCE(v_part.costo_por_vara, 0));
        v_precio_ref := v_part.precio_unitario;
        v_orig_lote   := v_part.lote_id;
        v_orig_codigo := v_part.codigo;
        SELECT proveedor_id INTO v_proveedor FROM lotes WHERE id = v_part.lote_id;

        UPDATE partidas_mostrador
           SET cantidad_disponible = cantidad_disponible - p_cantidad,
               agotada_en = CASE WHEN cantidad_disponible - p_cantidad = 0
                                 THEN now() ELSE agotada_en END
         WHERE id = p_partida_id;

    ELSIF p_lote_id IS NOT NULL THEN
        -- ─── De bodega ───
        v_origen := 'lote';

        SELECT * INTO v_lote FROM lotes WHERE id = p_lote_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'El lote no existe.';
        END IF;

        IF v_lote.producto_id <> p_producto_id THEN
            RAISE EXCEPTION 'Ese lote es de otro producto.';
        END IF;

        IF v_lote.varas_disponibles < p_cantidad THEN
            RAISE EXCEPTION 'El lote % tiene % varas y estás mermando %.',
                v_lote.codigo, v_lote.varas_disponibles, p_cantidad;
        END IF;

        v_costo_u := ROUND(v_lote.costo_por_vara);
        v_precio_ref := COALESCE(v_lote.precio_unitario, v_prod.precio);
        v_orig_lote   := p_lote_id;
        v_orig_codigo := v_lote.codigo;
        v_proveedor   := v_lote.proveedor_id;

        UPDATE lotes
           SET varas_disponibles = varas_disponibles - p_cantidad,
               estado = CASE WHEN varas_disponibles - p_cantidad = 0
                             THEN 'agotado'::estado_lote ELSE estado END,
               actualizado_en = now()
         WHERE id = p_lote_id;

    ELSE
        -- ─── Sin origen: lo que no controla lotes, o armados ───
        v_origen := 'stock';
        v_costo_u := ROUND(COALESCE(v_prod.costo, v_prod.costo_armado, 0));
        v_precio_ref := v_prod.precio;

        IF v_prod.tipo = 'armado' THEN
            IF COALESCE(v_prod.stock_listo, 0) < p_cantidad THEN
                RAISE EXCEPTION 'Solo hay % de % armados.',
                    COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
            END IF;
        ELSIF COALESCE(v_prod.stock, 0) < p_cantidad THEN
            RAISE EXCEPTION 'Solo hay % de % en bodega.',
                COALESCE(v_prod.stock, 0), v_prod.nombre;
        END IF;

        -- Un producto que controla lotes SIN indicar de cuál es la puerta
        -- abierta: nadie sabría de qué balde salieron esas varas, y el
        -- stock del producto dejaría de cuadrar con la suma de sus lotes.
        IF v_prod.controla_lotes AND v_prod.tipo <> 'armado' THEN
            RAISE EXCEPTION
                '% se maneja por lotes: escanea el balde o la partida de donde sale.',
                v_prod.nombre;
        END IF;
    END IF;

    -- ═══ Autorización ═══
    --
    -- Sobre el umbral hace falta la clave de una administradora. Es el
    -- mismo mecanismo del descuento en la venta: quien se lleva flor de a
    -- poco no puede escalar sin un cómplice.
    --
    -- Quién puede autorizar y si la clave es correcta lo verifica la API,
    -- que es donde vive BCrypt; acá solo se exige que venga firmado.
    v_umbral := fn_mer_umbral_autorizacion();
    v_valor  := p_cantidad * v_costo_u;

    -- Sin costo cargado, la merma valdría $0 y nunca pediría firma: es
    -- justo el producto del que nadie sabe cuánto costó. Para decidir la
    -- firma se usa entonces el precio de venta; el costo registrado sigue
    -- siendo el real (cero), para no inflar los reportes.
    v_valor_aut := p_cantidad * CASE WHEN v_costo_u > 0 THEN v_costo_u
                                     ELSE COALESCE(v_precio_ref, 0) END;

    IF v_valor_aut > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION
            'Una merma de $% necesita autorización: el tope sin firma es $%.%',
            v_valor_aut, v_umbral,
            CASE WHEN v_costo_u = 0 THEN ' (valorizada a precio de venta: el producto no tiene costo)' ELSE '' END;
    END IF;

    -- ═══ Validar el reingreso ═══
    -- Recuperar crea un lote nuevo con precio rebajado. Un producto que no
    -- se maneja por lotes no tiene dónde ponerlo: el lote quedaba fantasma
    -- (nunca se descuenta al vender y el inventario lo contaba dos veces), y
    -- en un armado las unidades desaparecían. Ahí lo que se salva se deja
    -- donde está y se registra solo lo perdido.
    IF p_destino = 'reingreso' AND (NOT v_prod.controla_lotes OR v_prod.tipo = 'armado') THEN
        RAISE EXCEPTION
            '% no se maneja por lotes: lo que se salva queda donde está. Registra como merma solo lo que se pierde.',
            v_prod.nombre;
    END IF;

    IF p_destino = 'reingreso' THEN
        IF v_recup < 1 THEN
            RAISE EXCEPTION 'Indica cuántas varas se recuperan.';
        END IF;

        IF v_recup > p_cantidad THEN
            RAISE EXCEPTION 'No se pueden recuperar % de % varas.', v_recup, p_cantidad;
        END IF;

        IF p_calidad IS NULL THEN
            RAISE EXCEPTION 'Indica en qué estado vuelve la flor.';
        END IF;
    ELSE
        v_recup := 0;
    END IF;


    -- ═══ El costo de lo recuperado ═══
    -- costo_perdido lo calcula la tabla (columna generada): cero en una
    -- devolución al proveedor, y en un reingreso solo la rebaja de lo que
    -- vuelve más lo que se perdió entero.
    IF p_destino = 'reingreso' THEN
        v_costo_rec_u := ROUND(v_costo_u * (1 - fn_mer_rebaja(p_calidad)));
    END IF;

    -- ═══ El lote de recuperación ═══
    IF v_recup > 0 THEN
        v_codigo := fn_abs_codigo_lote();
        v_precio_rec := ROUND(v_prod.precio * (1 - fn_mer_rebaja(p_calidad)));

        INSERT INTO lotes (
            codigo, producto_id, proveedor_id,
            fecha_ingreso, fecha_vencimiento,
            varas_iniciales, varas_disponibles, costo_por_vara,
            estado, precio_unitario, origen_lote_id, calidad, notas
        )
        VALUES (
            v_codigo, p_producto_id,
            v_proveedor,
            CURRENT_DATE,
            CURRENT_DATE + fn_mer_dias_vida_recuperado(p_calidad),
            v_recup, v_recup, v_costo_rec_u,
            'activo', v_precio_rec,
            v_orig_lote, p_calidad,
            'Recuperado de ' || COALESCE(v_orig_codigo, 'stock') ||
            ' · ' || btrim(p_motivo)
        )
        RETURNING id INTO v_lote_rec;
    END IF;

    -- ═══ El stock del producto ═══
    --
    -- Solo se descuenta si salió de bodega o del stock directo. Una merma
    -- del mostrador ya descontó de la partida, y el stock del producto no
    -- incluye lo que está adelante.
    IF v_prod.tipo = 'armado' THEN
        UPDATE productos
           SET stock_listo = stock_listo - p_cantidad, actualizado_en = now()
         WHERE id = p_producto_id;

    ELSIF v_origen = 'partida' THEN
        -- Solo entra lo que se recupera: vuelve a bodega.
        IF v_recup > 0 THEN
            UPDATE productos
               SET stock = COALESCE(stock, 0) + v_recup, actualizado_en = now()
             WHERE id = p_producto_id
            RETURNING stock INTO v_stock;
        END IF;

    ELSE
        UPDATE productos
           SET stock = COALESCE(stock, 0) - p_cantidad + v_recup,
               actualizado_en = now()
         WHERE id = p_producto_id
        RETURNING stock INTO v_stock;
    END IF;

    -- ═══ El registro ═══
    INSERT INTO mermas (
        producto_id, lote_id, partida_id, cantidad, motivo, motivo_id, detalle,
        costo_unitario, costo_total, usuario_id,
        destino, cantidad_recuperada, calidad_reingreso,
        lote_recuperacion_id, costo_recuperado_unitario,
        codigo_escaneado, escaneado, autorizado_por
    )
    VALUES (
        p_producto_id, p_lote_id, p_partida_id, p_cantidad,
        v_mot.nombre, v_mot.id, nullif(btrim(coalesce(p_detalle, '')), ''),
        v_costo_u, v_valor, p_usuario_id,
        p_destino, v_recup, p_calidad,
        v_lote_rec, CASE WHEN v_recup > 0 THEN v_costo_rec_u END,
        nullif(btrim(coalesce(p_codigo_escaneado, '')), ''),
        -- "Escaneado" lo dice el teléfono (lector vs. tipeo), y eso no se
        -- puede comprobar acá. Lo que sí: que el código leído sea el del
        -- balde o la partida de donde sale. Una merma a mano, sin balde, no
        -- puede declararse escaneada.
        COALESCE(p_escaneado, false)
            AND v_orig_codigo IS NOT NULL
            AND upper(btrim(coalesce(p_codigo_escaneado, ''))) = upper(v_orig_codigo),
        nullif(btrim(coalesce(p_autorizado_por, '')), '')
    )
    RETURNING id INTO v_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, p_lote_id, 'merma', -p_cantidad, v_stock - v_recup,
        v_mot.nombre,
        CASE WHEN v_recup > 0
             THEN v_recup || ' recuperadas en ' || v_codigo
             ELSE COALESCE(p_detalle, v_origen) END,
        p_usuario_id, 'merma', v_id
    );

    -- Lo recuperado vuelve a entrar, en su lote nuevo. Sin esta fila el
    -- historial decía -cantidad mientras el stock bajaba solo lo perdido.
    IF v_recup > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, lote_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            p_producto_id, v_lote_rec, 'entrada', v_recup, v_stock,
            'Recuperado de merma', v_mot.nombre || ' · ' || v_codigo,
            p_usuario_id, 'merma', v_id
        );
    END IF;

    RETURN v_id;
END;
$function$
;
