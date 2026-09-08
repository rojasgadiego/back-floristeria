-- ═══════════════════════════════════════════════════════════════
-- INVENTARIO · Consultas de productos
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- sp_inv_c_productos — la grilla
--
-- Todos los filtros son opcionales: NULL significa "no filtres por
-- esto". Así una sola función sirve para la grilla, el buscador y la
-- alerta de bajo mínimo, en vez de tres casi iguales.
--
-- total_filas viene por count(*) OVER (), que se calcula ANTES del
-- LIMIT. Eso da el total real para paginar sin una segunda consulta.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_c_productos(
    p_busqueda      text          DEFAULT NULL,
    p_categoria_id  int           DEFAULT NULL,
    p_tipo          tipo_producto DEFAULT NULL,
    p_activo        boolean       DEFAULT NULL,
    p_bajo_minimo   boolean       DEFAULT false,
    p_pagina        int           DEFAULT 1,
    p_tamano        int           DEFAULT 50
)
RETURNS TABLE (
    id              int,
    codigo          text,
    nombre          text,
    categoria_id    int,
    categoria       text,
    tipo            tipo_producto,
    emoji           text,
    precio          numeric,
    minimo          int,
    costo           numeric,
    stock           int,
    stock_listo     int,
    costo_armado    numeric,
    controla_lotes  boolean,
    dias_vida       int,
    activo          boolean,
    margen          numeric,
    bajo_minimo     boolean,
    creado_en       timestamptz,
    actualizado_en  timestamptz,
    total_filas     bigint
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        p.id,
        p.codigo,
        p.nombre,
        p.categoria_id,
        c.nombre AS categoria,
        p.tipo,
        p.emoji,
        p.precio,
        p.minimo,
        p.costo,
        p.stock,
        p.stock_listo,
        p.costo_armado,
        p.controla_lotes,
        p.dias_vida,
        p.activo,
        -- Margen sobre el costo que corresponda: los armados cuestan lo
        -- que suma su receta, no lo que dice costo.
        ROUND(
            (p.precio - COALESCE(p.costo_armado, p.costo, 0))
            / NULLIF(p.precio, 0) * 100
        , 1) AS margen,
        (p.stock <= p.minimo) AS bajo_minimo,
        p.creado_en,
        p.actualizado_en,
        count(*) OVER () AS total_filas
    FROM productos p
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE (p_busqueda IS NULL
           OR p.nombre ILIKE '%' || p_busqueda || '%'
           OR p.codigo ILIKE '%' || p_busqueda || '%')
      AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)
      AND (p_tipo IS NULL OR p.tipo = p_tipo)
      AND (p_activo IS NULL OR p.activo = p_activo)
      AND (NOT p_bajo_minimo OR p.stock <= p.minimo)
    ORDER BY p.nombre
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;

COMMENT ON FUNCTION sp_inv_c_productos IS
    'Grilla de inventario. Filtros opcionales; total_filas para paginar.';


-- ───────────────────────────────────────────────────────────────
-- sp_inv_c_producto — el detalle, por id
--
-- Devuelve cero filas si no existe. El 404 lo decide el endpoint: para
-- la base "no hay" no es un error.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_c_producto(p_id int)
RETURNS TABLE (
    id int, codigo text, nombre text, categoria_id int, categoria text,
    tipo tipo_producto, emoji text, precio numeric, minimo int, costo numeric,
    stock int, stock_listo int, costo_armado numeric, controla_lotes boolean,
    dias_vida int, activo boolean, margen numeric, bajo_minimo boolean,
    creado_en timestamptz, actualizado_en timestamptz, total_filas bigint
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        p.id, p.codigo, p.nombre, p.categoria_id, c.nombre,
        p.tipo, p.emoji, p.precio, p.minimo, p.costo,
        p.stock, p.stock_listo, p.costo_armado, p.controla_lotes,
        p.dias_vida, p.activo,
        ROUND((p.precio - COALESCE(p.costo_armado, p.costo, 0))
              / NULLIF(p.precio, 0) * 100, 1),
        (p.stock <= p.minimo),
        p.creado_en, p.actualizado_en,
        1::bigint
    FROM productos p
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE p.id = p_id;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_c_producto_codigo — lo que lee el lector de barras
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_c_producto_codigo(p_codigo text)
RETURNS TABLE (
    id int, codigo text, nombre text, categoria_id int, categoria text,
    tipo tipo_producto, emoji text, precio numeric, minimo int, costo numeric,
    stock int, stock_listo int, costo_armado numeric, controla_lotes boolean,
    dias_vida int, activo boolean, margen numeric, bajo_minimo boolean,
    creado_en timestamptz, actualizado_en timestamptz, total_filas bigint
)
LANGUAGE sql
STABLE
AS $$
    SELECT
        p.id, p.codigo, p.nombre, p.categoria_id, c.nombre,
        p.tipo, p.emoji, p.precio, p.minimo, p.costo,
        p.stock, p.stock_listo, p.costo_armado, p.controla_lotes,
        p.dias_vida, p.activo,
        ROUND((p.precio - COALESCE(p.costo_armado, p.costo, 0))
              / NULLIF(p.precio, 0) * 100, 1),
        (p.stock <= p.minimo),
        p.creado_en, p.actualizado_en,
        1::bigint
    FROM productos p
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE lower(p.codigo) = lower(btrim(p_codigo));
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_inv_c_categorias — combo del formulario
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_inv_c_categorias()
RETURNS TABLE (id int, nombre text, productos bigint)
LANGUAGE sql
STABLE
AS $$
    SELECT c.id, c.nombre, count(p.id)
    FROM categorias c
    LEFT JOIN productos p ON p.categoria_id = c.id AND p.activo
    GROUP BY c.id, c.nombre
    ORDER BY c.nombre;
$$;
