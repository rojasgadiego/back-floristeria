-- =====================================================================
-- Cotizaciones y eventos
-- =====================================================================
-- Presupuesto de un evento → aprobado (en la agenda) → abonos en caja →
-- cobro final en el punto de venta. Anulado en cualquier momento antes
-- del cobro.
--
-- Reglas:
--   · Aprobar NO aparta inventario: solo pone el evento en la agenda. La
--     flor se compra cuando toca; `faltantes` dice cuánta falta hoy.
--   · Cada abono es una BOLETA REAL: entra a la caja abierta, con folio y
--     medio de pago, para que el arqueo nunca muestre plata sin explicar.
--   · Las cuotas no se marcan pagadas una por una: una cuota está cubierta
--     cuando lo abonado alcanza su acumulado. En un acuerdo de palabra
--     nadie paga montos exactos.
--   · Privacidad: el vendedor ve solo las cotizaciones que creó; eso lo
--     aplica el backend pasando el usuario a las consultas.
--
-- Idempotente: se puede correr más de una vez.
-- =====================================================================


-- ─── Columnas de anulación ───────────────────────────────────────────
-- La tabla no tenía dónde guardar por qué ni quién anuló.
ALTER TABLE cotizaciones ADD COLUMN IF NOT EXISTS motivo_anulacion text;
ALTER TABLE cotizaciones ADD COLUMN IF NOT EXISTS anulada_por integer REFERENCES usuarios(id) ON DELETE SET NULL;
ALTER TABLE cotizaciones ADD COLUMN IF NOT EXISTS anulada_en timestamp with time zone;


-- ─── Resumen: la cotización con su plata ya calculada ────────────────
-- Todo lo derivado vive acá y en ningún otro lado: el listado, la ficha,
-- "a quiénes llamar" y la agenda leen lo mismo.
DROP VIEW IF EXISTS vw_cot_resumen CASCADE;

CREATE VIEW vw_cot_resumen AS
WITH pagado AS (
    SELECT cotizacion_id,
           COALESCE(sum(monto) FILTER (WHERE NOT anulado), 0)::int AS abonado
    FROM cotizacion_pagos
    GROUP BY cotizacion_id
),
base AS (
    SELECT c.*,
           COALESCE(p.abonado, 0) AS abonado,
           GREATEST(c.total - COALESCE(p.abonado, 0), 0) AS pendiente,
           EXISTS (SELECT 1 FROM cotizacion_cuotas q WHERE q.cotizacion_id = c.id) AS con_plan
    FROM cotizaciones c
    LEFT JOIN pagado p ON p.cotizacion_id = c.id
),
cobro AS (
    -- Lo exigible solo corre para una aprobada: un borrador todavía no
    -- compromete a nadie, y lo cobrado o anulado ya no se persigue.
    SELECT b.id,
        CASE
            WHEN b.estado <> 'aprobada' THEN 0
            WHEN b.con_plan THEN GREATEST(
                (SELECT COALESCE(sum(q.monto), 0) FROM cotizacion_cuotas q
                  WHERE q.cotizacion_id = b.id AND q.vence <= CURRENT_DATE) - b.abonado, 0)
            -- Sin plan, el saldo completo vence el día del evento.
            WHEN b.fecha_evento IS NOT NULL AND b.fecha_evento <= CURRENT_DATE THEN b.pendiente
            ELSE 0
        END::int AS exigible_hoy,
        CASE
            WHEN b.estado <> 'aprobada' THEN 0
            WHEN b.con_plan THEN GREATEST(
                (SELECT COALESCE(sum(q.monto), 0) FROM cotizacion_cuotas q
                  WHERE q.cotizacion_id = b.id AND q.vence < CURRENT_DATE) - b.abonado, 0)
            WHEN b.fecha_evento IS NOT NULL AND b.fecha_evento < CURRENT_DATE THEN b.pendiente
            ELSE 0
        END::int AS vencido,
        CASE
            WHEN b.estado <> 'aprobada' THEN NULL
            -- La primera cuota que lo abonado no alcanza a cubrir.
            WHEN b.con_plan THEN (
                SELECT min(x.vence) FROM (
                    SELECT q.vence,
                           sum(q.monto) OVER (ORDER BY q.numero) AS acumulado
                    FROM cotizacion_cuotas q WHERE q.cotizacion_id = b.id
                ) x
                WHERE x.acumulado > b.abonado AND x.vence < CURRENT_DATE)
            WHEN b.fecha_evento < CURRENT_DATE AND b.pendiente > 0 THEN b.fecha_evento
        END AS vencido_desde
    FROM base b
)
SELECT
    b.id, b.folio, b.cliente_id, b.cliente_nombre, b.tipo_evento, b.fecha_evento,
    b.contacto, b.traslado, b.montaje, b.total,
    b.abonado AS abono,
    b.pendiente AS saldo,
    CASE WHEN b.total > 0 THEN ROUND(b.abonado * 100.0 / b.total, 1) ELSE 0 END AS porcentaje_pagado,
    b.estado::text AS estado,
    b.notas, b.creada_por, u.nombre AS creada_por_nombre,
    b.venta_id, b.cobrada_en, b.creado_en, b.actualizado_en,
    (SELECT count(*) FROM cotizacion_items i WHERE i.cotizacion_id = b.id) AS lineas,
    (b.fecha_evento - CURRENT_DATE) AS dias_para_evento,
    k.exigible_hoy, k.vencido, k.vencido_desde,
    -- Redactado acá para que se lea igual en toda la aplicación.
    CASE
        WHEN b.estado = 'anulada' THEN 'anulada'
        WHEN b.estado = 'cobrada' THEN 'cobrada'
        WHEN b.pendiente = 0      THEN 'pagada'
        WHEN k.vencido > 0 THEN
            'debe ' || to_char(k.vencido, 'FM$999G999G999') ||
            ' desde el ' || to_char(k.vencido_desde, 'DD-MM')
        WHEN b.estado = 'borrador' THEN 'sin aprobar'
        ELSE 'al día'
    END AS estado_pago,
    b.motivo_anulacion
