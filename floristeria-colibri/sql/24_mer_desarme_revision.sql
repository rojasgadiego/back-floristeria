-- =====================================================================
-- Mermas: desarme con evidencia y bandeja de revisión
-- =====================================================================
-- 1. El desarme pasa a ser un reporte más (tipo 'desarme'): lleva fotos,
--    se puede deshacer en los 10 minutos y entra a la revisión. Por dentro
--    sigue usando sp_mer_i_desarme, que reparte cada componente.
--
-- 2. Deshacer recorría las mermas del reporte y revertía cada una. En un
--    desarme, revertir una ya revierte el desarme entero, y la segunda
--    fallaba con "ya fue revertida". Ahora se salta lo que ya quedó
--    revertido en el camino.
--
-- 3. Revisión: la administración ve los reportes que nadie ha mirado, con
--    sus fotos, y los marca como revisados. Es la otra mitad de pedir
--    evidencia: una foto que nadie mira no controla nada.
--
-- Idempotente.
-- =====================================================================

ALTER TABLE merma_reportes DROP CONSTRAINT IF EXISTS merma_reportes_tipo_check;
ALTER TABLE merma_reportes ADD CONSTRAINT merma_reportes_tipo_check
    CHECK (tipo IN ('puntual', 'incidente', 'desarme'));

ALTER TABLE merma_reportes
    ADD COLUMN IF NOT EXISTS revisado_en  timestamptz,
    ADD COLUMN IF NOT EXISTS revisado_por integer REFERENCES usuarios(id),
    ADD COLUMN IF NOT EXISTS nota_revision text;

CREATE INDEX IF NOT EXISTS ix_merma_reportes_pendientes
    ON merma_reportes (creado_en DESC)
    WHERE revisado_en IS NULL AND deshecho_en IS NULL;


-- ═══ Deshacer, sin tropezar con el desarme ═══

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

    FOR r IN SELECT m.id FROM mermas m WHERE m.reporte_id = p_reporte_id ORDER BY m.id
    LOOP
        -- Se vuelve a mirar en cada vuelta: revertir una línea de un
        -- desarme revierte todas las del grupo.
        IF NOT (SELECT m.revertida FROM mermas m WHERE m.id = r.id) THEN
            PERFORM sp_mer_u_revertir(r.id, 'Deshecha por quien la registró', p_usuario_id);
        END IF;
    END LOOP;

    UPDATE merma_reportes
       SET deshecho_en = now(), deshecho_por = p_usuario_id
     WHERE id = p_reporte_id;

    RETURN p_reporte_id;
END;
$$;


-- ═══ Desarme como reporte ═══
--
-- p_lineas: [{ componenteId, recuperadas, perdidas, calidad }]

CREATE OR REPLACE FUNCTION sp_mer_i_reporte_desarme(
    p_producto_id     integer,
    p_cantidad        integer,
    p_motivo          text,
    p_detalle         text,
    p_lineas          jsonb,
    p_fotos           bytea[],
    p_tipos_foto      text[],
    p_autorizado_por  text    DEFAULT NULL,
    p_usuario_id      integer DEFAULT NULL
)
RETURNS TABLE (reporte_id integer, mermas integer, recuperadas integer, perdidas integer, foto_repetida boolean)
LANGUAGE plpgsql
AS $$
DECLARE
    v_rep    int;
    v_mot    motivos_merma;
    v_res    record;
    v_grupo  int;
    v_fotos  int := COALESCE(array_length(p_fotos, 1), 0);
    v_firma  text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
    v_repe   boolean := false;
    v_sha    text;
