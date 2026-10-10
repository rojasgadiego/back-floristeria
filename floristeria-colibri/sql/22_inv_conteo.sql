-- =====================================================================
-- Inventario: conteo de stock de bodega
-- =====================================================================
-- No había forma de corregir el stock: ni cargar lo que ya estaba en la
-- tienda (los productos se crearon en 0), ni arreglar un error pasado el
-- momento. El conteo es esa forma, y es la ÚNICA: una merma registrada no
-- se revierte (más allá de deshacerla en el momento).
--
-- Se cuenta lo que hay en bodega: los productos sin lotes por producto, y
-- los que se manejan por lotes, balde por balde. La persona escribe lo
-- que contó y la base calcula la diferencia contra lo que hay EN ESE
-- MOMENTO (no contra lo que vio en pantalla: entremedio pudo venderse algo).
--
--   · Sobrante → ajuste positivo en el historial. Es también como entra el
--     inventario inicial.
--   · Faltante → MERMA "Faltante en conteo", con su costo. Así aparece en el
--     reporte de pérdidas en vez de esconderse en un ajuste.
--
-- La vitrina (partidas del mostrador) no entra acá.
--
-- Idempotente.
-- =====================================================================

-- ═══ La planilla para contar ═══

CREATE OR REPLACE FUNCTION sp_inv_c_conteo(p_categoria_id integer DEFAULT NULL)
RETURNS TABLE (
    producto_id       integer,
    producto          text,
    emoji             text,
    categoria         text,
    controla_lotes    boolean,
    lote_id           integer,
    lote_codigo       text,
    fecha_vencimiento date,
    sistema           integer,
    costo_unitario    integer
)
LANGUAGE sql
STABLE
AS $$
    -- Sin lotes: una fila por producto, con el stock de bodega.
    SELECT p.id, p.nombre, p.emoji, c.nombre, false,
           NULL::int, NULL::text, NULL::date,
           COALESCE(p.stock, 0), fn_mer_costo_ref(p.id)
      FROM productos p
      JOIN categorias c ON c.id = p.categoria_id
     WHERE p.tipo = 'simple' AND p.activo AND NOT p.controla_lotes
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    UNION ALL

    -- Con lotes: una fila por balde activo.
    SELECT p.id, p.nombre, p.emoji, c.nombre, true,
           l.id, l.codigo, l.fecha_vencimiento,
           l.varas_disponibles, ROUND(l.costo_por_vara)::int
      FROM productos p
      JOIN categorias c ON c.id = p.categoria_id
      JOIN lotes l ON l.producto_id = p.id
     WHERE p.tipo = 'simple' AND p.activo AND p.controla_lotes
       AND l.estado = 'activo' AND l.varas_disponibles > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    ORDER BY 4, 2, 8 NULLS FIRST, 7;
$$;


-- ═══ Aplicar el conteo ═══
--
-- p_items: [{ "productoId": 1, "loteId": null, "contado": 12 }, …]
-- Todo o nada: si una línea falla, no se aplica ninguna.

CREATE OR REPLACE FUNCTION sp_inv_i_conteo(
    p_items          jsonb,
    p_detalle        text    DEFAULT NULL,
    p_usuario_id     integer DEFAULT NULL,
    p_autorizado_por text    DEFAULT NULL
)
RETURNS TABLE (
    producto_id  integer,
    producto     text,
    lote_codigo  text,
    sistema      integer,
    contado      integer,
    diferencia   integer,
    merma_id     integer
)
LANGUAGE plpgsql
AS $$
DECLARE
    it        jsonb;
    v_n       int := 0;
    v_prod    record;
    v_lote    record;
    v_lote_id int;
    v_contado int;
    v_sistema int;
    v_dif     int;
    v_stock   int;
    v_merma   int;
    -- En variable y no leído de v_lote: el registro que no se cargó (producto
    -- sin lotes) hace caer la función entera al tocarlo.
    v_codigo  text;
    v_detalle text := COALESCE(nullif(btrim(coalesce(p_detalle, '')), ''), 'Conteo de stock');
BEGIN
    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'No hay nada contado.';
    END IF;

    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n       := v_n + 1;
        v_lote_id := (it->>'loteId')::int;
        v_contado := (it->>'contado')::int;
        v_merma   := NULL;
        v_codigo  := NULL;

        IF v_contado IS NULL OR v_contado < 0 THEN
            RAISE EXCEPTION 'Línea %: lo contado no puede ser negativo.', v_n;
        END IF;

        SELECT * INTO v_prod FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
        END IF;

        IF v_prod.tipo <> 'simple' THEN
            RAISE EXCEPTION '% es un armado: no se cuenta acá.', v_prod.nombre;
        END IF;

        -- ─── Lo que dice el sistema, ahora ───
        IF v_prod.controla_lotes THEN
            IF v_lote_id IS NULL THEN
                RAISE EXCEPTION '% se maneja por lotes: se cuenta balde por balde.', v_prod.nombre;
            END IF;

            SELECT * INTO v_lote FROM lotes WHERE id = v_lote_id FOR UPDATE;

            IF NOT FOUND OR v_lote.producto_id <> v_prod.id THEN
                RAISE EXCEPTION 'Línea %: ese balde no es de %.', v_n, v_prod.nombre;
            END IF;

            v_sistema := v_lote.varas_disponibles;
            v_codigo  := v_lote.codigo;
        ELSE
            IF v_lote_id IS NOT NULL THEN
                RAISE EXCEPTION '% no se maneja por lotes.', v_prod.nombre;
            END IF;

            v_sistema := COALESCE(v_prod.stock, 0);
        END IF;

        v_dif := v_contado - v_sistema;

        IF v_dif > 0 THEN
            -- ─── Sobrante: entra ───
            IF v_prod.controla_lotes THEN
                UPDATE lotes
                   SET varas_disponibles = varas_disponibles + v_dif,
                       estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
                       actualizado_en = now()
                 WHERE id = v_lote_id;
            END IF;

            UPDATE productos
               SET stock = COALESCE(stock, 0) + v_dif, actualizado_en = now()
             WHERE id = v_prod.id
            RETURNING stock INTO v_stock;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                v_prod.id, v_lote_id, 'ajuste', v_dif, v_stock,
                'Conteo de stock', 'Sobrante · ' || v_detalle,
                p_usuario_id, 'conteo', NULL
            );

        ELSIF v_dif < 0 THEN
            -- ─── Faltante: es una merma ───
            -- Con el mismo registro que cualquier merma: descuenta, deja su
            -- movimiento, se valoriza y aparece en el reporte de pérdidas.
            v_merma := sp_mer_i_merma(
                v_prod.id, v_lote_id, NULL, -v_dif,
                'Faltante en conteo', v_detalle,
                'perdida', 0, NULL,
                NULL, false, p_autorizado_por, p_usuario_id);
        END IF;

        RETURN QUERY
        SELECT v_prod.id, v_prod.nombre,
               v_codigo,
               v_sistema, v_contado, v_dif, v_merma;
    END LOOP;
END;
$$;
