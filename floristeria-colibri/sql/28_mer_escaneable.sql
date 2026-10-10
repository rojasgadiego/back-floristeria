-- =====================================================================
-- Mermas: "sin escanear" solo para lo que se podía escanear
-- =====================================================================
-- El control contaba como "registrada a mano" toda merma no escaneada,
-- incluidos los ramos armados, lo que no se maneja por lotes y los
-- faltantes de un conteo: nada de eso tiene etiqueta que leer. Inflaba el
-- porcentaje de quien más trabaja con ramos o hace los conteos.
--
-- Ahora cuenta solo lo que salió de un balde o una partida y no de un
-- conteo.
--
-- Copia las versiones vigentes en producción.
--
-- Idempotente.
-- =====================================================================

CREATE OR REPLACE FUNCTION public.sp_mer_c_patron_usuario(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date)
 RETURNS TABLE(usuario_id integer, usuario text, rol rol_usuario, registros bigint, unidades bigint, costo_perdido bigint, sin_escanear bigint, porcentaje_sin_escanear numeric, monto_promedio numeric, producto_top text, motivo_top text)
 LANGUAGE sql
 STABLE
AS $function$
    WITH m AS (
        -- escaneable: tenía etiqueta (balde o partida) y no salió de un
        -- conteo. Un ramo, una cinta o un faltante de conteo no se pueden
        -- escanear: contarlos como "a mano" acusaba a quien más los maneja.
        SELECT me.*, ((me.lote_id IS NOT NULL OR me.partida_id IS NOT NULL) AND NOT EXISTS (SELECT 1 FROM conteo_lineas cl WHERE cl.merma_id = me.id)) AS escaneable
        FROM mermas me
        WHERE NOT me.revertida
          AND me.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
          AND me.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
    )
    SELECT
        m.usuario_id, u.nombre, u.rol,
        count(*),
        sum(m.cantidad),
        sum(m.costo_perdido),
        count(*) FILTER (WHERE m.escaneable AND NOT m.escaneado),
        COALESCE(ROUND(count(*) FILTER (WHERE m.escaneable AND NOT m.escaneado)::numeric
                       / NULLIF(count(*) FILTER (WHERE m.escaneable), 0) * 100, 1), 0),
        ROUND(avg(m.costo_perdido)),

        -- Lo que más merma esa persona. Si siempre es el producto más caro
        -- del catálogo, vale la pena preguntar.
        (SELECT p.nombre FROM mermas m2
         JOIN productos p ON p.id = m2.producto_id
         WHERE m2.usuario_id = m.usuario_id AND NOT m2.revertida
           AND m2.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
         GROUP BY p.nombre ORDER BY sum(m2.costo_perdido) DESC LIMIT 1),

        (SELECT m3.motivo FROM mermas m3
         WHERE m3.usuario_id = m.usuario_id AND NOT m3.revertida
           AND m3.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
         GROUP BY m3.motivo ORDER BY count(*) DESC LIMIT 1)

    FROM m
    LEFT JOIN usuarios u ON u.id = m.usuario_id
    GROUP BY m.usuario_id, u.nombre, u.rol
    ORDER BY 6 DESC;
$function$
;

CREATE OR REPLACE FUNCTION public.sp_mer_c_sin_escanear(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date)
 RETURNS TABLE(id integer, producto text, emoji text, cantidad integer, costo_perdido integer, motivo text, detalle text, lote_codigo text, usuario text, creado_en timestamp with time zone)
 LANGUAGE sql
 STABLE
AS $function$
    SELECT
        me.id, p.nombre, p.emoji,
        me.cantidad, me.costo_perdido, me.motivo, me.detalle,
        COALESCE(l.codigo, pm.codigo), u.nombre, me.creado_en
    FROM mermas me
    JOIN productos p ON p.id = me.producto_id
    LEFT JOIN lotes l  ON l.id = me.lote_id
    LEFT JOIN partidas_mostrador pm ON pm.id = me.partida_id
    LEFT JOIN usuarios u ON u.id = me.usuario_id
    WHERE NOT me.revertida
      AND NOT me.escaneado
      -- Solo lo que tenía etiqueta y no salió de un conteo (ver arriba).
      AND ((me.lote_id IS NOT NULL OR me.partida_id IS NOT NULL) AND NOT EXISTS (SELECT 1 FROM conteo_lineas cl WHERE cl.merma_id = me.id))
      AND me.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND me.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
    ORDER BY me.costo_perdido DESC, me.creado_en DESC
    LIMIT 50;
$function$
;
