-- =====================================================================
-- Libro de movimientos: código de lote, ubicación y privacidad
-- =====================================================================
-- La grilla de Movimientos mostraba el lote vacío: la función devolvía
-- solo lote_id, y lo que se lee en el balde es el código (LOT-000001).
-- Tampoco decía si el movimiento fue en bodega o en el mesón.
--
-- p_usuario_id: la regla de privacidad. Null para el administrador (ve
-- todo); con un id, solo los movimientos de esa persona. Antes el recorte
-- lo hacía solo el front, y la API entregaba los de todos.
--
-- Cambia la firma (columnas y parámetro nuevos), así que se reemplaza con
-- DROP + CREATE. Idempotente.
-- =====================================================================

DROP FUNCTION IF EXISTS sp_inv_c_movimientos(integer, tipo_movimiento, date, date, integer, integer);
DROP FUNCTION IF EXISTS sp_inv_c_movimientos(integer, tipo_movimiento, date, date, integer, integer, integer);

CREATE FUNCTION sp_inv_c_movimientos(
    p_producto_id integer DEFAULT NULL::integer,
    p_tipo tipo_movimiento DEFAULT NULL::tipo_movimiento,
    p_desde date DEFAULT NULL::date,
    p_hasta date DEFAULT NULL::date,
    p_pagina integer DEFAULT 1,
    p_tamano integer DEFAULT 50,
    p_usuario_id integer DEFAULT NULL::integer)
RETURNS TABLE(
    id integer, producto_id integer, producto text, emoji text,
    lote_id integer, lote_codigo text, ubicacion text,
    tipo tipo_movimiento, cantidad integer, stock_resultante integer,
    motivo text, detalle text, usuario_id integer, usuario text,
    referencia_tipo text, referencia_id integer,
    creado_en timestamp with time zone, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT
        m.id, m.producto_id, p.nombre, p.emoji,
        -- Una merma del mostrador no tiene lote: se muestra la partida.
        m.lote_id, COALESCE(l.codigo, pm.codigo),
        -- Lo que pasa en el mesón: las ventas, lo que baja o vuelve como
        -- partida, y las mermas (o sus reversas) que salieron de una partida.
        -- Todo lo demás (compras, mermas de bodega, ajustes, armado) es bodega.
        CASE WHEN m.tipo = 'venta' OR m.referencia_tipo = 'partida'
                  OR me.partida_id IS NOT NULL
             THEN 'venta' ELSE 'bodega' END,
        m.tipo, m.cantidad, m.stock_resultante,
        m.motivo, m.detalle,
        m.usuario_id, u.nombre,
        m.referencia_tipo, m.referencia_id,
        m.creado_en,
        count(*) OVER ()
    FROM movimientos_inventario m
    JOIN productos p ON p.id = m.producto_id
    LEFT JOIN lotes l ON l.id = m.lote_id
    LEFT JOIN mermas me
           ON me.id = m.referencia_id
          AND m.referencia_tipo IN ('merma', 'merma_reversa')
    LEFT JOIN partidas_mostrador pm ON pm.id = me.partida_id
    LEFT JOIN usuarios u ON u.id = m.usuario_id
    WHERE (p_producto_id IS NULL OR m.producto_id = p_producto_id)
      AND (p_tipo        IS NULL OR m.tipo        = p_tipo)
      AND (p_desde       IS NULL OR m.creado_en >= p_desde)
      -- +1 día porque creado_en lleva hora: sin eso, "hasta el 22" deja
      -- fuera todo lo que pasó el 22 después de medianoche.
      AND (p_hasta       IS NULL OR m.creado_en <  p_hasta + 1)
      AND (p_usuario_id  IS NULL OR m.usuario_id  = p_usuario_id)
    ORDER BY m.creado_en DESC, m.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$;
