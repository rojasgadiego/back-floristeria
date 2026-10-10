-- =====================================================================
-- Inventario: conteo de bodega y vitrina, con historial
-- =====================================================================
-- Amplía el conteo de sql/22:
--
--   · Dónde: 'bodega' (productos sin lotes, flores balde por balde, y
--     flores SIN balde) o 'vitrina' (partidas del mostrador y ramos
--     armados listos). Quién puede contar qué lo decide la API: la
--     vendedora solo la vitrina.
--
--   · Flores sin balde: antes no aparecían (se cuentan por balde y no
--     tenían ninguno), así que no había cómo cargar las que ya estaban en
--     la tienda. Al contarlas se crea su lote, con vencimiento según sus
--     días de vida.
--
--   · Historial: cada conteo queda en `conteos` con sus líneas: quién,
--     cuándo, dónde y qué se contó, con lo que decía el sistema, lo que se
--     contó y la diferencia. Lo revisa la administración.
--
--   · Firma: los faltantes son mermas. Si suman más que el tope y quien
--     cuenta no es admin, hace falta el código por correo (lo verifica la
--     API y llega acá como p_autorizado_por). Si no, contar sería la forma
--     de esconder una pérdida grande sin foto ni firma.
--
-- Reemplaza sp_inv_c_conteo y sp_inv_i_conteo de sql/22.
--
-- Idempotente.
-- =====================================================================

CREATE TABLE IF NOT EXISTS conteos (
    id              serial PRIMARY KEY,
    ubicacion       text NOT NULL CHECK (ubicacion IN ('bodega', 'vitrina')),
    detalle         text,
    usuario_id      integer REFERENCES usuarios(id),
    autorizado_por  text,
    creado_en       timestamptz NOT NULL DEFAULT now(),
    lineas          integer NOT NULL DEFAULT 0,
    con_diferencia  integer NOT NULL DEFAULT 0,
    sobrantes       integer NOT NULL DEFAULT 0,
    faltantes       integer NOT NULL DEFAULT 0,
    valor_sobrante  integer NOT NULL DEFAULT 0,
    valor_faltante  integer NOT NULL DEFAULT 0
);

CREATE INDEX IF NOT EXISTS ix_conteos_fecha ON conteos (creado_en DESC);

CREATE TABLE IF NOT EXISTS conteo_lineas (
    id              serial PRIMARY KEY,
    conteo_id       integer NOT NULL REFERENCES conteos(id),
    producto_id     integer NOT NULL REFERENCES productos(id),
    lote_id         integer REFERENCES lotes(id),
    partida_id      integer REFERENCES partidas_mostrador(id),
    -- Foto del momento: el código del balde o la partida, o "sin balde".
    origen          text,
    sistema         integer NOT NULL,
    contado         integer NOT NULL CHECK (contado >= 0),
    diferencia      integer NOT NULL,
    costo_unitario  integer NOT NULL DEFAULT 0,
    merma_id        integer REFERENCES mermas(id)
);

CREATE INDEX IF NOT EXISTS ix_conteo_lineas_conteo ON conteo_lineas (conteo_id);


-- ═══ La planilla ═══

DROP FUNCTION IF EXISTS sp_inv_c_conteo(integer);
DROP FUNCTION IF EXISTS sp_inv_c_conteo(text, integer);