FROM base b
JOIN cobro k ON k.id = b.id
LEFT JOIN usuarios u ON u.id = b.creada_por;


-- ─── Consultas ───────────────────────────────────────────────────────

CREATE OR REPLACE FUNCTION sp_cot_c_cotizaciones(
    p_buscar text, p_estado text, p_cliente_id integer, p_solo_vencidas boolean,
    p_proximos_dias integer, p_desde date, p_hasta date, p_usuario_id integer,
    p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30)
RETURNS TABLE(
    id integer, folio text, cliente_id integer, cliente_nombre text, tipo_evento text,
    fecha_evento date, contacto text, traslado integer, montaje integer, total integer,
    abono integer, saldo integer, porcentaje_pagado numeric, estado text, notas text,
    creada_por integer, creada_por_nombre text, venta_id integer,
    cobrada_en timestamp with time zone, creado_en timestamp with time zone,
    actualizado_en timestamp with time zone, lineas bigint, dias_para_evento integer,
    exigible_hoy integer, vencido integer, vencido_desde date, estado_pago text,
    motivo_anulacion text, total_filas bigint)
LANGUAGE sql
STABLE
AS $function$
    SELECT r.*, count(*) OVER ()
    FROM vw_cot_resumen r
    WHERE (p_usuario_id IS NULL OR r.creada_por = p_usuario_id)
      AND (nullif(btrim(p_buscar), '') IS NULL
           OR r.folio ILIKE '%' || btrim(p_buscar) || '%'
           OR r.cliente_nombre ILIKE '%' || btrim(p_buscar) || '%'
           OR r.contacto ILIKE '%' || btrim(p_buscar) || '%')
      AND (nullif(p_estado, '') IS NULL OR r.estado = p_estado)
      AND (p_cliente_id IS NULL OR r.cliente_id = p_cliente_id)
      AND (NOT COALESCE(p_solo_vencidas, false) OR r.vencido > 0)
      AND (p_proximos_dias IS NULL
           OR (r.estado = 'aprobada' AND r.dias_para_evento BETWEEN 0 AND p_proximos_dias))
      AND (p_desde IS NULL OR r.fecha_evento >= p_desde)
      AND (p_hasta IS NULL OR r.fecha_evento <= p_hasta)
    ORDER BY r.creado_en DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$function$;


-- Vacía si no existe o si no es del vendedor que pregunta.
CREATE OR REPLACE FUNCTION sp_cot_c_cotizacion(p_id integer, p_usuario_id integer)
RETURNS SETOF vw_cot_resumen
LANGUAGE sql
STABLE
AS $function$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.id = p_id
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id);
$function$;


-- Del más atrasado al menos: es la lista de a quiénes llamar.
CREATE OR REPLACE FUNCTION sp_cot_c_por_cobrar(p_usuario_id integer)
RETURNS SETOF vw_cot_resumen
LANGUAGE sql
STABLE
AS $function$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.vencido > 0
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id)
    ORDER BY r.vencido_desde, r.vencido DESC;
$function$;


