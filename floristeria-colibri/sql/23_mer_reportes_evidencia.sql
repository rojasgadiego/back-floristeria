-- =====================================================================
-- Mermas: reportes con evidencia, incidentes y deshacer
-- =====================================================================
-- Cada registro pasa a ser un REPORTE con una o varias líneas (cada línea
-- es una fila de `mermas`, como siempre):
--   · puntual:   una cosa (se marchitó, se quebró);
--   · incidente: varias a la vez (temblor, corte de luz, cae un estante),
--                con un solo motivo, unas mismas fotos y UNA autorización
--                por el total.
--
-- Evidencia: toda merma lleva al menos una foto (máximo 3). Se guarda acá,
-- en bytea —el binario tal cual; base64 pesaría un 33% más— en una tabla
-- aparte para no cargar los listados. Viaja en el mismo respaldo que la
-- merma, y su huella SHA-256 permite ver si la misma foto se usó dos veces.
--
-- Deshacer: quien registró puede deshacer su reporte durante 10 minutos,
-- si nadie tocó esas varas. Después no hay reversa: lo que quede mal se
-- corrige con un conteo (sql/22). sp_mer_u_revertir queda solo como pieza
-- interna de esto.
--
-- Vitrina: con p_solo_vitrina (la vendedora) cada línea tiene que salir de
-- una partida del mostrador o ser un armado (los ramos están en vitrina).
--
-- Idempotente.
-- =====================================================================

CREATE TABLE IF NOT EXISTS merma_reportes (
    id              serial PRIMARY KEY,
    tipo            text NOT NULL CHECK (tipo IN ('puntual', 'incidente')),
    motivo_id       integer REFERENCES motivos_merma(id),
    detalle         text,
    usuario_id      integer REFERENCES usuarios(id),
    autorizado_por  text,
    creado_en       timestamptz NOT NULL DEFAULT now(),
    deshecho_en     timestamptz,
    deshecho_por    integer REFERENCES usuarios(id)
);

ALTER TABLE mermas ADD COLUMN IF NOT EXISTS reporte_id integer REFERENCES merma_reportes(id);
CREATE INDEX IF NOT EXISTS ix_mermas_reporte ON mermas (reporte_id);

-- Con qué valor se decidió si pedía firma (a costo, o a precio de venta si
-- no había costo; ver sql/15). El reporte suma esto para su firma única.
ALTER TABLE mermas ADD COLUMN IF NOT EXISTS valor_autorizacion integer;

CREATE TABLE IF NOT EXISTS merma_evidencias (
    id          serial PRIMARY KEY,
    reporte_id  integer NOT NULL REFERENCES merma_reportes(id),
    foto        bytea NOT NULL,
    tipo_mime   text NOT NULL DEFAULT 'image/jpeg'
                CHECK (tipo_mime IN ('image/jpeg', 'image/png', 'image/webp')),
    bytes       integer GENERATED ALWAYS AS (octet_length(foto)) STORED,
    sha256      text NOT NULL,
    creado_en   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT merma_evidencias_tamano CHECK (octet_length(foto) BETWEEN 1 AND 5242880)
);

CREATE INDEX IF NOT EXISTS ix_merma_evidencias_reporte ON merma_evidencias (reporte_id);
CREATE INDEX IF NOT EXISTS ix_merma_evidencias_sha ON merma_evidencias (sha256);

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
        codigo_escaneado, escaneado, autorizado_por, valor_autorizacion
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
        nullif(btrim(coalesce(p_autorizado_por, '')), ''),
        v_valor_aut
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

DROP FUNCTION IF EXISTS sp_mer_c_mermas(text, integer, integer, text, destino_merma, boolean, date, date, integer, integer, integer);
CREATE FUNCTION public.sp_mer_c_mermas(p_buscar text DEFAULT NULL::text, p_producto_id integer DEFAULT NULL::integer, p_lote_id integer DEFAULT NULL::integer, p_motivo text DEFAULT NULL::text, p_destino destino_merma DEFAULT NULL::destino_merma, p_revertida boolean DEFAULT false, p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50, p_usuario_id integer DEFAULT NULL::integer)
 RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, partida_id integer, origen_codigo text, origen text, cantidad integer, motivo text, detalle text, costo_unitario integer, costo_total integer, destino destino_merma, cantidad_recuperada integer, cantidad_perdida integer, calidad_reingreso calidad_reingreso, lote_recuperacion_id integer, lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer, escaneado boolean, autorizado_por text, usuario text, revertida boolean, revertida_por text, revertida_en timestamp with time zone, motivo_reversion text, desarme_grupo integer, reporte_id integer, fotos integer, usuario_id integer, creado_en timestamp with time zone, total_filas bigint)
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
        m.reporte_id,
        (SELECT count(*)::int FROM merma_evidencias e WHERE e.reporte_id = m.reporte_id),
        m.usuario_id,
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
$function$
;