CREATE FUNCTION sp_inv_c_conteo(p_ubicacion text DEFAULT 'bodega', p_categoria_id integer DEFAULT NULL)
RETURNS TABLE (
    tipo_fila         text,     -- producto · balde · sin_balde · partida · armado
    producto_id       integer,
    producto          text,
    emoji             text,
    categoria_id      integer,
    categoria         text,
    categoria_orden   integer,
    controla_lotes    boolean,
    lote_id           integer,
    partida_id        integer,
    origen            text,
    fecha_vencimiento date,
    sistema           integer,
    costo_unitario    integer
)
LANGUAGE sql
STABLE
AS $$
    -- ─── Bodega ───
    SELECT 'producto', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, false,
           NULL::int, NULL::int, NULL::text, NULL::date,
           COALESCE(p.stock, 0), fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND NOT p.controla_lotes
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    UNION ALL
    SELECT 'balde', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, true,
           l.id, NULL::int, l.codigo, l.fecha_vencimiento,
           l.varas_disponibles, ROUND(l.costo_por_vara)::int
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
      JOIN lotes l ON l.producto_id = p.id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND p.controla_lotes
       AND l.estado = 'activo' AND l.varas_disponibles > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    -- Flores sin ningún balde activo: una fila para poder cargarlas.
    UNION ALL
    SELECT 'sin_balde', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, true,
           NULL::int, NULL::int, 'sin balde', NULL::date,
           0, fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND p.controla_lotes
       AND NOT EXISTS (SELECT 1 FROM lotes l
                        WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0)
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    -- ─── Vitrina ───
    UNION ALL
    SELECT 'partida', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, p.controla_lotes,
           NULL::int, pm.id, pm.codigo, pm.fecha_vencimiento,
           pm.cantidad_disponible, ROUND(COALESCE(pm.costo_por_vara, 0))::int
      FROM partidas_mostrador pm
      JOIN productos p ON p.id = pm.producto_id
      JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'vitrina' AND pm.cantidad_disponible > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    UNION ALL
    SELECT 'armado', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, false,
           NULL::int, NULL::int, NULL::text, NULL::date,
           COALESCE(p.stock_listo, 0), fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'vitrina'
       AND p.tipo = 'armado' AND p.activo AND COALESCE(p.stock_listo, 0) > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    ORDER BY 7, 6, 3, 11 NULLS FIRST;
$$;


-- ═══ Aplicar el conteo ═══
--
-- p_items: [{ productoId, loteId, partidaId, contado }]
--   · loteId = balde · partidaId = partida del mostrador
--   · flor sin balde: sin loteId (si contado > 0 se le crea su lote)
--   · armado: sin loteId ni partidaId, en la vitrina
-- Todo o nada.

DROP FUNCTION IF EXISTS sp_inv_i_conteo(jsonb, text, integer, text);

CREATE OR REPLACE FUNCTION sp_inv_i_conteo(
    p_ubicacion      text,
    p_items          jsonb,
    p_detalle        text    DEFAULT NULL,
    p_usuario_id     integer DEFAULT NULL,
    p_autorizado_por text    DEFAULT NULL
)
RETURNS integer
LANGUAGE plpgsql
AS $$
DECLARE
    it         jsonb;
    v_n        int := 0;
    v_conteo   int;
    v_prod     record;
    v_lote     record;
    v_part     record;
    v_lote_id  int;
    v_part_id  int;
    v_contado  int;
    v_sistema  int;
    v_dif      int;
    v_stock    int;
    v_merma    int;
    v_origen   text;
    v_costo    int;
    v_codigo   text;
    v_firma    text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
    v_valor    int;
    v_umbral   int;
    v_detalle  text := COALESCE(nullif(btrim(coalesce(p_detalle, '')), ''), 'Conteo de stock');