-- Lo que se mira para decidir cuánta flor comprar esta semana.
CREATE OR REPLACE FUNCTION sp_cot_c_agenda(p_dias integer, p_usuario_id integer)
RETURNS SETOF vw_cot_resumen
LANGUAGE sql
STABLE
AS $function$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.estado = 'aprobada'
      AND r.fecha_evento BETWEEN CURRENT_DATE AND CURRENT_DATE + GREATEST(COALESCE(p_dias, 30), 1)
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id)
    ORDER BY r.fecha_evento, r.id;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_c_items(p_id integer)
RETURNS TABLE(
    id integer, producto_id integer, emoji text, nombre text, precio integer,
    cantidad integer, subtotal integer, a_medida boolean, disponible integer)
LANGUAGE sql
STABLE
AS $function$
    SELECT i.id, i.producto_id, p.emoji, i.nombre, i.precio, i.cantidad,
           i.precio * i.cantidad, i.a_medida,
           -- Lo que hay hoy entre bodega y mesón. Null en lo hecho a medida.
           CASE WHEN i.a_medida THEN NULL ELSE d.disponible END
    FROM cotizacion_items i
    LEFT JOIN productos p ON p.id = i.producto_id
    LEFT JOIN vw_productos_disponibles d ON d.id = i.producto_id
    WHERE i.cotizacion_id = p_id
    ORDER BY i.id;
$function$;


-- La flor comprometida que hoy no hay. Solo mientras el evento sigue vivo.
CREATE OR REPLACE FUNCTION sp_cot_c_faltantes(p_id integer)
RETURNS TABLE(
    producto_id integer, emoji text, producto text,
    comprometido integer, disponible integer, faltante integer)
LANGUAGE sql
STABLE
AS $function$
    SELECT i.producto_id, p.emoji, p.nombre,
           sum(i.cantidad)::int,
           COALESCE(max(d.disponible), 0)::int,
           (sum(i.cantidad) - COALESCE(max(d.disponible), 0))::int
    FROM cotizacion_items i
    JOIN cotizaciones c ON c.id = i.cotizacion_id
    JOIN productos p ON p.id = i.producto_id
    LEFT JOIN vw_productos_disponibles d ON d.id = i.producto_id
    WHERE i.cotizacion_id = p_id
      AND NOT i.a_medida
      AND c.estado IN ('borrador', 'aprobada')
    GROUP BY i.producto_id, p.emoji, p.nombre
    HAVING sum(i.cantidad) > COALESCE(max(d.disponible), 0)
    ORDER BY p.nombre;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_c_pagos(p_id integer)
RETURNS TABLE(
    id integer, monto integer, medio_pago text, fecha timestamp with time zone,
    venta_id integer, venta_folio text, usuario text, notas text, anulado boolean,
    anulado_en timestamp with time zone, motivo_anulacion text)
LANGUAGE sql
STABLE
AS $function$
    SELECT g.id, g.monto, g.medio_pago::text, g.fecha, g.venta_id, v.folio,
           u.nombre, g.notas, g.anulado, g.anulado_en, g.motivo_anulacion
    FROM cotizacion_pagos g
    LEFT JOIN ventas v ON v.id = g.venta_id
    LEFT JOIN usuarios u ON u.id = g.usuario_id
    WHERE g.cotizacion_id = p_id
    ORDER BY g.fecha, g.id;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_c_cuotas(p_id integer)
RETURNS TABLE(
    id integer, numero integer, monto integer, vence date, notas text,
    cubierta boolean, dias_para_vencer integer)
LANGUAGE sql
STABLE
AS $function$
    SELECT q.id, q.numero, q.monto, q.vence, q.notas,
           sum(q.monto) OVER (ORDER BY q.numero) <= r.abono,
           (q.vence - CURRENT_DATE)
    FROM cotizacion_cuotas q
    JOIN vw_cot_resumen r ON r.id = q.cotizacion_id
    WHERE q.cotizacion_id = p_id
    ORDER BY q.numero;
$function$;


-- Cómo resultó: solo tiene sentido una vez cobrada.
CREATE OR REPLACE FUNCTION sp_cot_c_resultado(p_id integer)
RETURNS TABLE(
    cobrado integer, boletas bigint, costo_flor integer,
    margen integer, margen_porcentaje numeric)
