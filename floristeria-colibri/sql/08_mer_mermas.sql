-- =====================================================================
-- Mermas: reversa consistente, desarme con firma, privacidad y umbral
-- =====================================================================
-- 1. Revertir una merma del MOSTRADOR no devolvía las varas a la partida
--    y sumaba al stock de bodega algo que nunca se había descontado.
-- 2. Revertir un DESARME sumaba stock a cada componente (que ya se había
--    consumido al armar) y no devolvía el armado. Ahora el desarme queda
--    agrupado y se revierte entero.
-- 3. El desarme no pedía firma sobre el umbral: era la forma de saltárselo.
-- 4. Privacidad: quien no es administrador ve solo las mermas que registró.
-- 5. El umbral se configura en Configuración → Venta
--    (venta.mermaSinAutorizacion). Sin valor, sigue valiendo 15.000.
-- Además: el motivo de la reversa tiene columna propia en vez de pegarse al
-- detalle, y registrar mermas vuelve a funcionar (fallaba siempre).
--
-- Varias funciones cambian de firma, así que van con DROP + CREATE.
-- Idempotente.
-- =====================================================================

-- ═══ Columnas ═══

ALTER TABLE mermas ADD COLUMN IF NOT EXISTS motivo_reversion text;

-- Todas las mermas de un mismo desarme comparten grupo: se revierten juntas.
ALTER TABLE mermas ADD COLUMN IF NOT EXISTS desarme_grupo integer;
ALTER TABLE mermas ADD COLUMN IF NOT EXISTS desarme_producto_id integer REFERENCES productos(id);
ALTER TABLE mermas ADD COLUMN IF NOT EXISTS desarme_cantidad integer;

CREATE SEQUENCE IF NOT EXISTS seq_mer_desarme_grupo;

CREATE INDEX IF NOT EXISTS ix_mermas_desarme_grupo
    ON mermas (desarme_grupo) WHERE desarme_grupo IS NOT NULL;


-- ═══ Umbral configurable ═══

CREATE OR REPLACE FUNCTION fn_mer_umbral_autorizacion()
RETURNS integer
LANGUAGE sql
STABLE
AS $function$
    -- Vive en la sección venta, junto al tope del descuento: los dos son
    -- "hasta cuánto se puede sin pedir firma". La sección 'merma' queda como
    -- respaldo por si alguien la cargó a mano.
    SELECT COALESCE(
        fn_ven_config('venta', 'mermaSinAutorizacion',
                      fn_ven_config('merma', 'sinAutorizacion', 15000))::int,
        15000);
$function$;

CREATE OR REPLACE FUNCTION sp_cfg_u_seccion(p_clave text, p_valor jsonb, p_usuario_id integer DEFAULT NULL::integer)
RETURNS jsonb
LANGUAGE plpgsql
AS $function$
DECLARE
    v_valor jsonb := p_valor;
BEGIN
    IF p_clave NOT IN ('local', 'ticket', 'venta', 'club') THEN
        RAISE EXCEPTION 'Sección desconocida: %.', p_clave;
    END IF;

    IF p_valor IS NULL OR jsonb_typeof(p_valor) <> 'object' THEN
        RAISE EXCEPTION 'La configuración debe ser un objeto.';
    END IF;

    -- ─── Validaciones por sección ───

    IF p_clave = 'local' THEN
        IF btrim(coalesce(p_valor->>'nombre', '')) = '' THEN
            RAISE EXCEPTION 'El nombre del local es obligatorio: sale impreso en cada ticket.';
        END IF;
    END IF;

    IF p_clave = 'venta' THEN
        IF (p_valor->>'iva')::numeric < 0 OR (p_valor->>'iva')::numeric > 100 THEN
            RAISE EXCEPTION 'El IVA debe estar entre 0 y 100.';
        END IF;

        IF (p_valor->>'descuentoSinAutorizacion')::int < 0 THEN
            RAISE EXCEPTION 'El umbral de descuento no puede ser negativo.';
        END IF;

        IF (p_valor->>'mermaSinAutorizacion')::int < 0 THEN
            RAISE EXCEPTION 'El umbral de merma no puede ser negativo.';
        END IF;
    END IF;

    IF p_clave = 'club' THEN
        IF (p_valor->>'valorPunto')::int < 1 THEN
            RAISE EXCEPTION 'Cada punto tiene que valer al menos $1.';
        END IF;

        IF (p_valor->>'puntosPorPeso')::int < 1 THEN
            RAISE EXCEPTION 'Indica cada cuántos pesos se gana un punto.';
        END IF;

        IF (p_valor->>'canjeMinimo')::int < 1 THEN
            RAISE EXCEPTION 'El canje mínimo debe ser al menos 1 punto.';
        END IF;
    END IF;

    INSERT INTO configuracion (clave, valor, actualizado_por, actualizado_en)
    VALUES (p_clave, v_valor, p_usuario_id, now())
    ON CONFLICT (clave) DO UPDATE
       SET valor = EXCLUDED.valor,
           actualizado_por = EXCLUDED.actualizado_por,
           actualizado_en = now();

    RETURN v_valor;