BEGIN
    IF v_fotos < 1 THEN
        RAISE EXCEPTION 'Agrega al menos una foto del ramo antes de desarmarlo.';
    END IF;

    IF v_fotos > 3 THEN
        RAISE EXCEPTION 'Son máximo 3 fotos por reporte.';
    END IF;

    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    INSERT INTO merma_reportes (tipo, motivo_id, detalle, usuario_id, autorizado_por)
    VALUES ('desarme', v_mot.id, nullif(btrim(coalesce(p_detalle, '')), ''), p_usuario_id, v_firma)
    RETURNING id INTO v_rep;

    SELECT * INTO v_res
      FROM sp_mer_i_desarme(p_producto_id, p_cantidad, p_motivo, p_detalle,
                            p_lineas, p_usuario_id, v_firma);

    -- El grupo que acaba de usar el desarme, en esta misma sesión.
    v_grupo := currval('seq_mer_desarme_grupo');

    UPDATE mermas m SET reporte_id = v_rep WHERE m.desarme_grupo = v_grupo;

    FOR i IN 1 .. v_fotos LOOP
        v_sha := encode(sha256(p_fotos[i]), 'hex');

        IF EXISTS (SELECT 1 FROM merma_evidencias e WHERE e.sha256 = v_sha) THEN
            v_repe := true;
        END IF;

        INSERT INTO merma_evidencias (reporte_id, foto, tipo_mime, sha256)
        VALUES (v_rep, p_fotos[i], COALESCE(p_tipos_foto[i], 'image/jpeg'), v_sha);
    END LOOP;

    RETURN QUERY SELECT v_rep, v_res.o_mermas, v_res.o_recuperadas, v_res.o_perdidas, v_repe;
END;
$$;


-- ═══ Revisión ═══

CREATE OR REPLACE FUNCTION sp_mer_c_reportes_pendientes(p_limite integer DEFAULT 50)
RETURNS TABLE (
    reporte_id     integer,
    tipo           text,
    motivo         text,
    detalle        text,
    usuario        text,
    creado_en      timestamptz,
    autorizado_por text,
    lineas         text,
    costo_perdido  integer,
    fotos          integer,
    foto_repetida  boolean,
    sin_escanear   integer
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        r.id, r.tipo, mm.nombre, r.detalle, u.nombre, r.creado_en, r.autorizado_por,
        (SELECT string_agg(me.cantidad || ' × ' || p.nombre, ' · ' ORDER BY me.id)
           FROM mermas me JOIN productos p ON p.id = me.producto_id
          WHERE me.reporte_id = r.id),
        (SELECT COALESCE(sum(me.costo_perdido), 0)::int FROM mermas me WHERE me.reporte_id = r.id),
        (SELECT count(*)::int FROM merma_evidencias e WHERE e.reporte_id = r.id),
        EXISTS (SELECT 1 FROM merma_evidencias e
                  JOIN merma_evidencias o ON o.sha256 = e.sha256 AND o.id <> e.id
                 WHERE e.reporte_id = r.id),
        -- Tenían etiqueta (balde o partida) y se tipeó a mano.
        (SELECT count(*)::int FROM mermas me
          WHERE me.reporte_id = r.id AND NOT me.escaneado
            AND (me.lote_id IS NOT NULL OR me.partida_id IS NOT NULL))
      FROM merma_reportes r
      LEFT JOIN motivos_merma mm ON mm.id = r.motivo_id
      LEFT JOIN usuarios u ON u.id = r.usuario_id
     WHERE r.revisado_en IS NULL AND r.deshecho_en IS NULL
     ORDER BY r.creado_en DESC
     LIMIT GREATEST(COALESCE(p_limite, 50), 1);
$$;

CREATE OR REPLACE FUNCTION sp_mer_u_reporte_revisado(
    p_reporte_id integer, p_usuario_id integer, p_nota text DEFAULT NULL)
RETURNS integer
LANGUAGE plpgsql
AS $$
BEGIN
    UPDATE merma_reportes
       SET revisado_en = now(), revisado_por = p_usuario_id,
           nota_revision = nullif(btrim(coalesce(p_nota, '')), '')
     WHERE id = p_reporte_id AND revisado_en IS NULL;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Ese reporte no existe o ya se revisó.';
    END IF;

    RETURN p_reporte_id;
END;
$$;