LANGUAGE sql
STABLE
AS $function$
    WITH v AS (
        SELECT ve.id, ve.total FROM ventas ve
        WHERE ve.cotizacion_id = p_id AND NOT ve.anulada
    ),
    k AS (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) AS costo
        FROM venta_consumos vc WHERE vc.venta_id IN (SELECT id FROM v)
    )
    SELECT COALESCE(sum(v.total), 0)::int,
           count(v.id),
           ROUND(max(k.costo))::int,
           (COALESCE(sum(v.total), 0) - ROUND(max(k.costo)))::int,
           CASE WHEN COALESCE(sum(v.total), 0) > 0
                THEN ROUND((sum(v.total) - max(k.costo)) * 100.0 / sum(v.total), 1)
                ELSE 0 END
    FROM k LEFT JOIN v ON true
    WHERE EXISTS (SELECT 1 FROM cotizaciones c WHERE c.id = p_id AND c.estado = 'cobrada');
$function$;


-- Las líneas sugeridas para la boleta final. NO cobra.
CREATE OR REPLACE FUNCTION sp_cot_c_preparar_cobro(p_id integer)
RETURNS TABLE(
    producto_id integer, nombre text, cantidad integer, precio integer,
    es_servicio boolean, disponible integer)
LANGUAGE sql
STABLE
AS $function$
    SELECT i.producto_id, i.nombre, i.cantidad, i.precio, i.a_medida,
           CASE WHEN i.a_medida THEN NULL ELSE d.disponible END
    FROM cotizacion_items i
    LEFT JOIN vw_productos_disponibles d ON d.id = i.producto_id
    WHERE i.cotizacion_id = p_id
    UNION ALL
    SELECT NULL, 'Traslado', 1, c.traslado, true, NULL
    FROM cotizaciones c WHERE c.id = p_id AND c.traslado > 0
    UNION ALL
    SELECT NULL, 'Montaje', 1, c.montaje, true, NULL
    FROM cotizaciones c WHERE c.id = p_id AND c.montaje > 0;
$function$;


-- ─── Escritura ───────────────────────────────────────────────────────

-- Reemplaza las líneas y recalcula el total. El precio de catálogo lo pone
-- la base: un presupuesto con precios inventados es una promesa que
-- después no se puede cumplir. Solo lo hecho a medida trae precio propio.
CREATE OR REPLACE FUNCTION fn_cot_guardar_items(p_id integer, p_items jsonb)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    it        jsonb;
    v_n       int := 0;
    v_cant    int;
    v_medida  boolean;
    v_nombre  text;
    v_precio  int;
    v_prod    record;
    v_suma    int := 0;
BEGIN
    IF p_items IS NULL OR jsonb_typeof(p_items) <> 'array' OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'El presupuesto necesita al menos una línea.';
    END IF;

    DELETE FROM cotizacion_items WHERE cotizacion_id = p_id;

    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n      := v_n + 1;
        v_cant   := COALESCE((it->>'cantidad')::int, 0);
        v_medida := COALESCE((it->>'aMedida')::boolean, false);

        IF v_cant < 1 THEN
            RAISE EXCEPTION 'Línea %: la cantidad debe ser al menos 1.', v_n;
        END IF;

        IF v_medida THEN
            v_nombre := nullif(btrim(COALESCE(it->>'nombre', '')), '');
            v_precio := COALESCE((it->>'precio')::int, -1);

            IF v_nombre IS NULL THEN
                RAISE EXCEPTION 'Línea %: una línea a medida necesita un nombre.', v_n;
            END IF;
            IF v_precio < 0 THEN
                RAISE EXCEPTION 'Línea %: indica el precio de "%".', v_n, v_nombre;
            END IF;

            INSERT INTO cotizacion_items (cotizacion_id, producto_id, nombre, precio, cantidad, a_medida)
            VALUES (p_id, NULL, v_nombre, v_precio, v_cant, true);
        ELSE
            SELECT id, nombre, precio, activo INTO v_prod
              FROM productos WHERE id = (it->>'productoId')::int;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
            END IF;
            IF NOT v_prod.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_prod.nombre;
            END IF;

            v_precio := v_prod.precio;

            INSERT INTO cotizacion_items (cotizacion_id, producto_id, nombre, precio, cantidad, a_medida)
            VALUES (p_id, v_prod.id, v_prod.nombre, v_precio, v_cant, false);
        END IF;

        v_suma := v_suma + v_precio * v_cant;
    END LOOP;

    UPDATE cotizaciones
       SET total = v_suma + traslado + montaje,
           actualizado_en = now()
     WHERE id = p_id;

    RETURN v_suma;
END;
$function$;


CREATE OR REPLACE FUNCTION fn_cot_validar_cabecera(
    p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text,
    p_traslado integer, p_montaje integer)