END;
$function$;


-- ═══ Registrar: no funcionaba ninguna merma ═══
-- a) Se escribía costo_perdido, que es una columna GENERADA: Postgres
--    rechaza el INSERT, así que ninguna merma (ni desarme) se registraba.
--    Ahora lo calcula la tabla con la misma fórmula.
-- b) Al crear el lote recuperado se leía v_lote y v_part a la vez, y uno
--    de los dos nunca se carga (la merma sale de un lote O de una partida).
--    En PL/pgSQL eso es error: todo reingreso se caía. Ahora lo que se
--    hereda del origen se guarda en variables dentro de cada rama.
-- Misma firma: CREATE OR REPLACE alcanza.

CREATE OR REPLACE FUNCTION sp_mer_i_merma(p_producto_id integer, p_lote_id integer, p_partida_id integer, p_cantidad integer, p_motivo text, p_detalle text DEFAULT NULL::text, p_destino destino_merma DEFAULT 'perdida'::destino_merma, p_cantidad_recuperada integer DEFAULT 0, p_calidad calidad_reingreso DEFAULT NULL::calidad_reingreso, p_codigo_escaneado text DEFAULT NULL::text, p_escaneado boolean DEFAULT false, p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer)
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
    v_origen      text;
    -- Lo que el lote de recuperación hereda del origen. Van en variables y
    -- no leídos de v_lote / v_part: el registro que no se cargó (el de la
    -- otra rama) hace caer la función entera al tocarlo.
    v_proveedor   int;
    v_orig_lote   int;
    v_orig_codigo text;
BEGIN
    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'La cantidad debe ser al menos 1.';
    END IF;

    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica el motivo de la merma.';
    END IF;

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

    IF v_valor > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION
            'Una merma de $% necesita autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    -- ═══ Validar el reingreso ═══
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
        producto_id, lote_id, partida_id, cantidad, motivo, detalle,
        costo_unitario, costo_total, usuario_id,
        destino, cantidad_recuperada, calidad_reingreso,
        lote_recuperacion_id, costo_recuperado_unitario,
        codigo_escaneado, escaneado, autorizado_por
    )
    VALUES (
        p_producto_id, p_lote_id, p_partida_id, p_cantidad,
        btrim(p_motivo), nullif(btrim(coalesce(p_detalle, '')), ''),
        v_costo_u, v_valor, p_usuario_id,
        p_destino, v_recup, p_calidad,
        v_lote_rec, CASE WHEN v_recup > 0 THEN v_costo_rec_u END,
        nullif(btrim(coalesce(p_codigo_escaneado, '')), ''),
        COALESCE(p_escaneado, false),
        nullif(btrim(coalesce(p_autorizado_por, '')), '')
    )
    RETURNING id INTO v_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, p_lote_id, 'merma', -p_cantidad, v_stock,
        btrim(p_motivo),
        CASE WHEN v_recup > 0
             THEN v_recup || ' recuperadas en ' || v_codigo
             ELSE COALESCE(p_detalle, v_origen) END,
        p_usuario_id, 'merma', v_id
    );

    RETURN v_id;