DROP FUNCTION IF EXISTS sp_mer_c_merma(integer, integer);
CREATE FUNCTION public.sp_mer_c_merma(p_id integer, p_usuario_id integer DEFAULT NULL::integer)
 RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, partida_id integer, origen_codigo text, origen text, cantidad integer, motivo text, detalle text, costo_unitario integer, costo_total integer, destino destino_merma, cantidad_recuperada integer, cantidad_perdida integer, calidad_reingreso calidad_reingreso, lote_recuperacion_id integer, lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer, escaneado boolean, autorizado_por text, usuario text, revertida boolean, revertida_por text, revertida_en timestamp with time zone, motivo_reversion text, desarme_grupo integer, reporte_id integer, fotos integer, usuario_id integer, creado_en timestamp with time zone, total_filas bigint)
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
        m.reporte_id,
        (SELECT count(*)::int FROM merma_evidencias e WHERE e.reporte_id = m.reporte_id),
        m.usuario_id,
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
$function$
;


-- ═══ Registrar un reporte ═══
--
-- p_lineas: [{ productoId, loteId, partidaId, cantidad, destino,
--              cantidadRecuperada, calidad, codigoEscaneado, escaneado }]
-- Todo o nada: si una línea falla, no queda nada.

CREATE OR REPLACE FUNCTION sp_mer_i_reporte(
    p_tipo            text,
    p_lineas          jsonb,
    p_motivo          text,
    p_detalle         text,
    p_fotos           bytea[],
    p_tipos_foto      text[],
    p_solo_vitrina    boolean DEFAULT false,
    p_autorizado_por  text    DEFAULT NULL,
    p_usuario_id      integer DEFAULT NULL
)
RETURNS TABLE (reporte_id integer, mermas integer, valor integer, foto_repetida boolean)
LANGUAGE plpgsql
AS $$
DECLARE
    it        jsonb;
    v_n       int := 0;
    v_rep     int;
    v_mot     motivos_merma;
    v_merma   int;
    v_prod    record;
    v_fotos   int := COALESCE(array_length(p_fotos, 1), 0);
    v_valor   int;
    v_umbral  int;
    v_firma   text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
    v_repe    boolean := false;
    v_sha     text;
BEGIN
    IF p_tipo NOT IN ('puntual', 'incidente') THEN
        RAISE EXCEPTION 'Tipo de reporte desconocido: %.', p_tipo;
    END IF;

    IF p_lineas IS NULL OR jsonb_array_length(p_lineas) = 0 THEN
        RAISE EXCEPTION 'Indica qué se dañó.';
    END IF;

    IF p_tipo = 'puntual' AND jsonb_array_length(p_lineas) > 1 THEN
        RAISE EXCEPTION 'Una merma puntual es de una sola cosa. Para varias, registra un incidente.';
    END IF;

    -- La foto es la evidencia: sin ella, "se marchitó" no se puede revisar.
    IF v_fotos < 1 THEN
        RAISE EXCEPTION 'Agrega al menos una foto de lo que se dañó.';
    END IF;

    IF v_fotos > 3 THEN
        RAISE EXCEPTION 'Son máximo 3 fotos por reporte.';
    END IF;

    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    INSERT INTO merma_reportes (tipo, motivo_id, detalle, usuario_id, autorizado_por)
    VALUES (p_tipo, v_mot.id, nullif(btrim(coalesce(p_detalle, '')), ''), p_usuario_id, v_firma)
    RETURNING id INTO v_rep;

    -- ─── Las líneas ───
    FOR it IN SELECT * FROM jsonb_array_elements(p_lineas)
    LOOP
        v_n := v_n + 1;

        IF p_solo_vitrina THEN
            SELECT tipo, nombre INTO v_prod FROM productos WHERE id = (it->>'productoId')::int;

            IF (it->>'partidaId') IS NULL AND v_prod.tipo IS DISTINCT FROM 'armado' THEN
                RAISE EXCEPTION
                    'Línea %: desde la vitrina se registra lo del mostrador (partidas PAR-…) o un ramo armado. Lo de bodega lo registra bodega.',
                    v_n;
            END IF;
        END IF;

        -- Cada línea pasa por la misma función que una merma suelta: mismo
        -- costo, mismas reglas de lote, partida, recuperación y firma.
        v_merma := sp_mer_i_merma(
            (it->>'productoId')::int,
            (it->>'loteId')::int,
            (it->>'partidaId')::int,
            (it->>'cantidad')::int,
            v_mot.nombre, p_detalle,
            COALESCE(nullif(it->>'destino', ''), 'perdida')::destino_merma,
            COALESCE((it->>'cantidadRecuperada')::int, 0),
            nullif(it->>'calidad', '')::calidad_reingreso,
            it->>'codigoEscaneado',
            COALESCE((it->>'escaneado')::boolean, false),
            v_firma, p_usuario_id);

        UPDATE mermas SET reporte_id = v_rep WHERE id = v_merma;
    END LOOP;

    -- ─── Una firma por el total ───
    -- Cada línea ya exigió firma si sola pasaba el tope; acá se mira la
    -- suma, que es lo que un incidente puede escalar de a poco.
    SELECT COALESCE(sum(m.valor_autorizacion), 0) INTO v_valor
      FROM mermas m WHERE m.reporte_id = v_rep;

    v_umbral := fn_mer_umbral_autorizacion();

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Este reporte suma $% y necesita autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    -- ─── Las fotos ───
    FOR i IN 1 .. v_fotos LOOP
        v_sha := encode(sha256(p_fotos[i]), 'hex');

        IF EXISTS (SELECT 1 FROM merma_evidencias e WHERE e.sha256 = v_sha) THEN
            v_repe := true;
        END IF;

        INSERT INTO merma_evidencias (reporte_id, foto, tipo_mime, sha256)
        VALUES (v_rep, p_fotos[i], COALESCE(p_tipos_foto[i], 'image/jpeg'), v_sha);
    END LOOP;

    RETURN QUERY SELECT v_rep, v_n, v_valor, v_repe;