RETURNS void
LANGUAGE plpgsql
AS $function$
BEGIN
    IF length(btrim(COALESCE(p_cliente_nombre, ''))) < 2 THEN
        RAISE EXCEPTION 'Indica a nombre de quién va el evento.';
    END IF;
    IF btrim(COALESCE(p_tipo_evento, '')) = '' THEN
        RAISE EXCEPTION 'Indica el tipo de evento.';
    END IF;
    IF COALESCE(p_traslado, 0) < 0 OR COALESCE(p_montaje, 0) < 0 THEN
        RAISE EXCEPTION 'El traslado y el montaje no pueden ser negativos.';
    END IF;
    IF p_cliente_id IS NOT NULL AND NOT EXISTS (SELECT 1 FROM clientes WHERE id = p_cliente_id) THEN
        RAISE EXCEPTION 'El cliente indicado no existe.';
    END IF;
END;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_i_cotizacion(
    p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text, p_fecha_evento date,
    p_contacto text, p_traslado integer, p_montaje integer, p_notas text,
    p_items jsonb, p_usuario_id integer)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_id    int;
    v_folio text;
BEGIN
    IF p_usuario_id IS NULL OR p_usuario_id <= 0 THEN
        RAISE EXCEPTION 'Sesión inválida.';
    END IF;

    PERFORM fn_cot_validar_cabecera(p_cliente_id, p_cliente_nombre, p_tipo_evento,
                                    p_traslado, p_montaje);

    -- El folio es correlativo por año. El candado evita que dos personas
    -- guardando a la vez saquen el mismo número.
    PERFORM pg_advisory_xact_lock(hashtext('cotizaciones.folio'));

    SELECT 'COT-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad((count(*) + 1)::text, 4, '0')
      INTO v_folio
      FROM cotizaciones WHERE extract(year FROM creado_en) = extract(year FROM CURRENT_DATE);

    INSERT INTO cotizaciones (
        folio, cliente_id, cliente_nombre, tipo_evento, fecha_evento, contacto,
        traslado, montaje, total, abono, estado, notas, creada_por
    )
    VALUES (
        v_folio, p_cliente_id, btrim(p_cliente_nombre), btrim(p_tipo_evento), p_fecha_evento,
        nullif(btrim(COALESCE(p_contacto, '')), ''),
        COALESCE(p_traslado, 0), COALESCE(p_montaje, 0), 0, 0, 'borrador',
        nullif(btrim(COALESCE(p_notas, '')), ''), p_usuario_id
    )
    RETURNING id INTO v_id;

    PERFORM fn_cot_guardar_items(v_id, p_items);

    RETURN v_id;
END;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_u_cotizacion(
    p_id integer, p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text,
    p_fecha_evento date, p_contacto text, p_traslado integer, p_montaje integer,
    p_notas text, p_items jsonb)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_c      record;
    v_abono  int;
    v_total  int;
BEGIN
    SELECT * INTO v_c FROM cotizaciones WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;

    -- Aprobado, el cliente ya tiene un número en la mano.
    IF v_c.estado <> 'borrador' THEN
        RAISE EXCEPTION 'Solo se edita un borrador. % está %.', v_c.folio, v_c.estado;
    END IF;

    PERFORM fn_cot_validar_cabecera(p_cliente_id, p_cliente_nombre, p_tipo_evento,
                                    p_traslado, p_montaje);

    UPDATE cotizaciones SET
        cliente_id     = p_cliente_id,
        cliente_nombre = btrim(p_cliente_nombre),
        tipo_evento    = btrim(p_tipo_evento),
        fecha_evento   = p_fecha_evento,
        contacto       = nullif(btrim(COALESCE(p_contacto, '')), ''),
        traslado       = COALESCE(p_traslado, 0),
        montaje        = COALESCE(p_montaje, 0),
        notas          = nullif(btrim(COALESCE(p_notas, '')), ''),
        actualizado_en = now()
    WHERE id = p_id;

    PERFORM fn_cot_guardar_items(p_id, p_items);

    -- Lo ya abonado no puede quedar por encima del presupuesto nuevo.
    SELECT abono, saldo + abono INTO v_abono, v_total FROM vw_cot_resumen WHERE id = p_id;
    IF v_abono > v_total THEN
        RAISE EXCEPTION 'El presupuesto quedaría en $% y ya se abonaron $%.', v_total, v_abono;
    END IF;

    -- Un plan de cuotas armado sobre el total anterior ya no cuadra.
    IF EXISTS (SELECT 1 FROM cotizacion_cuotas WHERE cotizacion_id = p_id)
       AND (SELECT COALESCE(sum(monto), 0) FROM cotizacion_cuotas WHERE cotizacion_id = p_id)
           <> v_total - v_abono THEN
        DELETE FROM cotizacion_cuotas WHERE cotizacion_id = p_id;
    END IF;

    RETURN p_id;