END;
$function$;


-- ═══ Desarme: agrupado y con firma ═══

DROP FUNCTION IF EXISTS sp_mer_i_desarme(integer, integer, text, text, jsonb, integer);
DROP FUNCTION IF EXISTS sp_mer_i_desarme(integer, integer, text, text, jsonb, integer, text);

CREATE FUNCTION sp_mer_i_desarme(
    p_producto_id integer, p_cantidad integer, p_motivo text, p_detalle text,
    p_lineas jsonb, p_usuario_id integer DEFAULT NULL::integer,
    p_autorizado_por text DEFAULT NULL::text)
RETURNS TABLE(o_producto_id integer, o_producto text, o_desarmados integer,
              o_mermas integer, o_recuperadas integer, o_perdidas integer)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_prod      record;
    r           record;
    it          jsonb;
    v_rec       int;
    v_perd      int;
    v_calidad   calidad_reingreso;
    v_merma_id  int;
    v_mermas    int := 0;
    v_tot_rec   int := 0;
    v_tot_perd  int := 0;
    v_grupo     int;
    v_umbral    int;
    v_valor     int;
    v_firma     text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
BEGIN
    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'Indica cuántas unidades desarmar.';
    END IF;

    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica el motivo del desarme.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.tipo <> 'armado' THEN
        RAISE EXCEPTION '% no es un producto armado: no hay nada que desarmar.', v_prod.nombre;
    END IF;

    IF COALESCE(v_prod.stock_listo, 0) < p_cantidad THEN
        RAISE EXCEPTION 'Solo hay % de % armados.', COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_producto_id) THEN
        RAISE EXCEPTION '% no tiene receta: no se sabe qué componentes lleva.', v_prod.nombre;
    END IF;

    -- ═══ Autorización ═══
    -- La misma regla que una merma suelta: lo que vale lo que sale, contra
    -- el umbral. Sin esto, desarmar era la forma de mermar caro sin firma.
    v_umbral := fn_mer_umbral_autorizacion();

    SELECT COALESCE(sum(rc.cantidad * p_cantidad * ROUND(COALESCE(p.costo, 0))), 0)
      INTO v_valor
      FROM recetas rc
      JOIN productos p ON p.id = rc.componente_id
     WHERE rc.producto_id = p_producto_id;

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Un desarme de $% necesita autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    v_grupo := nextval('seq_mer_desarme_grupo');

    -- ═══ Las unidades armadas salen del stock ═══
    UPDATE productos
       SET stock_listo = stock_listo - p_cantidad, actualizado_en = now()
     WHERE id = p_producto_id;

    -- ═══ Cada componente, según lo que decidió la persona ═══
    FOR r IN
        SELECT rc.componente_id, rc.cantidad * p_cantidad AS esperado, p.nombre
        FROM recetas rc
        JOIN productos p ON p.id = rc.componente_id
        WHERE rc.producto_id = p_producto_id
    LOOP
        SELECT value INTO it
          FROM jsonb_array_elements(COALESCE(p_lineas, '[]'::jsonb)) value
         WHERE (value->>'componenteId')::int = r.componente_id
         LIMIT 1;

        IF it IS NULL THEN
            RAISE EXCEPTION 'Falta decir qué pasa con las % varas de %.', r.esperado, r.nombre;
        END IF;

        v_rec     := GREATEST(COALESCE((it->>'recuperadas')::int, 0), 0);
        v_perd    := GREATEST(COALESCE((it->>'perdidas')::int, 0), 0);
        v_calidad := nullif(it->>'calidad', '')::calidad_reingreso;

        -- Cada vara tiene que tener un destino: si la suma no cuadra, el
        -- inventario queda mintiendo.
        IF v_rec + v_perd <> r.esperado THEN
            RAISE EXCEPTION
                '% : la receta dice % varas y estás repartiendo % (% recuperadas + % perdidas).',
                r.nombre, r.esperado, v_rec + v_perd, v_rec, v_perd;
        END IF;

        IF v_rec > 0 AND v_calidad IS NULL THEN
            RAISE EXCEPTION 'Indica en qué estado vuelven las % varas de %.', v_rec, r.nombre;
        END IF;

        -- Una merma por componente. El stock de los componentes NO se tocó
        -- al armar el ramo —se descontó entonces— así que acá solo entra lo
        -- que se recupera.
        --
        -- Se pasa lote_id NULL: las varas ya perdieron su identidad de lote
        -- cuando entraron al arreglo, y adivinar de cuál venían sería
        -- inventar trazabilidad.
        INSERT INTO mermas (
            producto_id, lote_id, cantidad, motivo, detalle,
            costo_unitario, costo_total, usuario_id,
            destino, cantidad_recuperada, calidad_reingreso, costo_recuperado_unitario,
            autorizado_por, desarme_grupo, desarme_producto_id, desarme_cantidad
        )
        SELECT
            r.componente_id, NULL, r.esperado,
            btrim(p_motivo),
            'Desarme de ' || p_cantidad || ' × ' || v_prod.nombre ||
            COALESCE(' · ' || nullif(btrim(coalesce(p_detalle, '')), ''), ''),
            ROUND(COALESCE(p.costo, 0)),
            r.esperado * ROUND(COALESCE(p.costo, 0)),
            p_usuario_id,
            CASE WHEN v_rec > 0 THEN 'reingreso'::destino_merma
                 ELSE 'perdida'::destino_merma END,
            v_rec, v_calidad,
            -- costo_perdido lo calcula la tabla a partir de este valor: sin
            -- él, lo recuperado contaría como pérdida entera.
            CASE WHEN v_rec > 0
                 THEN ROUND(ROUND(COALESCE(p.costo, 0)) * (1 - fn_mer_rebaja(v_calidad)))
            END,
            v_firma, v_grupo, p_producto_id, p_cantidad
        FROM productos p WHERE p.id = r.componente_id
        RETURNING id INTO v_merma_id;

        -- El lote de recuperación, con vida útil recortada.
        IF v_rec > 0 THEN
            WITH nuevo AS (
                INSERT INTO lotes (
                    codigo, producto_id, fecha_ingreso, fecha_vencimiento,
                    varas_iniciales, varas_disponibles, costo_por_vara,
                    estado, precio_unitario, calidad, notas
                )
                SELECT
                    fn_abs_codigo_lote(), r.componente_id,
                    CURRENT_DATE,
                    CURRENT_DATE + fn_mer_dias_vida_recuperado(v_calidad),
                    v_rec, v_rec,
                    ROUND(COALESCE(p.costo, 0) * (1 - fn_mer_rebaja(v_calidad))),
                    'activo',
                    ROUND(p.precio * (1 - fn_mer_rebaja(v_calidad))),
                    v_calidad,
                    'Recuperado del desarme de ' || v_prod.nombre
                FROM productos p WHERE p.id = r.componente_id
                RETURNING id
            )
            UPDATE mermas SET lote_recuperacion_id = (SELECT id FROM nuevo)
             WHERE id = v_merma_id;

            UPDATE productos
               SET stock = COALESCE(stock, 0) + v_rec, actualizado_en = now()
             WHERE id = r.componente_id;
        END IF;

        -- Un movimiento de cantidad cero no se acepta (y no dice nada): si
        -- todo se recuperó, lo cuenta el lote nuevo.
        IF v_perd > 0 THEN
            INSERT INTO movimientos_inventario (
                producto_id, tipo, cantidad, motivo, detalle,
                usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.componente_id, 'merma', -v_perd,
                'Desarme de ' || v_prod.nombre,
                v_rec || ' recuperadas, ' || v_perd || ' perdidas',
                p_usuario_id, 'desarme', v_merma_id
            );
        END IF;

        v_mermas   := v_mermas + 1;
        v_tot_rec  := v_tot_rec + v_rec;
        v_tot_perd := v_tot_perd + v_perd;
    END LOOP;

    INSERT INTO movimientos_inventario (
        producto_id, tipo, cantidad, motivo, detalle,
        usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, 'merma', -p_cantidad,
        'Desarme · ' || btrim(p_motivo),
        v_tot_rec || ' varas recuperadas de ' || (v_tot_rec + v_tot_perd),
        p_usuario_id, 'desarme', p_producto_id
    );

    RETURN QUERY
    SELECT p_producto_id, v_prod.nombre, p_cantidad, v_mermas, v_tot_rec, v_tot_perd;
