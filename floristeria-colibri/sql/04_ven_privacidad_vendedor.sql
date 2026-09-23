-- =====================================================================
-- Privacidad del vendedor
-- =====================================================================
-- Regla del negocio: un vendedor ve ÚNICAMENTE su información —sus ventas
-- de hoy y de siempre—, jamás las de otros. El administrador ve todo.
--
-- La caja es una sola para todo el local y cualquiera la puede cerrar, así
-- que el arqueo del vendedor es CIEGO: cuenta el efectivo y lo anota, sin
-- ver cuánto debería haber ni la diferencia. Ese número incluye lo que
-- cobraron los demás.
--
-- Estas funciones son las versiones "de un usuario" de las que ya existen.
-- Devuelven exactamente las mismas columnas —el backend las mapea al mismo
-- modelo— y dejan en NULL lo que el vendedor no debe ver. Las originales
-- no se tocan: las sigue usando el administrador.
--
-- Idempotente: se puede correr más de una vez.
-- =====================================================================


-- ─── Caja: la abierta (p_caja_id NULL) o una en particular ───────────
CREATE OR REPLACE FUNCTION sp_ven_c_caja_usuario(p_caja_id integer, p_usuario_id integer)
RETURNS TABLE(
    id integer, estado estado_caja, fondo_inicial integer,
    abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer,
    cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text,
    efectivo integer, debito integer, credito integer, transferencia integer,
    total_vendido integer, boletas bigint, anuladas bigint,
    total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer,
    en_cajon integer, efectivo_esperado integer, efectivo_contado integer,
    diferencia integer, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        c.id, c.estado, c.fondo_inicial,
        c.abierta_en, ua.nombre, c.abierta_por,
        c.cerrada_en, uc.nombre,
        -- La nota la escribe quien cierra y suele hablar del arqueo.
        NULL::text,
        COALESCE(v.efectivo, 0)::int, COALESCE(v.debito, 0)::int,
        COALESCE(v.credito, 0)::int, COALESCE(v.transferencia, 0)::int,
        COALESCE(v.total_vendido, 0)::int,
        COALESCE(v.boletas, 0), COALESCE(v.anuladas, 0),
        COALESCE(v.descuentos, 0)::int,
        COALESCE(v.puntos_otorgados, 0)::int, COALESCE(v.puntos_canjeados, 0)::int,
        -- Arqueo ciego: todo esto suma lo cobrado por el resto del equipo.
        NULL::int, NULL::int, NULL::int, NULL::int,
        1::bigint
    FROM cajas c
    LEFT JOIN usuarios ua ON ua.id = c.abierta_por
    LEFT JOIN usuarios uc ON uc.id = c.cerrada_por
    LEFT JOIN LATERAL (
        SELECT
            sum(ve.total) FILTER (WHERE ve.medio_pago = 'efectivo'      AND NOT ve.anulada) AS efectivo,
            sum(ve.total) FILTER (WHERE ve.medio_pago = 'debito'        AND NOT ve.anulada) AS debito,
            sum(ve.total) FILTER (WHERE ve.medio_pago = 'credito'       AND NOT ve.anulada) AS credito,
            sum(ve.total) FILTER (WHERE ve.medio_pago = 'transferencia' AND NOT ve.anulada) AS transferencia,
            sum(ve.total) FILTER (WHERE NOT ve.anulada) AS total_vendido,
            count(*)      FILTER (WHERE NOT ve.anulada) AS boletas,
            count(*)      FILTER (WHERE ve.anulada)     AS anuladas,
            sum(ve.descuento_total)  FILTER (WHERE NOT ve.anulada) AS descuentos,
            sum(ve.puntos_ganados)   FILTER (WHERE NOT ve.anulada) AS puntos_otorgados,
            sum(ve.puntos_canjeados) FILTER (WHERE NOT ve.anulada) AS puntos_canjeados
        FROM ventas ve
        WHERE ve.caja_id = c.id
          AND ve.usuario_id = p_usuario_id
    ) v ON true
    WHERE (p_caja_id IS NULL AND c.estado = 'abierta')
       OR c.id = p_caja_id;
$function$;


-- ─── Historial: los turnos en los que el vendedor VENDIÓ ─────────────
-- Antes eran los que abrió o cerró: quien vendió todo el día en una caja
-- que abrió otro no veía ese turno. Ahora cuenta haber vendido, abierto o
-- cerrado, y siempre con sus propios totales.
CREATE OR REPLACE FUNCTION sp_ven_c_cajas_usuario(
    p_desde date, p_hasta date, p_usuario_id integer,
    p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30)
RETURNS TABLE(
    id integer, estado estado_caja, fondo_inicial integer,
    abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer,
    cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text,
    efectivo integer, debito integer, credito integer, transferencia integer,
    total_vendido integer, boletas bigint, anuladas bigint,
    total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer,
    en_cajon integer, efectivo_esperado integer, efectivo_contado integer,
    diferencia integer, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        r.id, r.estado, r.fondo_inicial,
        r.abierta_en, r.abierta_por, r.abierta_por_id,
        r.cerrada_en, r.cerrada_por, r.nota_cierre,
        r.efectivo, r.debito, r.credito, r.transferencia,
        r.total_vendido, r.boletas, r.anuladas,
        r.total_descuentos, r.puntos_otorgados, r.puntos_canjeados,
        r.en_cajon, r.efectivo_esperado, r.efectivo_contado, r.diferencia,
        count(*) OVER ()
    FROM cajas c
    CROSS JOIN LATERAL sp_ven_c_caja_usuario(c.id, p_usuario_id) r
    WHERE (p_desde IS NULL OR c.abierta_en >= p_desde)
      -- +1 día porque abierta_en lleva hora.
      AND (p_hasta IS NULL OR c.abierta_en <  p_hasta + 1)
      AND (c.abierta_por = p_usuario_id
           OR c.cerrada_por = p_usuario_id
           OR EXISTS (SELECT 1 FROM ventas ve
                       WHERE ve.caja_id = c.id AND ve.usuario_id = p_usuario_id))
    ORDER BY c.abierta_en DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$;


-- ─── Panel de inicio de un usuario ───────────────────────────────────
-- Hoy, la semana pasada y el mes, contando solo sus boletas. Sin costo,
-- utilidad, margen, inventario, clientes ni puntos por pagar: son números
-- del local, no suyos.
CREATE OR REPLACE FUNCTION sp_rep_c_panel_usuario(p_usuario_id integer)
RETURNS TABLE(
    hoy_boletas bigint, hoy_vendido bigint, hoy_ticket_promedio bigint,
    hoy_unidades bigint, hoy_costo bigint, hoy_utilidad bigint, hoy_margen numeric,
    hoy_anuladas bigint, hoy_descuentos bigint, hoy_clientes_nuevos bigint,
    sem_boletas bigint, sem_vendido bigint, variacion numeric,
    caja_id integer, caja_abierta_por text, caja_abierta_en timestamp with time zone,
    caja_fondo integer, caja_efectivo integer, caja_en_cajon integer, caja_boletas bigint,
    mes_vendido bigint, mes_boletas bigint, inventario_valorizado numeric,
    clientes_activos bigint, puntos_por_pagar bigint)
LANGUAGE sql
STABLE
AS $function$
    WITH mias AS (
        SELECT * FROM ventas v WHERE v.usuario_id = p_usuario_id
    ),
    hoy AS (
        SELECT count(*) AS boletas,
               COALESCE(sum(v.total), 0) AS vendido,
               COALESCE(sum(v.descuento_total), 0) AS descuentos
        FROM mias v
        WHERE v.creado_en::date = CURRENT_DATE AND NOT v.anulada
    ),
    hoy_detalle AS (
        SELECT COALESCE(sum(vi.cantidad), 0) AS unidades
        FROM venta_items vi
        JOIN mias v ON v.id = vi.venta_id
        WHERE v.creado_en::date = CURRENT_DATE AND NOT v.anulada
    ),
    hoy_anuladas AS (
        SELECT count(*) AS n FROM mias v
        WHERE v.creado_en::date = CURRENT_DATE AND v.anulada
    ),
    semana AS (
        SELECT count(*) AS boletas, COALESCE(sum(v.total), 0) AS vendido
        FROM mias v
        WHERE v.creado_en::date = CURRENT_DATE - 7 AND NOT v.anulada
    ),
    caja AS (
        SELECT c.id, u.nombre AS quien, c.abierta_en, c.fondo_inicial,
               COALESCE(e.efectivo, 0)::int AS efectivo,
               COALESCE(e.boletas, 0) AS boletas
        FROM cajas c
        LEFT JOIN usuarios u ON u.id = c.abierta_por
        LEFT JOIN LATERAL (
            SELECT sum(v.total) FILTER (WHERE v.medio_pago = 'efectivo') AS efectivo,
                   count(*) AS boletas
            FROM mias v
            WHERE v.caja_id = c.id AND NOT v.anulada
        ) e ON true
        WHERE c.estado = 'abierta'
        LIMIT 1
    ),
    mes AS (
        SELECT count(*) AS boletas, COALESCE(sum(v.total), 0) AS vendido
        FROM mias v
        WHERE v.creado_en >= date_trunc('month', CURRENT_DATE) AND NOT v.anulada
    )
    SELECT
        h.boletas,
        h.vendido,
        CASE WHEN h.boletas > 0 THEN ROUND(h.vendido::numeric / h.boletas) ELSE 0 END::bigint,
        hd.unidades,
        NULL::bigint, NULL::bigint, NULL::numeric,
        ha.n,
        h.descuentos,
        NULL::bigint,
        s.boletas,
        s.vendido,
        CASE WHEN s.vendido > 0
             THEN ROUND((h.vendido - s.vendido)::numeric / s.vendido * 100, 1)
             ELSE 0 END,
        c.id, c.quien, c.abierta_en, c.fondo_inicial,
        c.efectivo,
        -- En el cajón está lo de todos: arqueo ciego.
        NULL::int,
        c.boletas,
        m.vendido, m.boletas,
        NULL::numeric, NULL::bigint, NULL::bigint
    FROM hoy h
    CROSS JOIN hoy_detalle hd
    CROSS JOIN hoy_anuladas ha
    CROSS JOIN semana s
    CROSS JOIN mes m
    LEFT JOIN caja c ON true;
$function$;


-- ─── Compras de un cliente hechas por un vendedor ────────────────────
CREATE OR REPLACE FUNCTION sp_cli_c_compras_usuario(
    p_cliente_id integer, p_usuario_id integer,
    p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 20)
RETURNS TABLE(
    id integer, folio text, creado_en timestamp with time zone, total integer,
    descuento_total integer, medio_pago medio_pago, puntos_ganados integer,
    puntos_canjeados integer, lineas bigint, vendedor text, anulada boolean,
    total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        v.id, v.folio, v.creado_en,
        v.total, v.descuento_total, v.medio_pago,
        v.puntos_ganados, v.puntos_canjeados,
        (SELECT count(*) FROM venta_items vi WHERE vi.venta_id = v.id),
        u.nombre, v.anulada,
        count(*) OVER ()
    FROM ventas v
    LEFT JOIN usuarios u ON u.id = v.usuario_id
    WHERE v.cliente_id = p_cliente_id
      AND v.usuario_id = p_usuario_id
    ORDER BY v.creado_en DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$;