END;
$function$;


CREATE OR REPLACE FUNCTION sp_cot_u_aprobar(p_id integer)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_c record;
BEGIN
    SELECT * INTO v_c FROM cotizaciones WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;
    IF v_c.estado <> 'borrador' THEN
        RAISE EXCEPTION '% ya está %.', v_c.folio, v_c.estado;
    END IF;
    -- Aprobar es ponerlo en la agenda: sin fecha no hay dónde ponerlo.
    IF v_c.fecha_evento IS NULL THEN
        RAISE EXCEPTION 'Indica la fecha del evento antes de aprobar.';
    END IF;
    IF v_c.total <= 0 THEN
        RAISE EXCEPTION 'El presupuesto está en $0.';
    END IF;

    UPDATE cotizaciones SET estado = 'aprobada', actualizado_en = now() WHERE id = p_id;
    RETURN p_id;
END;
$function$;


-- Los abonos recibidos NO se tocan: esa plata entró en turnos que quizás ya
-- se cerraron. Quedan como saldo a favor; si hay que devolverla, se anula
-- cada abono (y su boleta) por separado.
CREATE OR REPLACE FUNCTION sp_cot_u_anular(p_id integer, p_motivo text, p_usuario_id integer)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_c record;
BEGIN
    IF length(btrim(COALESCE(p_motivo, ''))) < 5 THEN
        RAISE EXCEPTION 'Explica el motivo, con al menos 5 caracteres.';
    END IF;

    SELECT * INTO v_c FROM cotizaciones WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;
    IF v_c.estado NOT IN ('borrador', 'aprobada') THEN
        RAISE EXCEPTION '% ya está %.', v_c.folio, v_c.estado;
    END IF;

    UPDATE cotizaciones SET
        estado = 'anulada',
        motivo_anulacion = btrim(p_motivo),
        anulada_por = p_usuario_id,
        anulada_en = now(),
        actualizado_en = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$function$;


-- El abono es una boleta de servicio en la caja abierta: sin caja no hay
-- dónde registrar la plata, y una boleta sin caja no aparecería en ningún
-- arqueo.
CREATE OR REPLACE FUNCTION sp_cot_i_pago(
    p_id integer, p_monto integer, p_medio_pago text, p_recibido integer,
    p_notas text, p_usuario_id integer)
RETURNS TABLE(
    o_pago_id integer, o_venta_id integer, o_venta_folio text,
    o_monto integer, o_vuelto integer, o_saldo integer)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_c        record;
    v_saldo    int;
    v_caja     int;
    v_venta    int;
    v_folio    text;
    v_atencion text;
    v_iva_tasa numeric;
    v_neto     int;
    v_medio    medio_pago;
    v_pago     int;
    v_vuelto   int;