BEGIN
    IF p_ubicacion NOT IN ('bodega', 'vitrina') THEN
        RAISE EXCEPTION 'Indica si el conteo es de bodega o de vitrina.';
    END IF;

    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'No hay nada contado.';
    END IF;

    INSERT INTO conteos (ubicacion, detalle, usuario_id, autorizado_por)
    VALUES (p_ubicacion, v_detalle, p_usuario_id, v_firma)
    RETURNING id INTO v_conteo;

    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n       := v_n + 1;
        v_lote_id := (it->>'loteId')::int;
        v_part_id := (it->>'partidaId')::int;
        v_contado := (it->>'contado')::int;
        v_merma   := NULL;
        v_origen  := NULL;

        IF v_contado IS NULL OR v_contado < 0 THEN
            RAISE EXCEPTION 'Línea %: lo contado no puede ser negativo.', v_n;
        END IF;

        SELECT * INTO v_prod FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
        END IF;

        -- ─── Lo que dice el sistema, AHORA ───
        IF v_part_id IS NOT NULL THEN
            IF p_ubicacion <> 'vitrina' THEN
                RAISE EXCEPTION 'Las partidas del mostrador se cuentan en la vitrina.';
            END IF;

            SELECT * INTO v_part FROM partidas_mostrador WHERE id = v_part_id FOR UPDATE;
            IF NOT FOUND OR v_part.producto_id <> v_prod.id THEN
                RAISE EXCEPTION 'Línea %: esa partida no es de %.', v_n, v_prod.nombre;
            END IF;

            v_sistema := v_part.cantidad_disponible;
            v_origen  := v_part.codigo;
            v_costo   := ROUND(COALESCE(v_part.costo_por_vara, 0));

        ELSIF v_prod.tipo = 'armado' THEN
            IF p_ubicacion <> 'vitrina' THEN
                RAISE EXCEPTION '% es un ramo armado: se cuenta en la vitrina.', v_prod.nombre;
            END IF;

            v_sistema := COALESCE(v_prod.stock_listo, 0);
            v_costo   := fn_mer_costo_ref(v_prod.id);

        ELSE
            IF p_ubicacion <> 'bodega' THEN
                RAISE EXCEPTION '% se cuenta en bodega.', v_prod.nombre;
            END IF;

            IF v_prod.controla_lotes AND v_lote_id IS NOT NULL THEN
                SELECT * INTO v_lote FROM lotes WHERE id = v_lote_id FOR UPDATE;
                IF NOT FOUND OR v_lote.producto_id <> v_prod.id THEN
                    RAISE EXCEPTION 'Línea %: ese balde no es de %.', v_n, v_prod.nombre;
                END IF;

                v_sistema := v_lote.varas_disponibles;
                v_origen  := v_lote.codigo;
                v_costo   := ROUND(v_lote.costo_por_vara);

            ELSIF v_prod.controla_lotes THEN
                -- Flor sin balde: el sistema dice 0 en un balde que aún no existe.
                v_sistema := 0;
                v_origen  := 'sin balde';
                v_costo   := fn_mer_costo_ref(v_prod.id);

            ELSE
                IF v_lote_id IS NOT NULL THEN
                    RAISE EXCEPTION '% no se maneja por lotes.', v_prod.nombre;
                END IF;

                v_sistema := COALESCE(v_prod.stock, 0);
                v_costo   := fn_mer_costo_ref(v_prod.id);
            END IF;
        END IF;

        v_dif := v_contado - v_sistema;

        IF v_dif > 0 THEN
            -- ─── Sobrante: entra donde se contó ───
            IF v_part_id IS NOT NULL THEN
                UPDATE partidas_mostrador
                   SET cantidad_disponible = cantidad_disponible + v_dif, agotada_en = NULL
                 WHERE id = v_part_id;
                v_stock := NULL;

            ELSIF v_prod.tipo = 'armado' THEN
                UPDATE productos SET stock_listo = COALESCE(stock_listo, 0) + v_dif, actualizado_en = now()
                 WHERE id = v_prod.id RETURNING stock_listo INTO v_stock;

            ELSE
                IF v_prod.controla_lotes AND v_lote_id IS NULL THEN
                    -- El balde nace acá: con vencimiento por sus días de vida y
                    -- sin precio propio (toma el de la ficha al bajar al mesón).
                    v_codigo := fn_abs_codigo_lote();
                    INSERT INTO lotes (
                        codigo, producto_id, fecha_ingreso, fecha_vencimiento,
                        varas_iniciales, varas_disponibles, costo_por_vara, estado, notas
                    )
                    VALUES (
                        v_codigo, v_prod.id, CURRENT_DATE,
                        CASE WHEN v_prod.dias_vida IS NOT NULL THEN CURRENT_DATE + v_prod.dias_vida END,
                        v_dif, v_dif, v_costo, 'activo', 'Creado en conteo · ' || v_detalle
                    )
                    RETURNING id INTO v_lote_id;
                    v_origen := v_codigo;

                ELSIF v_prod.controla_lotes THEN
                    UPDATE lotes
                       SET varas_disponibles = varas_disponibles + v_dif,
                           estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
                           actualizado_en = now()
                     WHERE id = v_lote_id;
                END IF;

                UPDATE productos SET stock = COALESCE(stock, 0) + v_dif, actualizado_en = now()
                 WHERE id = v_prod.id RETURNING stock INTO v_stock;
            END IF;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                v_prod.id, v_lote_id, 'ajuste', v_dif, v_stock,
                'Conteo de stock', 'Sobrante en ' || p_ubicacion || ' · ' || v_detalle,
                p_usuario_id, 'conteo', v_conteo
            );

        ELSIF v_dif < 0 THEN
            -- ─── Faltante: es una merma, con el mismo registro que cualquiera ───
            v_merma := sp_mer_i_merma(
                v_prod.id, v_lote_id, v_part_id, -v_dif,
                'Faltante en conteo', v_detalle || ' (' || p_ubicacion || ')',
                'perdida', 0, NULL,
                NULL, false, v_firma, p_usuario_id);
        END IF;

        INSERT INTO conteo_lineas (
            conteo_id, producto_id, lote_id, partida_id, origen,
            sistema, contado, diferencia, costo_unitario, merma_id
        )
        VALUES (
            v_conteo, v_prod.id, v_lote_id, v_part_id, v_origen,
            v_sistema, v_contado, v_dif, COALESCE(v_costo, 0), v_merma
        );
    END LOOP;

    -- ─── Una firma por el total de faltantes ───
    SELECT COALESCE(sum(m.valor_autorizacion), 0) INTO v_valor
      FROM conteo_lineas cl JOIN mermas m ON m.id = cl.merma_id
     WHERE cl.conteo_id = v_conteo;

    v_umbral := fn_mer_umbral_autorizacion();

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Los faltantes suman $% y necesitan autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    -- ─── El resumen, para el historial ───
    UPDATE conteos c SET
        lineas         = s.lineas,
        con_diferencia = s.con_dif,
        sobrantes      = s.sobrantes,
        faltantes      = s.faltantes,
        valor_sobrante = s.v_sob,
        valor_faltante = s.v_fal
    FROM (
        SELECT count(*) AS lineas,
               count(*) FILTER (WHERE diferencia <> 0) AS con_dif,
               COALESCE(sum(diferencia) FILTER (WHERE diferencia > 0), 0) AS sobrantes,
               COALESCE(-sum(diferencia) FILTER (WHERE diferencia < 0), 0) AS faltantes,
               COALESCE(sum(diferencia * costo_unitario) FILTER (WHERE diferencia > 0), 0) AS v_sob,
               COALESCE(-sum(diferencia * costo_unitario) FILTER (WHERE diferencia < 0), 0) AS v_fal
          FROM conteo_lineas WHERE conteo_id = v_conteo
    ) s
    WHERE c.id = v_conteo;

    RETURN v_conteo;
