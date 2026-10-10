-- =====================================================================
-- Mermas: el plan de desarme con el costo real de cada componente
-- =====================================================================
-- El plan valorizaba con productos.costo, que en las flores es 0 (su costo
-- vive en los lotes). La pantalla estimaba mal si el desarme pedía firma.
-- Ahora usa fn_mer_costo_ref, el mismo costo con que la base registra y
-- decide la firma (sql/15).
--
-- Idempotente.
-- =====================================================================

CREATE OR REPLACE FUNCTION sp_mer_c_plan_desarme(p_producto_id integer, p_cantidad integer DEFAULT 1)
RETURNS TABLE(componente_id integer, componente text, emoji text, controla_lotes boolean, por_unidad integer, total integer, costo_unitario numeric, precio integer, dias_vida integer)
LANGUAGE sql
STABLE
AS $$
    SELECT
        r.componente_id, p.nombre, p.emoji, p.controla_lotes,
        r.cantidad,
        r.cantidad * GREATEST(p_cantidad, 1),
        fn_mer_costo_ref(p.id)::numeric,
        p.precio,
        p.dias_vida
    FROM recetas r
    JOIN productos p ON p.id = r.componente_id
    WHERE r.producto_id = p_producto_id
    ORDER BY p.nombre;
$$;