BEGIN
    IF p_usuario_id IS NULL OR p_usuario_id <= 0 THEN
        RAISE EXCEPTION 'Sesión inválida.';
    END IF;

    SELECT * INTO v_c FROM cotizaciones WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;
    IF v_c.estado NOT IN ('borrador', 'aprobada') THEN
        RAISE EXCEPTION 'No se abona una cotización %.', v_c.estado;
    END IF;

    SELECT saldo INTO v_saldo FROM vw_cot_resumen WHERE id = p_id;

    IF COALESCE(p_monto, 0) < 1 THEN
        RAISE EXCEPTION 'El monto debe ser mayor que cero.';
    END IF;
    IF p_monto > v_saldo THEN
        RAISE EXCEPTION 'El saldo pendiente es $% y estás abonando $%.', v_saldo, p_monto;
    END IF;

    BEGIN
        v_medio := p_medio_pago::medio_pago;
    EXCEPTION WHEN invalid_text_representation THEN
        RAISE EXCEPTION 'Medio de pago desconocido: %.', p_medio_pago;
    END;

    IF v_medio = 'efectivo' AND p_recibido IS NOT NULL AND p_recibido < p_monto THEN
        RAISE EXCEPTION 'Recibiste $% y el abono es $%.', p_recibido, p_monto;
    END IF;

    SELECT id INTO v_caja FROM cajas WHERE estado = 'abierta';
    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay caja abierta. Abre el turno antes de recibir un abono.';
    END IF;

    -- Mismo folio y número de atención que cualquier boleta.
    SELECT 'B-' || to_char(CURRENT_DATE, 'YYYY') || '-' || lpad((count(*) + 1)::text, 5, '0')
      INTO v_folio
      FROM ventas WHERE extract(year FROM creado_en) = extract(year FROM CURRENT_DATE);

    SELECT lpad((count(*) + 1)::text, 3, '0') INTO v_atencion
      FROM ventas WHERE creado_en::date = CURRENT_DATE;

    -- El IVA va incluido: el neto se desarma del total, no se suma encima.
    v_iva_tasa := fn_ven_config('venta', 'iva', 19);
    v_neto     := ROUND(p_monto / (1 + COALESCE(v_iva_tasa, 19) / 100.0));
    v_vuelto   := CASE WHEN v_medio = 'efectivo' AND p_recibido IS NOT NULL
                       THEN p_recibido - p_monto END;

    -- Sin puntos: los gana la compra, y la compra se cierra en el cobro final.
    INSERT INTO ventas (
        folio, numero_atencion, caja_id, usuario_id, cliente_id, cotizacion_id,
        iva_tasa, medio_pago, bruto, descuento_total, neto, iva_monto, total,
        recibido, vuelto
    )
    VALUES (
        v_folio, v_atencion, v_caja, p_usuario_id, v_c.cliente_id, p_id,
        COALESCE(v_iva_tasa, 19), v_medio, p_monto, 0, v_neto, p_monto - v_neto, p_monto,
        CASE WHEN v_medio = 'efectivo' THEN p_recibido END, v_vuelto
    )
    RETURNING id INTO v_venta;

    INSERT INTO venta_items (venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio)
    VALUES (v_venta, NULL, 'Abono ' || v_c.folio || ' · ' || v_c.tipo_evento, p_monto, 1, p_monto, true);

    INSERT INTO cotizacion_pagos (cotizacion_id, venta_id, monto, medio_pago, fecha, usuario_id, notas, anulado)
    VALUES (p_id, v_venta, p_monto, v_medio, now(), p_usuario_id,
            nullif(btrim(COALESCE(p_notas, '')), ''), false)
    RETURNING id INTO v_pago;

    -- La columna `abono` es un espejo de la suma: se mantiene al día para
    -- quien la lea directo, pero la verdad está en cotizacion_pagos.
    UPDATE cotizaciones SET abono = abono + p_monto, actualizado_en = now() WHERE id = p_id;

    RETURN QUERY SELECT v_pago, v_venta, v_folio, p_monto, v_vuelto, v_saldo - p_monto;
END;
$function$;


-- Anula el abono y su boleta a la vez. La boleta se anula con la misma
-- función que cualquier venta, así rigen sus reglas: por ejemplo, no se
-- puede si la caja de ese día ya se cerró.
CREATE OR REPLACE FUNCTION sp_cot_u_anular_pago(
    p_id integer, p_pago_id integer, p_motivo text, p_usuario_id integer)
RETURNS TABLE(o_pago_id integer, o_venta_folio text, o_monto integer)
LANGUAGE plpgsql
AS $function$
DECLARE
    v_g     record;
    v_folio text;
BEGIN
    IF length(btrim(COALESCE(p_motivo, ''))) < 5 THEN
        RAISE EXCEPTION 'Explica el motivo, con al menos 5 caracteres.';
    END IF;

    SELECT g.*, c.estado AS estado_cot
      INTO v_g
      FROM cotizacion_pagos g
      JOIN cotizaciones c ON c.id = g.cotizacion_id
     WHERE g.id = p_pago_id AND g.cotizacion_id = p_id
       FOR UPDATE OF g;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Ese abono no es de esta cotización.';
    END IF;
    IF v_g.anulado THEN
        RAISE EXCEPTION 'Ese abono ya estaba anulado.';
    END IF;
    IF v_g.estado_cot = 'cobrada' THEN
        RAISE EXCEPTION 'La cotización ya se cobró: su cuenta está cerrada.';
    END IF;

    IF v_g.venta_id IS NOT NULL THEN
        SELECT o_folio INTO v_folio
          FROM sp_ven_u_venta_anular(v_g.venta_id, btrim(p_motivo), p_usuario_id);
    END IF;

    UPDATE cotizacion_pagos SET
        anulado = true,
        anulado_en = now(),
        motivo_anulacion = btrim(p_motivo)
    WHERE id = p_pago_id;

    UPDATE cotizaciones
       SET abono = GREATEST(abono - v_g.monto, 0), actualizado_en = now()
     WHERE id = p_id;

    RETURN QUERY SELECT p_pago_id, v_folio, v_g.monto;
END;
$function$;