END;
$$;


-- ═══ Historial ═══

CREATE OR REPLACE FUNCTION sp_inv_c_conteos(p_limite integer DEFAULT 100)
RETURNS TABLE (
    id integer, ubicacion text, detalle text, usuario text, autorizado_por text,
    creado_en timestamptz, lineas integer, con_diferencia integer,
    sobrantes integer, faltantes integer, valor_sobrante integer, valor_faltante integer
)
LANGUAGE sql
STABLE
AS $$
    SELECT c.id, c.ubicacion, c.detalle, u.nombre, c.autorizado_por, c.creado_en,
           c.lineas, c.con_diferencia, c.sobrantes, c.faltantes, c.valor_sobrante, c.valor_faltante
      FROM conteos c
      LEFT JOIN usuarios u ON u.id = c.usuario_id
     ORDER BY c.creado_en DESC
     LIMIT GREATEST(COALESCE(p_limite, 100), 1);
$$;

CREATE OR REPLACE FUNCTION sp_inv_c_conteo_lineas(p_conteo_id integer)
RETURNS TABLE (
    producto_id integer, producto text, emoji text, categoria text, origen text,
    sistema integer, contado integer, diferencia integer, costo_unitario integer, merma_id integer
)
LANGUAGE sql
STABLE
AS $$
    SELECT cl.producto_id, p.nombre, p.emoji, c.nombre, cl.origen,
           cl.sistema, cl.contado, cl.diferencia, cl.costo_unitario, cl.merma_id
      FROM conteo_lineas cl
      JOIN productos p ON p.id = cl.producto_id
      JOIN categorias c ON c.id = p.categoria_id
     WHERE cl.conteo_id = p_conteo_id
     ORDER BY (cl.diferencia = 0), c.orden, p.nombre, cl.origen;
$$;
