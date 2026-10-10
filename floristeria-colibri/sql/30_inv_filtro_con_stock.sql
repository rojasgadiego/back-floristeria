-- =====================================================================
-- Inventario: filtro "con stock" en la grilla de productos
-- =====================================================================
-- Inventario Bodega muestra todo el catálogo, y con 140 productos la
-- mayoría en 0 se pierde lo que sí hay. p_con_stock deja solo lo que tiene
-- algo en bodega o en venta.
--
-- Va al final y con default: se elimina la versión de 9 argumentos (si no,
-- una llamada con 9 sería ambigua) y el backend que está corriendo, que
-- llama con 9, sigue funcionando mientras se redespliega.
--
-- Copia la versión vigente en producción (con lo de sql/29). Idempotente.
-- =====================================================================

DROP FUNCTION IF EXISTS sp_inv_c_productos(text, integer, tipo_producto, boolean, boolean, boolean, boolean, integer, integer);
DROP FUNCTION IF EXISTS sp_inv_c_productos(text, integer, tipo_producto, boolean, boolean, boolean, boolean, integer, integer, boolean);

CREATE FUNCTION public.sp_inv_c_productos(p_buscar text DEFAULT NULL::text, p_categoria_id integer DEFAULT NULL::integer, p_tipo tipo_producto DEFAULT NULL::tipo_producto, p_activo boolean DEFAULT NULL::boolean, p_bajo_minimo boolean DEFAULT false, p_controla_lotes boolean DEFAULT NULL::boolean, p_solo_en_venta boolean DEFAULT false, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50, p_con_stock boolean DEFAULT false)
 RETURNS TABLE(id integer, codigo text, nombre text, emoji text, categoria_id integer, categoria text, tipo tipo_producto, costo numeric, costo_armado numeric, costo_efectivo numeric, precio numeric, precio_ramo numeric, precio_liquidacion numeric, margen numeric, en_bodega integer, en_venta integer, stock_total integer, stock_listo integer, minimo integer, controla_lotes boolean, activo boolean, bajo_minimo boolean, total_filas bigint)
 LANGUAGE sql
 STABLE
AS $function$
    WITH base AS (
        SELECT
            p.id, p.codigo, p.nombre, p.emoji,
            p.categoria_id, c.nombre AS categoria_nombre, p.tipo,
            p.costo, p.costo_armado, p.precio, p.precio_ramo,
            p.precio_liquidacion, p.stock_listo, p.minimo,
            p.controla_lotes, p.activo,
            -- Un ramo listo está en la vitrina, no en bodega: se vende desde
            -- sus unidades listas (sp_ven_i_venta) y se cuenta en la vitrina.
            -- Antes caía en "bodega" con 0 en venta, y el POS, que muestra
            -- solo lo que está en venta, nunca lo ofrecía.
            CASE WHEN p.tipo = 'armado' THEN COALESCE(p.stock_listo, 0)
                 ELSE COALESCE(m.stock, 0) END AS venta,
            -- Los CHECK productos_forma_* dejan uno de los dos en NULL, no
            -- en cero: un simple tiene costo y stock, un armado tiene
            -- costo_armado y stock_listo.
            COALESCE(p.costo, p.costo_armado) AS costo_efe,
            CASE WHEN p.tipo = 'armado' THEN 0
                 ELSE COALESCE(p.stock, 0) END AS bodega
        FROM productos p
        LEFT JOIN categorias c   ON c.id = p.categoria_id
        LEFT JOIN vw_mostrador m ON m.producto_id = p.id
    )
    SELECT
        b.id, b.codigo, b.nombre, b.emoji,
        b.categoria_id, b.categoria_nombre, b.tipo,
        b.costo, b.costo_armado, b.costo_efe,
        b.precio, b.precio_ramo, b.precio_liquidacion,
        ROUND((b.precio - b.costo_efe)::numeric
              / NULLIF(b.precio, 0)::numeric * 100, 1),
        b.bodega, b.venta, b.bodega + b.venta, b.stock_listo, b.minimo,
        b.controla_lotes, b.activo,
        (b.bodega + b.venta) <= b.minimo,
        count(*) OVER ()
    FROM base b
    WHERE (p_buscar IS NULL
           OR b.nombre ILIKE '%' || p_buscar || '%'
           OR b.codigo ILIKE '%' || p_buscar || '%')
      AND (p_categoria_id   IS NULL OR b.categoria_id   = p_categoria_id)
      AND (p_tipo           IS NULL OR b.tipo           = p_tipo)
      AND (p_activo         IS NULL OR b.activo         = p_activo)
      AND (p_controla_lotes IS NULL OR b.controla_lotes = p_controla_lotes)
      AND (NOT COALESCE(p_bajo_minimo, false) OR (b.bodega + b.venta) <= b.minimo)
      AND (NOT COALESCE(p_solo_en_venta, false) OR b.venta > 0)
      -- Con algo en bodega o en venta (un ramo listo cuenta: está en vitrina).
      AND (NOT COALESCE(p_con_stock, false) OR (b.bodega + b.venta) > 0)
    ORDER BY b.nombre
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$
;