-- Reemplaza el plan completo. Las cuotas tienen que sumar exactamente el
-- saldo: un plan incompleto significa que el último pago va a ser una
-- sorpresa. Una lista vacía borra el plan.
CREATE OR REPLACE FUNCTION sp_cot_u_cuotas(p_id integer, p_cuotas jsonb)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_c     record;
    v_saldo int;
    v_suma  int;
    v_n     int;
BEGIN
    SELECT * INTO v_c FROM cotizaciones WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;
    IF v_c.estado NOT IN ('borrador', 'aprobada') THEN
        RAISE EXCEPTION 'No se cambia el plan de una cotización %.', v_c.estado;
    END IF;

    DELETE FROM cotizacion_cuotas WHERE cotizacion_id = p_id;

    IF p_cuotas IS NULL OR jsonb_array_length(p_cuotas) = 0 THEN
        RETURN 0;
    END IF;

    IF EXISTS (SELECT 1 FROM jsonb_array_elements(p_cuotas) q
                WHERE COALESCE((q->>'monto')::int, 0) < 1) THEN
        RAISE EXCEPTION 'Cada cuota tiene que ser de al menos $1.';
    END IF;
    IF EXISTS (SELECT 1 FROM jsonb_array_elements(p_cuotas) q
                WHERE nullif(q->>'vence', '') IS NULL) THEN
        RAISE EXCEPTION 'Cada cuota necesita su fecha de vencimiento.';
    END IF;

    SELECT saldo INTO v_saldo FROM vw_cot_resumen WHERE id = p_id;
    SELECT sum((q->>'monto')::int) INTO v_suma FROM jsonb_array_elements(p_cuotas) q;

    IF v_suma <> v_saldo THEN
        RAISE EXCEPTION 'Las cuotas suman $% y el saldo es $%.', v_suma, v_saldo;
    END IF;

    -- Numeradas por fecha: la primera es la que vence antes.
    INSERT INTO cotizacion_cuotas (cotizacion_id, numero, monto, vence, notas)
    SELECT p_id,
           row_number() OVER (ORDER BY (q->>'vence')::date, ord),
           (q->>'monto')::int,
           (q->>'vence')::date,
           nullif(btrim(COALESCE(q->>'notas', '')), '')
    FROM jsonb_array_elements(p_cuotas) WITH ORDINALITY AS t(q, ord);

    GET DIAGNOSTICS v_n = ROW_COUNT;
    UPDATE cotizaciones SET actualizado_en = now() WHERE id = p_id;
    RETURN v_n;
END;
$function$;


-- Reparte el saldo en cuotas iguales; la diferencia del redondeo va en la
-- primera: es mejor cobrar el peso de más al principio que descubrirlo al
-- final.
CREATE OR REPLACE FUNCTION sp_cot_i_generar_cuotas(
    p_id integer, p_cantidad integer, p_primer_vencimiento date, p_cada_dias integer)
RETURNS integer
LANGUAGE plpgsql
AS $function$
DECLARE
    v_saldo  int;
    v_base   int;
    v_resto  int;
    v_primer date;
    v_lista  jsonb := '[]'::jsonb;
BEGIN
    IF COALESCE(p_cantidad, 0) < 1 OR p_cantidad > 60 THEN
        RAISE EXCEPTION 'Entre 1 y 60 cuotas.';
    END IF;
    IF COALESCE(p_cada_dias, 0) < 1 OR p_cada_dias > 365 THEN
        RAISE EXCEPTION 'El espacio entre cuotas va de 1 a 365 días.';
    END IF;

    SELECT saldo INTO v_saldo FROM vw_cot_resumen WHERE id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La cotización no existe.';
    END IF;
    IF v_saldo <= 0 THEN
        RAISE EXCEPTION 'No hay saldo pendiente que repartir.';
    END IF;
    IF p_cantidad > v_saldo THEN
        RAISE EXCEPTION 'No se reparten $% en % cuotas.', v_saldo, p_cantidad;
    END IF;

    v_base   := v_saldo / p_cantidad;
    v_resto  := v_saldo - v_base * p_cantidad;
    v_primer := COALESCE(p_primer_vencimiento, CURRENT_DATE);

    SELECT jsonb_agg(jsonb_build_object(
               'monto', v_base + CASE WHEN n = 1 THEN v_resto ELSE 0 END,
               'vence', (v_primer + (n - 1) * p_cada_dias)::text))
      INTO v_lista
      FROM generate_series(1, p_cantidad) n;

    RETURN sp_cot_u_cuotas(p_id, v_lista);
END;
$function$;