END;
$function$;


-- ═══ Reversa ═══

CREATE OR REPLACE FUNCTION sp_mer_u_revertir(p_id integer, p_motivo text, p_usuario_id integer)
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
        v_m.producto_id, v_m.lote_id, 'ajuste', v_m.cantidad, v_stock,
        'Reversa de merma', v_motivo,
        p_usuario_id, 'merma_reversa', p_id
    );

    RETURN p_id;
END;
$function$;


-- ═══ Consultas con privacidad ═══
-- p_usuario_id: null para el administrador (todo); con un id, solo las
-- mermas que registró esa persona.

DROP FUNCTION IF EXISTS sp_mer_c_mermas(text, integer, integer, text, destino_merma, boolean, date, date, integer, integer);
DROP FUNCTION IF EXISTS sp_mer_c_mermas(text, integer, integer, text, destino_merma, boolean, date, date, integer, integer, integer);

CREATE FUNCTION sp_mer_c_mermas(
    p_buscar text DEFAULT NULL::text, p_producto_id integer DEFAULT NULL::integer,
    p_lote_id integer DEFAULT NULL::integer, p_motivo text DEFAULT NULL::text,
    p_destino destino_merma DEFAULT NULL::destino_merma,
    p_revertida boolean DEFAULT false,
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(id integer, producto_id integer, producto text, emoji text,
    lote_id integer, partida_id integer, origen_codigo text, origen text,
    cantidad integer, motivo text, detalle text,
    costo_unitario integer, costo_total integer, destino destino_merma,
    cantidad_recuperada integer, cantidad_perdida integer,
    calidad_reingreso calidad_reingreso, lote_recuperacion_id integer,
    lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer,
    escaneado boolean, autorizado_por text, usuario text,
    revertida boolean, revertida_por text, revertida_en timestamp with time zone,
    motivo_reversion text, desarme_grupo integer,
    creado_en timestamp with time zone, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        m.id,
        m.producto_id, p.nombre, p.emoji,
        m.lote_id, m.partida_id,
        COALESCE(l.codigo, pm.codigo),
        CASE WHEN m.partida_id IS NOT NULL    THEN 'mostrador'
             WHEN m.lote_id IS NOT NULL       THEN 'bodega'
             WHEN m.desarme_grupo IS NOT NULL THEN 'desarme'
             ELSE 'stock' END,
        m.cantidad, m.motivo, m.detalle,
        m.costo_unitario, m.costo_total,
        m.destino,
        m.cantidad_recuperada,
        m.cantidad - m.cantidad_recuperada,
        m.calidad_reingreso,
        m.lote_recuperacion_id, lr.codigo,
        m.costo_recuperado_unitario, m.costo_perdido,
        m.escaneado, m.autorizado_por,
        u.nombre,
        m.revertida, ur.nombre, m.revertida_en,
        m.motivo_reversion, m.desarme_grupo,
        m.creado_en,
        count(*) OVER ()
    FROM mermas m
    JOIN productos p       ON p.id = m.producto_id
    LEFT JOIN lotes l      ON l.id = m.lote_id
    LEFT JOIN partidas_mostrador pm ON pm.id = m.partida_id
    LEFT JOIN lotes lr     ON lr.id = m.lote_recuperacion_id
    LEFT JOIN usuarios u   ON u.id = m.usuario_id
    LEFT JOIN usuarios ur  ON ur.id = m.revertida_por
    WHERE (p_buscar IS NULL
           OR p.nombre ILIKE '%' || p_buscar || '%'
           OR m.motivo ILIKE '%' || p_buscar || '%'
           OR l.codigo ILIKE '%' || p_buscar || '%'
           OR pm.codigo ILIKE '%' || p_buscar || '%')
      AND (p_producto_id IS NULL OR m.producto_id = p_producto_id)
      AND (p_lote_id     IS NULL OR m.lote_id     = p_lote_id)
      AND (p_motivo      IS NULL OR m.motivo      = p_motivo)
      AND (p_destino     IS NULL OR m.destino     = p_destino)
      AND (p_revertida   IS NULL OR m.revertida   = p_revertida)
      AND (p_desde IS NULL OR m.creado_en >= p_desde)
      AND (p_hasta IS NULL OR m.creado_en <  p_hasta + 1)
      AND (p_usuario_id  IS NULL OR m.usuario_id  = p_usuario_id)
    ORDER BY m.creado_en DESC, m.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$;

-- Devuelve las mismas columnas que el listado: el modelo es uno solo, y
-- antes el detalle dejaba vacíos el origen y su código (el mensaje de
-- "Lote … dado de baja" salía sin el código).
DROP FUNCTION IF EXISTS sp_mer_c_merma(integer);
DROP FUNCTION IF EXISTS sp_mer_c_merma(integer, integer);

CREATE FUNCTION sp_mer_c_merma(p_id integer, p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(id integer, producto_id integer, producto text, emoji text,
    lote_id integer, partida_id integer, origen_codigo text, origen text,
    cantidad integer, motivo text, detalle text,
    costo_unitario integer, costo_total integer, destino destino_merma,
    cantidad_recuperada integer, cantidad_perdida integer,
    calidad_reingreso calidad_reingreso, lote_recuperacion_id integer,
    lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer,
    escaneado boolean, autorizado_por text, usuario text,
    revertida boolean, revertida_por text, revertida_en timestamp with time zone,
    motivo_reversion text, desarme_grupo integer,
    creado_en timestamp with time zone, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        m.id, m.producto_id, p.nombre, p.emoji,
        m.lote_id, m.partida_id,
        COALESCE(l.codigo, pm.codigo),
        CASE WHEN m.partida_id IS NOT NULL    THEN 'mostrador'
             WHEN m.lote_id IS NOT NULL       THEN 'bodega'
             WHEN m.desarme_grupo IS NOT NULL THEN 'desarme'
             ELSE 'stock' END,
        m.cantidad, m.motivo, m.detalle,
        m.costo_unitario, m.costo_total, m.destino,
        m.cantidad_recuperada, m.cantidad - m.cantidad_recuperada,
        m.calidad_reingreso, m.lote_recuperacion_id, lr.codigo,
        m.costo_recuperado_unitario, m.costo_perdido,
        m.escaneado, m.autorizado_por,
        u.nombre, m.revertida, ur.nombre, m.revertida_en,
        m.motivo_reversion, m.desarme_grupo,
        m.creado_en, 1::bigint
    FROM mermas m
    JOIN productos p       ON p.id = m.producto_id
    LEFT JOIN lotes l      ON l.id = m.lote_id
    LEFT JOIN partidas_mostrador pm ON pm.id = m.partida_id
    LEFT JOIN lotes lr     ON lr.id = m.lote_recuperacion_id
    LEFT JOIN usuarios u   ON u.id = m.usuario_id
    LEFT JOIN usuarios ur  ON ur.id = m.revertida_por
    WHERE m.id = p_id
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id);
$function$;


DROP FUNCTION IF EXISTS sp_mer_c_resumen(date, date);
DROP FUNCTION IF EXISTS sp_mer_c_resumen(date, date, integer);

CREATE FUNCTION sp_mer_c_resumen(
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(desde date, hasta date, registros bigint, unidades bigint,
    unidades_perdidas bigint, unidades_recuperadas bigint, costo_total bigint,
    costo_perdido bigint, costo_recuperado bigint, costo_botado bigint,
    costo_desvalorizado bigint, costo_devuelto bigint, ventas_periodo bigint,
    porcentaje_sobre_ventas numeric)
LANGUAGE sql
STABLE
AS $function$
    WITH rango AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE)      AS h
    ),
    m AS (
        SELECT me.*
        FROM mermas me, rango r
        WHERE NOT me.revertida
          AND me.creado_en >= r.d
          AND me.creado_en <  r.h + 1
          AND (p_usuario_id IS NULL OR me.usuario_id = p_usuario_id)
    ),
    -- Las ventas del local son del administrador. Para cualquier otro no
    -- se calculan: el porcentaje sobre ventas es un número del negocio,
    -- no de una persona.
    v AS (
        SELECT CASE WHEN p_usuario_id IS NULL
                    THEN COALESCE(sum(ve.total), 0) END AS vendido
        FROM ventas ve, rango r
        WHERE p_usuario_id IS NULL
          AND NOT ve.anulada
          AND ve.creado_en >= r.d
          AND ve.creado_en <  r.h + 1
    )
    SELECT
        r.d, r.h,
        (SELECT count(*) FROM m),
        (SELECT COALESCE(sum(cantidad), 0) FROM m),
        (SELECT COALESCE(sum(cantidad - cantidad_recuperada), 0) FROM m),
        (SELECT COALESCE(sum(cantidad_recuperada), 0) FROM m),
        (SELECT COALESCE(sum(costo_total), 0) FROM m),
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m),

        -- Lo que sigue valiendo algo porque volvió al stock rebajado.
        (SELECT COALESCE(sum(cantidad_recuperada * COALESCE(costo_recuperado_unitario, 0)), 0)
         FROM m),

        -- Se botó de verdad: no volvió nada.
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m WHERE destino = 'perdida'),

        -- Volvió pero vale menos: solo se perdió la rebaja.
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m WHERE destino = 'reingreso'),

        -- El proveedor lo abona: NO es costo, y contarlo como merma haría
        -- ver mal a quien compró bien.
        (SELECT COALESCE(sum(costo_total), 0) FROM m WHERE destino = 'devolucion_proveedor'),

        v.vendido,

        -- Sobre 5% en una florería es señal de que se compra más de lo que
        -- se alcanza a vender.
        CASE WHEN v.vendido > 0
             THEN ROUND((SELECT COALESCE(sum(costo_perdido), 0) FROM m)::numeric
                        / v.vendido * 100, 2)
             END
    FROM rango r, v;
