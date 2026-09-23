-- =====================================================================
-- Mermas: catálogo de motivos administrable, por categoría
-- =====================================================================
-- Los motivos estaban escritos dentro de sp_mer_c_motivos (8 fijos) y no
-- había cómo agregar "Hongos", "Temblor" o "No se alcanzó a vender". Peor:
-- "marchita por mal cuidado" y "no se vendió a tiempo" caían en el mismo
-- saco, y son problemas distintos (manejo vs. sobrecompra).
--
-- Ahora viven en motivos_merma, agrupados por categoría, y la
-- administradora los edita desde la app. Cada merma guarda el motivo_id
-- además del texto: el texto queda como foto del momento, el id permite
-- agrupar aunque después se renombre el motivo.
--
-- Un motivo puede exigir detalle (lo que no se entiende sin contexto:
-- "Otro", "Robo", "Siniestro") y sugerir un destino ("Llegó en mal estado"
-- → devolución al proveedor).
--
-- Idempotente.
-- =====================================================================

-- ═══ El catálogo ═══

CREATE TABLE IF NOT EXISTS motivos_merma (
    id               serial PRIMARY KEY,
    nombre           text NOT NULL,
    categoria        text NOT NULL,
    requiere_detalle boolean NOT NULL DEFAULT false,
    destino_sugerido destino_merma,
    activo           boolean NOT NULL DEFAULT true,
    orden            integer NOT NULL DEFAULT 100,
    creado_en        timestamptz NOT NULL DEFAULT now(),
    actualizado_en   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT motivos_merma_nombre_no_vacio CHECK (length(btrim(nombre)) > 0),
    CONSTRAINT motivos_merma_categoria_valida CHECK (categoria IN
        ('natural', 'accidente', 'operacional', 'proveedor', 'comercial', 'faltante', 'otro'))
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_motivos_merma_nombre ON motivos_merma (lower(btrim(nombre)));

INSERT INTO motivos_merma (nombre, categoria, requiere_detalle, destino_sugerido, orden) VALUES
    -- La flor hizo lo suyo
    ('Marchita',                         'natural',     false, NULL, 10),
    ('Deshidratada',                     'natural',     false, NULL, 20),
    ('Hongos o plaga',                   'natural',     false, NULL, 30),
    -- Algo le pasó
    ('Quebrada',                         'accidente',   false, NULL, 10),
    ('Golpeada',                         'accidente',   false, NULL, 20),
    ('Caída o accidente',                'accidente',   false, NULL, 30),
    ('Siniestro (temblor, inundación)',  'accidente',   true,  NULL, 40),
    -- Algo falló en el local
    ('Falla de refrigeración o corte de luz', 'operacional', true, NULL, 10),
    ('Sobrante de armado',               'operacional', false, NULL, 20),
    -- Vino mal
    ('Llegó en mal estado',              'proveedor',   false, 'devolucion_proveedor', 10),
    -- Decisiones del negocio
    ('No se alcanzó a vender',           'comercial',   false, NULL, 10),
    ('Reclamo o reposición a cliente',   'comercial',   true,  NULL, 20),
    ('Regalo o cortesía',                'comercial',   false, NULL, 30),
    ('Uso interno (vitrina, decoración)', 'comercial',  false, NULL, 40),
    -- No está y no se sabe por qué
    ('Faltante en conteo',               'faltante',    true,  NULL, 10),
    ('Robo',                             'faltante',    true,  NULL, 20),
    ('Otro',                             'otro',        true,  NULL, 10)
ON CONFLICT DO NOTHING;

-- "Error de digitación" no es una merma: para eso está Revertir. Queda en
-- el catálogo apagado, para que lo histórico siga teniendo a qué apuntar.
INSERT INTO motivos_merma (nombre, categoria, activo, orden)
VALUES ('Error de digitación', 'otro', false, 90)
ON CONFLICT DO NOTHING;

-- Lo que ya se hubiera registrado con un texto que no está en el catálogo
-- entra como motivo apagado de categoría "otro": nada queda huérfano.
INSERT INTO motivos_merma (nombre, categoria, activo, orden)
SELECT DISTINCT ON (lower(btrim(m.motivo))) btrim(m.motivo), 'otro', false, 99
  FROM mermas m
 WHERE NOT EXISTS (SELECT 1 FROM motivos_merma mm
                    WHERE lower(btrim(mm.nombre)) = lower(btrim(m.motivo)))
ON CONFLICT DO NOTHING;

ALTER TABLE mermas ADD COLUMN IF NOT EXISTS motivo_id integer REFERENCES motivos_merma(id);

UPDATE mermas m
   SET motivo_id = mm.id
  FROM motivos_merma mm
 WHERE m.motivo_id IS NULL
   AND lower(btrim(mm.nombre)) = lower(btrim(m.motivo));


-- ═══ Validar el motivo ═══
-- Una sola regla para la merma suelta, el descarte de un lote y el
-- desarme. Devuelve el id; el texto que se guarda es el del catálogo, así
-- "marchita" y "Marchita" no terminan siendo dos filas en el reporte.

CREATE OR REPLACE FUNCTION fn_mer_motivo(p_motivo text, p_detalle text)
RETURNS motivos_merma
LANGUAGE plpgsql
STABLE
AS $function$
DECLARE
    v motivos_merma;
BEGIN
    SELECT * INTO v
      FROM motivos_merma
     WHERE lower(btrim(nombre)) = lower(btrim(coalesce(p_motivo, '')));

    IF NOT FOUND OR NOT v.activo THEN
        RAISE EXCEPTION 'El motivo "%" no está en la lista. Elige uno de la lista o usa "Otro".',
            btrim(coalesce(p_motivo, ''));
    END IF;

    IF v.requiere_detalle AND btrim(coalesce(p_detalle, '')) = '' THEN
        RAISE EXCEPTION 'Con el motivo "%" hay que contar qué pasó en el detalle.', v.nombre;
    END IF;

    RETURN v;
END;
$function$;


-- ═══ El catálogo desde la app ═══

DROP FUNCTION IF EXISTS sp_mer_c_motivos();
DROP FUNCTION IF EXISTS sp_mer_c_motivos(boolean);

-- Los más usados primero dentro de cada categoría: es lo que la persona
-- busca. p_todos incluye los apagados (la pantalla de administración).
CREATE FUNCTION sp_mer_c_motivos(p_todos boolean DEFAULT false)
RETURNS TABLE(id integer, motivo text, categoria text, requiere_detalle boolean,
              destino_sugerido destino_merma, activo boolean, orden integer, usos bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT mm.id, mm.nombre, mm.categoria, mm.requiere_detalle,
           mm.destino_sugerido, mm.activo, mm.orden,
           (SELECT count(*) FROM mermas m
             WHERE m.motivo_id = mm.id AND NOT m.revertida
               AND m.creado_en > now() - interval '1 year')
    FROM motivos_merma mm
    WHERE p_todos OR mm.activo
    ORDER BY CASE mm.categoria
                 WHEN 'natural' THEN 1 WHEN 'accidente' THEN 2
                 WHEN 'operacional' THEN 3 WHEN 'proveedor' THEN 4
                 WHEN 'comercial' THEN 5 WHEN 'faltante' THEN 6 ELSE 7 END,
             mm.orden, mm.nombre;
$function$;

CREATE OR REPLACE FUNCTION sp_mer_i_motivo(
    p_nombre text, p_categoria text,
    p_requiere_detalle boolean DEFAULT false,
    p_destino_sugerido destino_merma DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_id int;
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Indica el nombre del motivo.';
    END IF;

    IF EXISTS (SELECT 1 FROM motivos_merma
                WHERE lower(btrim(nombre)) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ya existe el motivo "%". Si está apagado, actívalo.', btrim(p_nombre);
    END IF;

    INSERT INTO motivos_merma (nombre, categoria, requiere_detalle, destino_sugerido)
    VALUES (btrim(p_nombre), p_categoria, COALESCE(p_requiere_detalle, false), p_destino_sugerido)
    RETURNING id INTO v_id;

    RETURN v_id;
EXCEPTION
    WHEN check_violation THEN
        RAISE EXCEPTION 'Categoría desconocida: %.', p_categoria;
END;
$function$;

-- Renombrar no toca lo registrado: cada merma guarda el texto con que se
-- anotó. Apagar un motivo lo saca de la lista sin borrar su historia.
CREATE OR REPLACE FUNCTION sp_mer_u_motivo(
    p_id integer, p_nombre text, p_categoria text,
    p_requiere_detalle boolean, p_destino_sugerido destino_merma,
    p_activo boolean, p_orden integer DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
AS $function$
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Indica el nombre del motivo.';
    END IF;

    IF EXISTS (SELECT 1 FROM motivos_merma
                WHERE id <> p_id AND lower(btrim(nombre)) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ya existe otro motivo llamado "%".', btrim(p_nombre);
    END IF;

    UPDATE motivos_merma
       SET nombre = btrim(p_nombre),
           categoria = p_categoria,
           requiere_detalle = COALESCE(p_requiere_detalle, false),
           destino_sugerido = p_destino_sugerido,
           activo = COALESCE(p_activo, true),
           orden = COALESCE(p_orden, orden),
           actualizado_en = now()
     WHERE id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El motivo no existe.';
    END IF;

    RETURN p_id;
EXCEPTION
    WHEN check_violation THEN
        RAISE EXCEPTION 'Categoría desconocida: %.', p_categoria;
END;
$function$;


-- ═══ Reporte por categoría ═══

DROP FUNCTION IF EXISTS sp_mer_c_resumen_categoria(date, date, integer);

CREATE FUNCTION sp_mer_c_resumen_categoria(
    p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(categoria text, registros bigint, unidades bigint, costo_perdido bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT COALESCE(mm.categoria, 'otro'), count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    LEFT JOIN motivos_merma mm ON mm.id = m.motivo_id
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY 1
    ORDER BY 4 DESC;
$function$;


-- ═══ Registrar con el catálogo ═══
-- sp_mer_i_merma y sp_mer_i_desarme validan el motivo y guardan motivo_id.
-- Se parchan sobre la definición vigente (la del script 08) para no
-- duplicar sus 200 líneas: solo cambia el chequeo del motivo y el INSERT.

DO $parche$
DECLARE
    v_def text;
BEGIN
    -- ─── Merma suelta ───
    -- Sin CR: si el script que la creó se guardó con saltos de Windows, los
    -- reemplazos de abajo no encontrarían nada.
    v_def := replace(pg_get_functiondef('sp_mer_i_merma(integer,integer,integer,integer,text,text,destino_merma,integer,calidad_reingreso,text,boolean,text,integer)'::regprocedure), E'\r', '');

    IF position('fn_mer_motivo' IN v_def) = 0 THEN
        v_def := replace(v_def,
            E'    v_proveedor   int;\n',
            E'    v_proveedor   int;\n    v_mot         motivos_merma;\n');
        v_def := replace(v_def,
            E'    IF btrim(coalesce(p_motivo, '''')) = '''' THEN\n        RAISE EXCEPTION ''Indica el motivo de la merma.'';\n    END IF;\n',
            E'    IF btrim(coalesce(p_motivo, '''')) = '''' THEN\n        RAISE EXCEPTION ''Indica el motivo de la merma.'';\n    END IF;\n\n    -- Del catálogo, y con detalle si el motivo lo pide.\n    v_mot := fn_mer_motivo(p_motivo, p_detalle);\n');
        v_def := replace(v_def,
            E'        producto_id, lote_id, partida_id, cantidad, motivo, detalle,\n',
            E'        producto_id, lote_id, partida_id, cantidad, motivo, motivo_id, detalle,\n');
        v_def := replace(v_def,
            E'        p_producto_id, p_lote_id, p_partida_id, p_cantidad,\n        btrim(p_motivo), nullif',
            E'        p_producto_id, p_lote_id, p_partida_id, p_cantidad,\n        v_mot.nombre, v_mot.id, nullif');
        v_def := replace(v_def,
            E'        p_producto_id, p_lote_id, ''merma'', -p_cantidad, v_stock,\n        btrim(p_motivo),',
            E'        p_producto_id, p_lote_id, ''merma'', -p_cantidad, v_stock,\n        v_mot.nombre,');

        IF position('v_mot.id' IN v_def) = 0 OR position('fn_mer_motivo' IN v_def) = 0 THEN
            RAISE EXCEPTION 'No se pudo parchar sp_mer_i_merma: la definición no es la esperada.';
        END IF;

        EXECUTE v_def;
    END IF;

    -- ─── Desarme ───
    v_def := replace(pg_get_functiondef('sp_mer_i_desarme(integer,integer,text,text,jsonb,integer,text)'::regprocedure), E'\r', '');

    IF position('fn_mer_motivo' IN v_def) = 0 THEN
        v_def := replace(v_def,
            E'    v_firma     text',
            E'    v_mot       motivos_merma;\n    v_firma     text');
        v_def := replace(v_def,
            E'        RAISE EXCEPTION ''Indica el motivo del desarme.'';\n    END IF;\n',
            E'        RAISE EXCEPTION ''Indica el motivo del desarme.'';\n    END IF;\n\n    v_mot := fn_mer_motivo(p_motivo, p_detalle);\n');
        v_def := replace(v_def,
            E'            producto_id, lote_id, cantidad, motivo, detalle,\n',
            E'            producto_id, lote_id, cantidad, motivo, motivo_id, detalle,\n');
        v_def := replace(v_def,
            E'            r.componente_id, NULL, r.esperado,\n            btrim(p_motivo),\n',
            E'            r.componente_id, NULL, r.esperado,\n            v_mot.nombre, v_mot.id,\n');

        IF position('v_mot.id' IN v_def) = 0 OR position('fn_mer_motivo' IN v_def) = 0 THEN
            RAISE EXCEPTION 'No se pudo parchar sp_mer_i_desarme: la definición no es la esperada.';
        END IF;

        EXECUTE v_def;
    END IF;
END;
$parche$;