END;
$$;


-- ═══ Deshacer ═══
-- Solo quien registró, dentro de 10 minutos, y solo si nadie tocó esas
-- varas (eso lo valida sp_mer_u_revertir línea por línea).

CREATE OR REPLACE FUNCTION sp_mer_u_deshacer_reporte(p_reporte_id integer, p_usuario_id integer)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    v_rep  merma_reportes;
    r      record;
BEGIN
    SELECT * INTO v_rep FROM merma_reportes WHERE id = p_reporte_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El reporte no existe.';
    END IF;

    IF v_rep.deshecho_en IS NOT NULL THEN
        RAISE EXCEPTION 'Este reporte ya se deshizo.';
    END IF;

    IF v_rep.usuario_id IS DISTINCT FROM p_usuario_id THEN
        RAISE EXCEPTION 'Solo quien registró la merma puede deshacerla.';
    END IF;

    IF v_rep.creado_en < now() - interval '10 minutes' THEN
        RAISE EXCEPTION 'Pasaron más de 10 minutos: ya no se puede deshacer. Si quedó mal, se corrige con un conteo.';
    END IF;

    FOR r IN SELECT m.id FROM mermas m WHERE m.reporte_id = p_reporte_id AND NOT m.revertida ORDER BY m.id
    LOOP
        PERFORM sp_mer_u_revertir(r.id, 'Deshecha por quien la registró', p_usuario_id);
    END LOOP;

    UPDATE merma_reportes
       SET deshecho_en = now(), deshecho_por = p_usuario_id
     WHERE id = p_reporte_id;

    RETURN p_reporte_id;
END;
$$;


-- ═══ Ver la evidencia ═══

CREATE OR REPLACE FUNCTION sp_mer_c_evidencias(p_reporte_id integer)
RETURNS TABLE (id integer, tipo_mime text, bytes integer, sha256 text, repetida boolean, creado_en timestamptz)
LANGUAGE sql
STABLE
AS $$
    SELECT e.id, e.tipo_mime, e.bytes, e.sha256,
           EXISTS (SELECT 1 FROM merma_evidencias o WHERE o.sha256 = e.sha256 AND o.id <> e.id),
           e.creado_en
      FROM merma_evidencias e
     WHERE e.reporte_id = p_reporte_id
     ORDER BY e.id;
$$;

CREATE OR REPLACE FUNCTION sp_mer_c_evidencia(p_id integer)
RETURNS TABLE (foto bytea, tipo_mime text, reporte_id integer, usuario_id integer)
LANGUAGE sql
STABLE
AS $$
    SELECT e.foto, e.tipo_mime, e.reporte_id, r.usuario_id
      FROM merma_evidencias e
      JOIN merma_reportes r ON r.id = e.reporte_id
     WHERE e.id = p_id;
$$;