$function$;


DROP FUNCTION IF EXISTS sp_mer_c_resumen_destino(date, date);
DROP FUNCTION IF EXISTS sp_mer_c_resumen_destino(date, date, integer);

CREATE FUNCTION sp_mer_c_resumen_destino(
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(destino destino_merma, registros bigint, unidades bigint, costo_perdido bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT m.destino, count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.destino
    ORDER BY 4 DESC;
$function$;


DROP FUNCTION IF EXISTS sp_mer_c_resumen_motivo(date, date);
DROP FUNCTION IF EXISTS sp_mer_c_resumen_motivo(date, date, integer);

CREATE FUNCTION sp_mer_c_resumen_motivo(
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(motivo text, registros bigint, unidades bigint, costo_perdido bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT m.motivo, count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.motivo
    ORDER BY 4 DESC
    LIMIT 10;
$function$;


DROP FUNCTION IF EXISTS sp_mer_c_resumen_producto(date, date);
DROP FUNCTION IF EXISTS sp_mer_c_resumen_producto(date, date, integer);

CREATE FUNCTION sp_mer_c_resumen_producto(
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(producto_id integer, producto text, emoji text, registros bigint,
    unidades bigint, unidades_recuperadas bigint, costo_perdido bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT m.producto_id, p.nombre, p.emoji,
           count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.cantidad_recuperada), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    JOIN productos p ON p.id = m.producto_id
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.producto_id, p.nombre, p.emoji
    ORDER BY 7 DESC
    LIMIT 15;
$function$;
