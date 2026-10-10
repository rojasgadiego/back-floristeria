--
-- PostgreSQL database dump
--

\restrict WQ8occa8xgm7FDCaCYtfgU76WadLqGaj4C9Kq0utauPtLLsXBJGxzCM2u6Xcbd6

-- Dumped from database version 17.11 (Debian 17.11-1.pgdg13+2)
-- Dumped by pg_dump version 17.11 (Debian 17.11-1.pgdg13+2)

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- Name: public; Type: SCHEMA; Schema: -; Owner: -
--

-- *not* creating schema, since initdb creates it


--
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON SCHEMA public IS '';


--
-- Name: pg_trgm; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;


--
-- Name: EXTENSION pg_trgm; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pg_trgm IS 'text similarity measurement and index searching based on trigrams';


--
-- Name: pgcrypto; Type: EXTENSION; Schema: -; Owner: -
--

CREATE EXTENSION IF NOT EXISTS pgcrypto WITH SCHEMA public;


--
-- Name: EXTENSION pgcrypto; Type: COMMENT; Schema: -; Owner: -
--

COMMENT ON EXTENSION pgcrypto IS 'cryptographic functions';


--
-- Name: alcance_promocion; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.alcance_promocion AS ENUM (
    'boleta',
    'categoria',
    'producto'
);


--
-- Name: calidad_reingreso; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.calidad_reingreso AS ENUM (
    'optima',
    'buena',
    'limitada'
);


--
-- Name: destino_merma; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.destino_merma AS ENUM (
    'perdida',
    'reingreso',
    'devolucion_proveedor'
);


--
-- Name: estado_caja; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.estado_caja AS ENUM (
    'abierta',
    'cerrada'
);


--
-- Name: estado_compra; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.estado_compra AS ENUM (
    'borrador',
    'recibida',
    'anulada'
);


--
-- Name: estado_cotizacion; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.estado_cotizacion AS ENUM (
    'borrador',
    'aprobada',
    'cobrada',
    'anulada'
);


--
-- Name: estado_lote; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.estado_lote AS ENUM (
    'activo',
    'agotado',
    'descartado'
);


--
-- Name: medio_pago; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.medio_pago AS ENUM (
    'efectivo',
    'debito',
    'credito',
    'transferencia'
);


--
-- Name: rol_usuario; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.rol_usuario AS ENUM (
    'admin',
    'vendedor',
    'bodega'
);


--
-- Name: tipo_consumo; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.tipo_consumo AS ENUM (
    'listo',
    'simple'
);


--
-- Name: tipo_movimiento; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.tipo_movimiento AS ENUM (
    'alta',
    'entrada',
    'salida',
    'ajuste',
    'venta',
    'consumo',
    'armado',
    'merma',
    'baja',
    'traspaso'
);


--
-- Name: tipo_presentacion; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.tipo_presentacion AS ENUM (
    'vara',
    'paquete',
    'caja'
);


--
-- Name: tipo_producto; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.tipo_producto AS ENUM (
    'simple',
    'armado'
);


--
-- Name: tipo_promocion; Type: TYPE; Schema: public; Owner: -
--

CREATE TYPE public.tipo_promocion AS ENUM (
    'porcentaje',
    'monto'
);


--
-- Name: fn_abs_codigo_lote(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_abs_codigo_lote() RETURNS text
    LANGUAGE sql
    AS $$
    SELECT 'LOT-' || lpad(nextval('lotes_codigo_seq')::text, 6, '0');
$$;


--
-- Name: fn_abs_guardar_items(integer, jsonb, numeric); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_abs_guardar_items(p_compra_id integer, p_items jsonb, p_iva_tasa numeric) RETURNS void
    LANGUAGE plpgsql
    AS $$
DECLARE
    it            jsonb;
    v_producto    int;
    v_presenta    int;
    v_cantidad    int;
    v_costo       int;
    v_tipo        tipo_producto;
    v_nombre      text;
    v_por_unidad  int;
    v_neto        int;
    v_iva         int;
    v_n           int := 0;
BEGIN
    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'La compra necesita al menos una línea.';
    END IF;

    DELETE FROM compra_items WHERE compra_id = p_compra_id;

    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n        := v_n + 1;
        v_producto := (it->>'productoId')::int;
        v_presenta := (it->>'presentacionId')::int;
        v_cantidad := (it->>'cantidad')::int;
        v_costo    := COALESCE((it->>'costoUnitario')::int, 0);

        SELECT tipo, nombre INTO v_tipo, v_nombre
          FROM productos WHERE id = v_producto;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
        END IF;

        -- Un armado no se compra: se arma con lo que ya está en bodega. Y
        -- su columna stock es NULL por CHECK, así que recibirlo reventaría
        -- al intentar sumarle varas.
        IF v_tipo = 'armado' THEN
            RAISE EXCEPTION 'Línea %: % es un producto armado y no se compra, se arma.', v_n, v_nombre;
        END IF;

        IF v_cantidad < 1 THEN
            RAISE EXCEPTION 'Línea % (%): la cantidad debe ser al menos 1.', v_n, v_nombre;
        END IF;

        IF v_costo < 0 THEN
            RAISE EXCEPTION 'Línea % (%): el costo no puede ser negativo.', v_n, v_nombre;
        END IF;

        SELECT pe.paquetes * pe.varas_por_paquete INTO v_por_unidad
          FROM presentaciones pe
         WHERE pe.id = v_presenta AND pe.producto_id = v_producto;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Línea % (%): esa presentación no existe o es de otro producto.', v_n, v_nombre;
        END IF;

        INSERT INTO compra_items (
            compra_id, producto_id, presentacion_id,
            cantidad, costo_unitario, varas_totales, costo_por_vara
        )
        VALUES (
            p_compra_id, v_producto, v_presenta,
            v_cantidad, v_costo,
            v_cantidad * v_por_unidad,
            ROUND(v_costo::numeric / NULLIF(v_por_unidad, 0), 4)
        );
    END LOOP;

    SELECT COALESCE(sum(cantidad * costo_unitario), 0) INTO v_neto
      FROM compra_items WHERE compra_id = p_compra_id;

    -- ⚠️ La tasa NO se guarda: la tabla compras tiene neto, iva y total,
    -- pero no la tasa con que se calcularon. Al reabrir el borrador, el
    -- formulario vuelve a mostrar 19% aunque se haya guardado con otra.
    -- Si algún día hay compras exentas o con tasa distinta, hay que
    -- agregar la columna.
    v_iva := ROUND(v_neto * (COALESCE(p_iva_tasa, 19) / 100.0));

    UPDATE compras SET
        neto           = v_neto,
        iva            = v_iva,
        total          = v_neto + v_iva,
        actualizado_en = now()
    WHERE id = p_compra_id;
END;
$$;


--
-- Name: fn_abs_qr_lote(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_abs_qr_lote(p_lote_id integer) RETURNS text
    LANGUAGE sql STABLE
    AS $$
    SELECT 'CLB1*' || l.id
                   || '*' || l.producto_id
                   || '*' || to_char(l.fecha_ingreso, 'YYYYMMDD')
                   || '*' || l.codigo
    FROM lotes l
    WHERE l.id = p_lote_id;
$$;


--
-- Name: fn_cli_normalizar_rut(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_cli_normalizar_rut(p_rut text) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT upper(regexp_replace(coalesce(p_rut, ''), '[^0-9kK]', '', 'g'));
$$;


--
-- Name: fn_cot_guardar_items(integer, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_cot_guardar_items(p_id integer, p_items jsonb) RETURNS integer
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: fn_cot_validar_cabecera(integer, text, text, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_cot_validar_cabecera(p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text, p_traslado integer, p_montaje integer) RETURNS void
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: fn_lot_alerta(integer, boolean, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_lot_alerta(p_dias_para_vencer integer, p_rezagado boolean, p_dias_aviso integer DEFAULT 3) RETURNS text
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT CASE
        WHEN p_dias_para_vencer IS NOT NULL AND p_dias_para_vencer < 0 THEN 'vencido'
        WHEN p_dias_para_vencer IS NOT NULL AND p_dias_para_vencer <= p_dias_aviso THEN 'por vencer'
        WHEN p_rezagado THEN 'resto por liquidar'
        ELSE 'normal'
    END;
$$;


--
-- Name: fn_lot_precio_por_definir(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_lot_precio_por_definir() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    NEW.precio_unitario := NULLIF(NEW.precio_unitario, 0);
    RETURN NEW;
END;
$$;


--
-- Name: fn_mer_costo_ref(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_mer_costo_ref(p_producto_id integer) RETURNS integer
    LANGUAGE sql STABLE
    AS $$
    SELECT ROUND(COALESCE(
        -- Lo que hay hoy en bodega, ponderado por varas.
        (SELECT sum(l.varas_disponibles * l.costo_por_vara)
                / NULLIF(sum(l.varas_disponibles), 0)
           FROM lotes l
          WHERE l.producto_id = p_producto_id
            AND l.estado = 'activo' AND l.varas_disponibles > 0),
        -- Sin lotes vivos: el último que entró.
        (SELECT l.costo_por_vara
           FROM lotes l
          WHERE l.producto_id = p_producto_id AND l.costo_por_vara > 0
          ORDER BY l.fecha_ingreso DESC, l.id DESC
          LIMIT 1),
        (SELECT NULLIF(p.costo, 0) FROM productos p WHERE p.id = p_producto_id),
        (SELECT p.costo_armado FROM productos p WHERE p.id = p_producto_id),
        0))::int;
$$;


--
-- Name: fn_mer_dias_vida_recuperado(public.calidad_reingreso); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_mer_dias_vida_recuperado(p_calidad public.calidad_reingreso) RETURNS integer
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT CASE p_calidad
             WHEN 'optima'   THEN 3
             WHEN 'buena'    THEN 2
             WHEN 'limitada' THEN 1
           END;
$$;


SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- Name: motivos_merma; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.motivos_merma (
    id integer NOT NULL,
    nombre text NOT NULL,
    categoria text NOT NULL,
    requiere_detalle boolean DEFAULT false NOT NULL,
    destino_sugerido public.destino_merma,
    activo boolean DEFAULT true NOT NULL,
    orden integer DEFAULT 100 NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT motivos_merma_categoria_valida CHECK ((categoria = ANY (ARRAY['natural'::text, 'accidente'::text, 'operacional'::text, 'proveedor'::text, 'comercial'::text, 'faltante'::text, 'otro'::text]))),
    CONSTRAINT motivos_merma_nombre_no_vacio CHECK ((length(btrim(nombre)) > 0))
);


--
-- Name: fn_mer_motivo(text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_mer_motivo(p_motivo text, p_detalle text) RETURNS public.motivos_merma
    LANGUAGE plpgsql STABLE
    AS $$
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
$$;


--
-- Name: fn_mer_rebaja(public.calidad_reingreso); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_mer_rebaja(p_calidad public.calidad_reingreso) RETURNS numeric
    LANGUAGE sql IMMUTABLE
    AS $$
    SELECT CASE p_calidad
             WHEN 'optima'   THEN 0.20   -- casi entera: 20% menos
             WHEN 'buena'    THEN 0.40
             WHEN 'limitada' THEN 0.60   -- se vende barato o no se vende
           END;
$$;


--
-- Name: fn_mer_umbral_autorizacion(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_mer_umbral_autorizacion() RETURNS integer
    LANGUAGE sql STABLE
    AS $$
    -- Vive en la sección venta, junto al tope del descuento: los dos son
    -- "hasta cuánto se puede sin pedir firma". La sección 'merma' queda como
    -- respaldo por si alguien la cargó a mano.
    SELECT COALESCE(
        fn_ven_config('venta', 'mermaSinAutorizacion',
                      fn_ven_config('merma', 'sinAutorizacion', 15000))::int,
        15000);
$$;


--
-- Name: fn_ven_codigo_partida(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_codigo_partida() RETURNS text
    LANGUAGE sql
    AS $$
    SELECT 'PAR-' || lpad(nextval('partidas_codigo_seq')::text, 6, '0');
$$;


--
-- Name: fn_ven_config(text, text, numeric); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_config(p_clave text, p_campo text, p_default numeric) RETURNS numeric
    LANGUAGE sql STABLE
    AS $$
    SELECT COALESCE((valor ->> p_campo)::numeric, p_default)
    FROM configuracion WHERE clave = p_clave;
$$;


--
-- Name: fn_ven_config_bool(text, text, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_config_bool(p_clave text, p_campo text, p_default boolean) RETURNS boolean
    LANGUAGE sql STABLE
    AS $$
    SELECT COALESCE((valor ->> p_campo)::boolean, p_default)
    FROM configuracion WHERE clave = p_clave;
$$;


--
-- Name: fn_ven_descuento_promo(integer, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_descuento_promo(p_promocion_id integer, p_items jsonb) RETURNS integer
    LANGUAGE plpgsql STABLE
    AS $$
DECLARE
    pr    record;
    v_base int := 0;
BEGIN
    SELECT * INTO pr FROM promociones WHERE id = p_promocion_id;
    IF NOT FOUND THEN RETURN 0; END IF;

    IF NOT pr.activa THEN RETURN 0; END IF;
    IF pr.desde IS NOT NULL AND CURRENT_DATE < pr.desde THEN RETURN 0; END IF;
    IF pr.hasta IS NOT NULL AND CURRENT_DATE > pr.hasta THEN RETURN 0; END IF;

    -- dias vacío = todos los días. Postgres numera el domingo como 0 y la
    -- tabla guarda smallint[], así que se compara contra extract(dow).
    IF array_length(pr.dias, 1) IS NOT NULL
       AND NOT (extract(dow FROM CURRENT_DATE)::smallint = ANY(pr.dias)) THEN
        RETURN 0;
    END IF;

    SELECT COALESCE(sum((it->>'subtotal')::int), 0)
      INTO v_base
      FROM jsonb_array_elements(p_items) it
      JOIN productos p ON p.id = (it->>'productoId')::int
     WHERE CASE pr.alcance
             WHEN 'boleta'    THEN true
             WHEN 'categoria' THEN p.categoria_id = pr.categoria_id
             WHEN 'producto'  THEN p.id = pr.producto_id
           END;

    IF v_base < COALESCE(pr.minimo, 0) OR v_base = 0 THEN RETURN 0; END IF;

    RETURN CASE pr.tipo
             WHEN 'porcentaje' THEN ROUND(v_base * pr.valor / 100.0)
             -- Un descuento de monto fijo no puede superar lo que se está
             -- comprando: si no, la boleta quedaría en negativo.
             WHEN 'monto'      THEN LEAST(pr.valor, v_base)
           END::int;
END;
$$;


--
-- Name: fn_ven_item_con_precio(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_item_con_precio() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT NEW.es_servicio AND NEW.precio_unitario <= 0 THEN
        RAISE EXCEPTION '% no tiene precio. Defínelo en inventario antes de venderlo.', NEW.nombre;
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: fn_ven_partida_con_precio(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.fn_ven_partida_con_precio() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF coalesce(NEW.precio_unitario, 0) <= 0 THEN
        RAISE EXCEPTION '% no tiene precio. Defínelo antes de bajarlo al mesón.',
            (SELECT nombre FROM productos WHERE id = NEW.producto_id);
    END IF;
    RETURN NEW;
END;
$$;


--
-- Name: sp_abs_c_compra(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_compra(p_id integer) RETURNS TABLE(id integer, folio text, proveedor_id integer, proveedor text, proveedor_rut text, fecha date, documento text, estado public.estado_compra, neto integer, iva integer, total integer, lineas bigint, varas_totales bigint, lotes bigint, notas text, usuario_id integer, usuario text, recibida_en timestamp with time zone, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        c.id, c.folio, c.proveedor_id, pr.nombre, pr.rut,
        c.fecha, c.documento, c.estado,
        c.neto, c.iva, c.total,
        (SELECT count(*) FROM compra_items ci WHERE ci.compra_id = c.id),
        (SELECT COALESCE(sum(ci.varas_totales), 0) FROM compra_items ci WHERE ci.compra_id = c.id),
        (SELECT count(*) FROM lotes l WHERE l.compra_id = c.id),
        c.notas, c.usuario_id, u.nombre,
        c.recibida_en, c.creado_en, 1::bigint
    FROM compras c
    JOIN proveedores pr ON pr.id = c.proveedor_id
    LEFT JOIN usuarios u ON u.id = c.usuario_id
    WHERE c.id = p_id;
$$;


--
-- Name: sp_abs_c_compra_items(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_compra_items(p_compra_id integer) RETURNS TABLE(id integer, compra_id integer, producto_id integer, producto text, emoji text, presentacion_id integer, presentacion text, tipo public.tipo_presentacion, paquetes integer, varas_por_paquete integer, cantidad integer, paquetes_totales integer, varas_totales integer, costo_unitario integer, costo_por_vara numeric, costo_anterior numeric, subtotal integer, lotes_generados bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        ci.id, ci.compra_id, ci.producto_id, p.nombre, p.emoji,
        ci.presentacion_id, pe.nombre, pe.tipo,
        pe.paquetes, pe.varas_por_paquete,
        ci.cantidad,
        ci.cantidad * pe.paquetes,
        ci.varas_totales,
        ci.costo_unitario, ci.costo_por_vara,
        ant.costo_por_vara,
        ci.cantidad * ci.costo_unitario,
        (SELECT count(*) FROM lotes l WHERE l.compra_item_id = ci.id)
    FROM compra_items ci
    JOIN productos p       ON p.id  = ci.producto_id
    JOIN presentaciones pe ON pe.id = ci.presentacion_id
    LEFT JOIN LATERAL (
        SELECT ci2.costo_por_vara
        FROM compra_items ci2
        JOIN compras c2 ON c2.id = ci2.compra_id
        WHERE ci2.producto_id = ci.producto_id
          AND ci2.compra_id  <> ci.compra_id
          AND c2.estado = 'recibida'
        ORDER BY c2.fecha DESC, c2.id DESC
        LIMIT 1
    ) ant ON true
    WHERE ci.compra_id = p_compra_id
    ORDER BY ci.id;
$$;


--
-- Name: sp_abs_c_compras(text, integer, public.estado_compra, date, date, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_compras(p_buscar text DEFAULT NULL::text, p_proveedor_id integer DEFAULT NULL::integer, p_estado public.estado_compra DEFAULT NULL::public.estado_compra, p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50) RETURNS TABLE(id integer, folio text, proveedor_id integer, proveedor text, fecha date, documento text, estado public.estado_compra, neto integer, iva integer, total integer, lineas bigint, varas_totales bigint, lotes bigint, notas text, usuario text, recibida_en timestamp with time zone, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        c.id, c.folio, c.proveedor_id, pr.nombre,
        c.fecha, c.documento, c.estado,
        c.neto, c.iva, c.total,
        (SELECT count(*) FROM compra_items ci WHERE ci.compra_id = c.id),
        (SELECT COALESCE(sum(ci.varas_totales), 0) FROM compra_items ci WHERE ci.compra_id = c.id),
        (SELECT count(*) FROM lotes l WHERE l.compra_id = c.id),
        c.notas, u.nombre, c.recibida_en, c.creado_en,
        count(*) OVER ()
    FROM compras c
    JOIN proveedores pr ON pr.id = c.proveedor_id
    LEFT JOIN usuarios u ON u.id = c.usuario_id
    WHERE (p_buscar IS NULL
           OR c.folio     ILIKE '%' || p_buscar || '%'
           OR c.documento ILIKE '%' || p_buscar || '%'
           OR pr.nombre   ILIKE '%' || p_buscar || '%')
      AND (p_proveedor_id IS NULL OR c.proveedor_id = p_proveedor_id)
      AND (p_estado       IS NULL OR c.estado       = p_estado)
      AND (p_desde        IS NULL OR c.fecha       >= p_desde)
      AND (p_hasta        IS NULL OR c.fecha       <= p_hasta)
    ORDER BY c.fecha DESC, c.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_abs_c_evolucion_costo(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_evolucion_costo(p_producto_id integer, p_limite integer DEFAULT 12) RETURNS TABLE(compra_id integer, folio text, fecha date, proveedor text, presentacion text, cantidad integer, costo_unitario integer, varas_totales integer, costo_por_vara numeric, variacion numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH historial AS (
        SELECT
            c.id, c.folio, c.fecha, pr.nombre AS proveedor,
            pe.nombre AS presentacion,
            ci.cantidad, ci.costo_unitario, ci.varas_totales, ci.costo_por_vara,
            -- lead() y no lag(): el orden es descendente, así que la fila
            -- "siguiente" es cronológicamente la anterior.
            lead(ci.costo_por_vara) OVER (ORDER BY c.fecha DESC, c.id DESC) AS previo
        FROM compra_items ci
        JOIN compras c        ON c.id  = ci.compra_id
        JOIN proveedores pr   ON pr.id = c.proveedor_id
        JOIN presentaciones pe ON pe.id = ci.presentacion_id
        WHERE ci.producto_id = p_producto_id
          AND c.estado = 'recibida'
        ORDER BY c.fecha DESC, c.id DESC
        LIMIT GREATEST(p_limite, 1)
    )
    SELECT
        h.id, h.folio, h.fecha, h.proveedor, h.presentacion,
        h.cantidad, h.costo_unitario, h.varas_totales, h.costo_por_vara,
        -- Porcentaje de cambio respecto de la compra anterior. Null en la
        -- más antigua del tramo, que no tiene con qué compararse.
        CASE WHEN h.previo IS NOT NULL AND h.previo <> 0
             THEN ROUND((h.costo_por_vara - h.previo) / h.previo * 100, 1) END
    FROM historial h;
$$;


--
-- Name: sp_abs_c_lote_qr(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_lote_qr(p_codigo text) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, precio integer, precio_ramo numeric, precio_liquidacion numeric, fecha_ingreso date, fecha_vencimiento date, dias_restantes integer, vencido boolean, varas_disponibles integer, costo_por_vara numeric, estado public.estado_lote, vendible boolean, motivo_no_vendible text)
    LANGUAGE sql STABLE
    AS $$
    WITH entrada AS (
        SELECT CASE
                 -- QR completo, cualquiera de los dos separadores: el
                 -- código legible es siempre el último campo.
                 WHEN btrim(p_codigo) LIKE 'CLB1*%'
                    THEN split_part(btrim(p_codigo), '*', 5)
                 WHEN btrim(p_codigo) LIKE 'CLB1|%'
                    THEN split_part(btrim(p_codigo), '|', 5)
                 ELSE upper(btrim(p_codigo))
               END AS cod
    )
    SELECT
        l.id, l.codigo, l.producto_id, p.nombre, p.emoji,
        p.precio, p.precio_ramo, p.precio_liquidacion,
        l.fecha_ingreso, l.fecha_vencimiento,
        CASE WHEN l.fecha_vencimiento IS NOT NULL
             THEN (l.fecha_vencimiento - CURRENT_DATE)::int END,
        COALESCE(l.fecha_vencimiento < CURRENT_DATE, false),
        l.varas_disponibles, l.costo_por_vara, l.estado,

        -- Lo que el punto de venta necesita saber en una sola pregunta.
        (l.estado = 'activo'
         AND l.varas_disponibles > 0
         AND p.activo
         AND COALESCE(l.fecha_vencimiento >= CURRENT_DATE, true)),

        -- Y por qué no, si la respuesta es no. Un "no se puede" sin razón
        -- deja al vendedor con el cliente enfrente y sin qué decirle.
        CASE
            WHEN l.estado = 'agotado'                     THEN 'Este lote ya se agotó.'
            WHEN l.estado = 'descartado'                  THEN 'Este lote fue descartado.'
            WHEN l.varas_disponibles <= 0                 THEN 'No quedan varas en este lote.'
            WHEN NOT p.activo                             THEN 'El producto está desactivado.'
            WHEN l.fecha_vencimiento < CURRENT_DATE       THEN 'Este lote venció el ' || to_char(l.fecha_vencimiento, 'DD-MM-YYYY') || '.'
        END
    FROM entrada e
    JOIN lotes l     ON l.codigo = e.cod
    JOIN productos p ON p.id = l.producto_id;
$$;


--
-- Name: sp_abs_c_lotes_compra(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_lotes_compra(p_compra_id integer) RETURNS TABLE(id integer, codigo text, qr text, producto_id integer, producto text, emoji text, presentacion text, proveedor text, fecha_ingreso date, fecha_vencimiento date, dias_restantes integer, varas_iniciales integer, varas_disponibles integer, costo_por_vara numeric, precio_unitario integer, estado public.estado_lote)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        l.id, l.codigo,
        'CLB1*' || l.id || '*' || l.producto_id
                || '*' || to_char(l.fecha_ingreso, 'YYYYMMDD')
                || '*' || l.codigo,
        l.producto_id, p.nombre, p.emoji,
        pe.nombre, pr.nombre,
        l.fecha_ingreso, l.fecha_vencimiento,
        CASE WHEN l.fecha_vencimiento IS NOT NULL
             THEN (l.fecha_vencimiento - CURRENT_DATE)::int END,
        l.varas_iniciales, l.varas_disponibles,
        l.costo_por_vara, l.precio_unitario, l.estado
    FROM lotes l
    JOIN productos p            ON p.id  = l.producto_id
    LEFT JOIN presentaciones pe ON pe.id = l.presentacion_id
    LEFT JOIN proveedores pr    ON pr.id = l.proveedor_id
    WHERE l.compra_id = p_compra_id
    ORDER BY p.nombre, l.id;
$$;


--
-- Name: sp_abs_c_lotes_resumen(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_lotes_resumen(p_compra_id integer) RETURNS TABLE(id integer, codigo text, producto text, emoji text, varas integer, fecha_vencimiento date, dias_restantes integer, estado public.estado_lote)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        l.id, l.codigo, p.nombre, p.emoji,
        l.varas_iniciales, l.fecha_vencimiento,
        CASE WHEN l.fecha_vencimiento IS NOT NULL
             THEN (l.fecha_vencimiento - CURRENT_DATE)::int END,
        l.estado
    FROM lotes l
    JOIN productos p ON p.id = l.producto_id
    WHERE l.compra_id = p_compra_id
    ORDER BY p.nombre, l.id;
$$;


--
-- Name: sp_abs_c_presentaciones(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_presentaciones(p_producto_id integer DEFAULT NULL::integer, p_activa boolean DEFAULT true) RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, nombre text, tipo public.tipo_presentacion, paquetes integer, varas_por_paquete integer, varas_totales integer, predeterminada boolean, activa boolean)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        pe.id, pe.producto_id, p.nombre, p.emoji,
        pe.nombre, pe.tipo,
        pe.paquetes, pe.varas_por_paquete,
        pe.paquetes * pe.varas_por_paquete,
        pe.predeterminada, pe.activa
    FROM presentaciones pe
    JOIN productos p ON p.id = pe.producto_id
    WHERE (p_producto_id IS NULL OR pe.producto_id = p_producto_id)
      AND (p_activa      IS NULL OR pe.activa      = p_activa)
    ORDER BY p.nombre, pe.predeterminada DESC, pe.nombre;
$$;


--
-- Name: sp_abs_c_proveedor(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_proveedor(p_id integer) RETURNS TABLE(id integer, nombre text, rut text, contacto text, telefono text, correo text, direccion text, notas text, activo boolean, compras bigint, ultima_compra date, total_comprado bigint, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        pr.id, pr.nombre, pr.rut, pr.contacto, pr.telefono,
        pr.correo, pr.direccion, pr.notas, pr.activo,
        count(c.id) FILTER (WHERE c.estado = 'recibida'),
        max(c.fecha) FILTER (WHERE c.estado = 'recibida'),
        COALESCE(sum(c.total) FILTER (WHERE c.estado = 'recibida'), 0),
        pr.creado_en,
        1::bigint
    FROM proveedores pr
    LEFT JOIN compras c ON c.proveedor_id = pr.id
    WHERE pr.id = p_id
    GROUP BY pr.id;
$$;


--
-- Name: sp_abs_c_proveedores(text, boolean, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_c_proveedores(p_buscar text DEFAULT NULL::text, p_activo boolean DEFAULT NULL::boolean, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50) RETURNS TABLE(id integer, nombre text, rut text, contacto text, telefono text, correo text, direccion text, notas text, activo boolean, compras bigint, ultima_compra date, total_comprado bigint, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        pr.id, pr.nombre, pr.rut, pr.contacto, pr.telefono,
        pr.correo, pr.direccion, pr.notas, pr.activo,
        count(c.id) FILTER (WHERE c.estado = 'recibida'),
        max(c.fecha) FILTER (WHERE c.estado = 'recibida'),
        -- Solo las recibidas: un borrador todavía no es plata gastada.
        COALESCE(sum(c.total) FILTER (WHERE c.estado = 'recibida'), 0),
        pr.creado_en,
        count(*) OVER ()
    FROM proveedores pr
    LEFT JOIN compras c ON c.proveedor_id = pr.id
    WHERE (p_buscar IS NULL
           OR pr.nombre   ILIKE '%' || p_buscar || '%'
           OR pr.rut      ILIKE '%' || p_buscar || '%'
           OR pr.contacto ILIKE '%' || p_buscar || '%')
      AND (p_activo IS NULL OR pr.activo = p_activo)
    GROUP BY pr.id
    ORDER BY pr.nombre
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_abs_d_compra_item(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_d_compra_item(p_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_compra_id int;
    v_estado    estado_compra;
    v_neto      int;
BEGIN
    SELECT ci.compra_id, c.estado INTO v_compra_id, v_estado
      FROM compra_items ci
      JOIN compras c ON c.id = ci.compra_id
     WHERE ci.id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La línea no existe.';
    END IF;

    IF v_estado <> 'borrador' THEN
        RAISE EXCEPTION 'No se pueden quitar líneas: la compra está %.', v_estado;
    END IF;

    DELETE FROM compra_items WHERE id = p_id;

    SELECT COALESCE(sum(cantidad * costo_unitario), 0) INTO v_neto
      FROM compra_items WHERE compra_id = v_compra_id;

    UPDATE compras SET
        neto           = v_neto,
        iva            = ROUND(v_neto * 0.19),
        total          = v_neto + ROUND(v_neto * 0.19),
        actualizado_en = now()
    WHERE id = v_compra_id;

    RETURN v_compra_id;
END;
$$;


--
-- Name: sp_abs_i_compra(integer, date, text, text, numeric, jsonb, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_i_compra(p_proveedor_id integer, p_fecha date, p_documento text, p_notas text, p_iva_tasa numeric, p_items jsonb, p_usuario_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id     int;
    v_fecha  date := coalesce(p_fecha, CURRENT_DATE);
    v_folio  text;
    v_n      int;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM proveedores WHERE id = p_proveedor_id) THEN
        RAISE EXCEPTION 'El proveedor no existe.';
    END IF;

    IF NOT EXISTS (SELECT 1 FROM proveedores WHERE id = p_proveedor_id AND activo) THEN
        RAISE EXCEPTION 'El proveedor está desactivado.';
    END IF;

    IF v_fecha > CURRENT_DATE + 30 THEN
        RAISE EXCEPTION 'La fecha está demasiado adelante. Revisa el año.';
    END IF;

    SELECT count(*) + 1 INTO v_n
      FROM compras
     WHERE extract(year FROM fecha) = extract(year FROM v_fecha);

    v_folio := 'OC-' || to_char(v_fecha, 'YYYY') || '-' || lpad(v_n::text, 4, '0');

    INSERT INTO compras (folio, proveedor_id, fecha, documento, estado, notas, usuario_id)
    VALUES (
        v_folio, p_proveedor_id, v_fecha,
        nullif(btrim(coalesce(p_documento, '')), ''),
        'borrador',
        nullif(btrim(coalesce(p_notas, '')), ''),
        p_usuario_id
    )
    RETURNING id INTO v_id;

    -- Si esto lanza, el INSERT de arriba se revierte solo: o queda toda la
    -- compra o no queda ninguna.
    PERFORM fn_abs_guardar_items(v_id, p_items, p_iva_tasa);

    RETURN v_id;
END;
$$;


--
-- Name: sp_abs_i_compra_item(integer, integer, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_i_compra_item(p_compra_id integer, p_producto_id integer, p_presentacion_id integer, p_cantidad integer, p_costo_unitario integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_estado        estado_compra;
    v_tipo          tipo_producto;
    v_por_unidad    int;
    v_varas         int;
    v_costo_vara    numeric;
    v_id            int;
    v_neto          int;
BEGIN
    SELECT estado INTO v_estado FROM compras WHERE id = p_compra_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La compra no existe.';
    END IF;

    IF v_estado <> 'borrador' THEN
        RAISE EXCEPTION 'No se pueden agregar líneas: la compra está %.', v_estado;
    END IF;

    SELECT tipo INTO v_tipo FROM productos WHERE id = p_producto_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    -- Un armado no se compra: se arma con lo que ya está en bodega. Y su
    -- columna stock es NULL por CHECK, así que recibirlo reventaría al
    -- intentar sumarle varas.
    IF v_tipo = 'armado' THEN
        RAISE EXCEPTION 'Un producto armado no se compra: se arma desde su receta.';
    END IF;

    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'La cantidad debe ser al menos 1.';
    END IF;

    IF coalesce(p_costo_unitario, 0) < 0 THEN
        RAISE EXCEPTION 'El costo no puede ser negativo.';
    END IF;

    SELECT pe.paquetes * pe.varas_por_paquete INTO v_por_unidad
      FROM presentaciones pe
     WHERE pe.id = p_presentacion_id AND pe.producto_id = p_producto_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'Esa presentación no existe o no pertenece a este producto.';
    END IF;

    v_varas      := p_cantidad * v_por_unidad;
    v_costo_vara := ROUND(p_costo_unitario::numeric / NULLIF(v_por_unidad, 0), 4);

    INSERT INTO compra_items (
        compra_id, producto_id, presentacion_id,
        cantidad, costo_unitario, varas_totales, costo_por_vara
    )
    VALUES (
        p_compra_id, p_producto_id, p_presentacion_id,
        p_cantidad, p_costo_unitario, v_varas, v_costo_vara
    )
    RETURNING id INTO v_id;

    SELECT COALESCE(sum(cantidad * costo_unitario), 0) INTO v_neto
      FROM compra_items WHERE compra_id = p_compra_id;

    UPDATE compras SET
        neto           = v_neto,
        iva            = ROUND(v_neto * 0.19),
        total          = v_neto + ROUND(v_neto * 0.19),
        actualizado_en = now()
    WHERE id = p_compra_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_abs_i_presentacion(integer, text, public.tipo_presentacion, integer, integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_i_presentacion(p_producto_id integer, p_nombre text, p_tipo public.tipo_presentacion, p_paquetes integer, p_varas_por_paquete integer, p_predeterminada boolean DEFAULT false) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id    int;
    v_tipo  tipo_producto;
BEGIN
    SELECT tipo INTO v_tipo FROM productos WHERE id = p_producto_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    -- Un armado no se compra: se arma con lo que ya está en bodega, así
    -- que no tiene sentido definirle presentaciones de proveedor.
    IF v_tipo = 'armado' THEN
        RAISE EXCEPTION 'Un producto armado no se compra: no lleva presentaciones.';
    END IF;

    -- Ver el comentario de cabecera.
    IF p_tipo = 'vara' THEN
        RAISE EXCEPTION 'Las presentaciones se registran por paquete o por caja, no por vara suelta.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre de la presentación.';
    END IF;

    IF coalesce(p_paquetes, 0) < 1 THEN
        RAISE EXCEPTION 'La presentación debe traer al menos un paquete.';
    END IF;

    IF coalesce(p_varas_por_paquete, 0) < 1 THEN
        RAISE EXCEPTION 'Cada paquete debe traer al menos una vara.';
    END IF;

    -- Cada paquete es una etiqueta. Doscientos por presentación ya es
    -- improbable; mil sería un dedo gordo tipeando.
    IF p_paquetes > 200 THEN
        RAISE EXCEPTION 'Doscientos paquetes por presentación es demasiado. Revisa el dato.';
    END IF;

    IF EXISTS (SELECT 1 FROM presentaciones
                WHERE producto_id = p_producto_id
                  AND lower(nombre) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ese producto ya tiene una presentación llamada %.', btrim(p_nombre);
    END IF;

    -- Solo una predeterminada por producto: es la que el formulario de
    -- compra elige sola, y dos candidatas dejarían la elección al azar.
    IF p_predeterminada THEN
        UPDATE presentaciones SET predeterminada = false
         WHERE producto_id = p_producto_id;
    END IF;

    INSERT INTO presentaciones (
        producto_id, nombre, tipo, paquetes, varas_por_paquete, predeterminada, activa
    )
    VALUES (
        p_producto_id, btrim(p_nombre), p_tipo,
        p_paquetes, p_varas_por_paquete,
        coalesce(p_predeterminada, false), true
    )
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_abs_i_proveedor(text, text, text, text, text, text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_i_proveedor(p_nombre text, p_rut text DEFAULT NULL::text, p_contacto text DEFAULT NULL::text, p_telefono text DEFAULT NULL::text, p_correo text DEFAULT NULL::text, p_direccion text DEFAULT NULL::text, p_notas text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id int;
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del proveedor.';
    END IF;

    IF EXISTS (SELECT 1 FROM proveedores
                WHERE lower(nombre) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ya existe un proveedor con el nombre %.', btrim(p_nombre);
    END IF;

    -- El RUT es opcional, pero si viene no puede repetirse: dos fichas del
    -- mismo proveedor parten el historial de compras en dos.
    IF nullif(btrim(coalesce(p_rut, '')), '') IS NOT NULL
       AND EXISTS (SELECT 1 FROM proveedores WHERE rut = btrim(p_rut)) THEN
        RAISE EXCEPTION 'Ya existe un proveedor con el RUT %.', btrim(p_rut);
    END IF;

    INSERT INTO proveedores (nombre, rut, contacto, telefono, correo, direccion, notas, activo)
    VALUES (
        btrim(p_nombre),
        nullif(btrim(coalesce(p_rut, '')), ''),
        nullif(btrim(coalesce(p_contacto, '')), ''),
        nullif(btrim(coalesce(p_telefono, '')), ''),
        nullif(btrim(coalesce(p_correo, '')), ''),
        nullif(btrim(coalesce(p_direccion, '')), ''),
        nullif(btrim(coalesce(p_notas, '')), ''),
        true
    )
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_abs_u_compra(integer, integer, date, text, text, numeric, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_compra(p_id integer, p_proveedor_id integer, p_fecha date, p_documento text, p_notas text, p_iva_tasa numeric, p_items jsonb) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_estado estado_compra;
    v_folio  text;
BEGIN
    SELECT estado, folio INTO v_estado, v_folio
      FROM compras WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La compra no existe.';
    END IF;

    IF v_estado <> 'borrador' THEN
        RAISE EXCEPTION 'Solo se puede editar una compra en borrador. % está %.', v_folio, v_estado;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM proveedores WHERE id = p_proveedor_id AND activo) THEN
        RAISE EXCEPTION 'El proveedor no existe o está desactivado.';
    END IF;

    UPDATE compras SET
        proveedor_id   = p_proveedor_id,
        fecha          = coalesce(p_fecha, fecha),
        documento      = nullif(btrim(coalesce(p_documento, '')), ''),
        notas          = nullif(btrim(coalesce(p_notas, '')), ''),
        actualizado_en = now()
    WHERE id = p_id;

    PERFORM fn_abs_guardar_items(p_id, p_items, p_iva_tasa);

    RETURN p_id;
END;
$$;


--
-- Name: sp_abs_u_compra_anular(integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_compra_anular(p_compra_id integer, p_usuario_id integer DEFAULT NULL::integer, p_motivo text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_estado estado_compra;
    v_folio  text;
BEGIN
    SELECT estado, folio INTO v_estado, v_folio
      FROM compras WHERE id = p_compra_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La compra no existe.';
    END IF;

    IF v_estado = 'anulada' THEN
        RAISE EXCEPTION 'La compra % ya está anulada.', v_folio;
    END IF;

    IF v_estado = 'recibida' THEN
        RAISE EXCEPTION 'La compra % ya fue recibida y generó lotes. Registra una merma o una devolución al proveedor en vez de anularla.', v_folio;
    END IF;

    UPDATE compras SET
        estado         = 'anulada',
        notas          = coalesce(notas || ' · ', '') || 'Anulada: ' || coalesce(nullif(btrim(coalesce(p_motivo, '')), ''), 'sin motivo'),
        usuario_id     = coalesce(p_usuario_id, usuario_id),
        actualizado_en = now()
    WHERE id = p_compra_id;

    RETURN p_compra_id;
END;
$$;


--
-- Name: sp_abs_u_compra_recibir(integer, integer, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_compra_recibir(p_compra_id integer, p_usuario_id integer DEFAULT NULL::integer, p_fecha_ingreso date DEFAULT NULL::date) RETURNS TABLE(o_compra_id integer, o_folio text, o_lotes_generados integer, o_varas_ingresadas integer, o_productos integer)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_estado  estado_compra;
    v_folio   text;
    v_prov    int;
    v_fecha   date := coalesce(p_fecha_ingreso, CURRENT_DATE);
    r         record;
    v_codigo  text;
    v_vence   date;
    v_varas   int;
    v_stock   int;
    v_lotes   int := 0;
    v_total   int := 0;
    v_prods   int := 0;
BEGIN
    -- FOR UPDATE: dos personas dando "recibir" al mismo tiempo generarían
    -- los lotes dos veces y duplicarían el stock.
    SELECT c.estado, c.folio, c.proveedor_id
      INTO v_estado, v_folio, v_prov
      FROM compras c WHERE c.id = p_compra_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La compra no existe.';
    END IF;

    IF v_estado = 'recibida' THEN
        RAISE EXCEPTION 'La compra % ya fue recibida.', v_folio;
    END IF;

    IF v_estado = 'anulada' THEN
        RAISE EXCEPTION 'La compra % está anulada.', v_folio;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM compra_items WHERE compra_id = p_compra_id) THEN
        RAISE EXCEPTION 'La compra no tiene líneas: no hay nada que recibir.';
    END IF;

    FOR r IN
        SELECT ci.id AS item_id, ci.producto_id, ci.presentacion_id,
               ci.cantidad, ci.varas_totales, ci.costo_por_vara,
               pe.paquetes, pe.varas_por_paquete,
               p.dias_vida, p.precio, p.controla_lotes
        FROM compra_items ci
        JOIN presentaciones pe ON pe.id = ci.presentacion_id
        JOIN productos p       ON p.id  = ci.producto_id
        WHERE ci.compra_id = p_compra_id
        ORDER BY ci.id
    LOOP
        -- varas_totales ya viene calculado desde la línea:
        -- cantidad × paquetes × varas_por_paquete.
        v_varas := r.varas_totales;

        v_vence := CASE WHEN r.dias_vida IS NOT NULL THEN v_fecha + r.dias_vida END;

        IF r.controla_lotes THEN
            v_codigo := fn_abs_codigo_lote();

            INSERT INTO lotes (
                codigo, producto_id, compra_id, compra_item_id, proveedor_id,
                presentacion_id, fecha_ingreso, fecha_vencimiento,
                varas_iniciales, varas_disponibles, costo_por_vara,
                estado, precio_unitario,
                -- Queda anotado cómo llegó: sirve al contar el balde y para
                -- entender un descuadre meses después.
                notas
            )
            VALUES (
                v_codigo, r.producto_id, p_compra_id, r.item_id, v_prov,
                r.presentacion_id, v_fecha, v_vence,
                v_varas, v_varas, r.costo_por_vara,
                'activo', r.precio,
                r.cantidad || ' × ' || r.paquetes || ' paquete(s) de ' ||
                r.varas_por_paquete || ' varas'
            );

            UPDATE productos
               SET stock = coalesce(stock, 0) + v_varas,
                   actualizado_en = now()
             WHERE id = r.producto_id
            RETURNING stock INTO v_stock;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.producto_id,
                (SELECT id FROM lotes WHERE codigo = v_codigo),
                'entrada', v_varas, v_stock,
                'Recepción de compra ' || v_folio,
                'Lote ' || v_codigo,
                p_usuario_id, 'compra', p_compra_id
            );

            v_lotes := v_lotes + 1;
        ELSE
            -- Sin lotes: entra al stock y nada más. Antes se creaba un lote
            -- igual, que quedaba fantasma (se vende del stock, nunca del
            -- lote) y el inventario lo contaba dos veces.
            --
            -- Y el costo pasa a ser el promedio ponderado entre lo que había
            -- y lo que llega: es la única fuente de costo de estos productos,
            -- y sin ella las mermas se valorizaban en cero.
            UPDATE productos
               SET costo = CASE
                       WHEN r.costo_por_vara <= 0 THEN costo
                       WHEN coalesce(stock, 0) <= 0 OR coalesce(costo, 0) <= 0
                           THEN ROUND(r.costo_por_vara)
                       ELSE ROUND((stock * costo + v_varas * r.costo_por_vara)
                                  / (stock + v_varas))
                   END,
                   stock = coalesce(stock, 0) + v_varas,
                   actualizado_en = now()
             WHERE id = r.producto_id
            RETURNING stock INTO v_stock;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.producto_id, NULL, 'entrada', v_varas, v_stock,
                'Recepción de compra ' || v_folio,
                r.cantidad || ' × ' || r.paquetes || ' × ' || r.varas_por_paquete,
                p_usuario_id, 'compra', p_compra_id
            );
        END IF;

        v_total := v_total + v_varas;
        v_prods := v_prods + 1;
    END LOOP;

    UPDATE compras SET
        estado         = 'recibida',
        recibida_en    = now(),
        usuario_id     = coalesce(p_usuario_id, usuario_id),
        actualizado_en = now()
    WHERE id = p_compra_id;

    RETURN QUERY
    SELECT p_compra_id AS compra_id, v_folio AS folio,
           v_lotes AS lotes_generados, v_total AS varas_ingresadas,
           v_prods AS productos;
END;
$$;


--
-- Name: sp_abs_u_presentacion(integer, text, public.tipo_presentacion, integer, integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_presentacion(p_id integer, p_nombre text, p_tipo public.tipo_presentacion, p_paquetes integer, p_varas_por_paquete integer, p_predeterminada boolean DEFAULT false) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_producto_id  int;
    v_paquetes     int;
    v_varas        int;
    v_usada        int;
BEGIN
    SELECT producto_id, paquetes, varas_por_paquete
      INTO v_producto_id, v_paquetes, v_varas
      FROM presentaciones
     WHERE id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La presentación no existe.';
    END IF;

    IF p_tipo = 'vara' THEN
        RAISE EXCEPTION 'Las presentaciones se registran por paquete o por caja, no por vara suelta.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre de la presentación.';
    END IF;

    IF coalesce(p_paquetes, 0) < 1 OR coalesce(p_varas_por_paquete, 0) < 1 THEN
        RAISE EXCEPTION 'Los paquetes y las varas por paquete deben ser mayores a 0.';
    END IF;

    IF p_paquetes > 200 THEN
        RAISE EXCEPTION 'Doscientos paquetes por presentación es demasiado. Revisa el dato.';
    END IF;

    IF EXISTS (SELECT 1 FROM presentaciones
                WHERE producto_id = v_producto_id
                  AND lower(nombre) = lower(btrim(p_nombre))
                  AND id <> p_id) THEN
        RAISE EXCEPTION 'Ese producto ya tiene otra presentación llamada %.', btrim(p_nombre);
    END IF;

    -- Cambiar el contenido de una presentación ya usada reescribiría el
    -- pasado: las compras viejas guardaron sus varas_totales calculadas con
    -- los números de entonces, y el costo por vara dejaría de cuadrar con
    -- lo que realmente se pagó.
    --
    -- El nombre y la predeterminada sí se pueden corregir: no cambian
    -- ningún cálculo hecho.
    SELECT count(*) INTO v_usada FROM compra_items WHERE presentacion_id = p_id;

    IF v_usada > 0
       AND (v_paquetes <> p_paquetes OR v_varas <> p_varas_por_paquete) THEN
        RAISE EXCEPTION 'No se puede cambiar el contenido: ya se usó en % compra(s). Crea una presentación nueva.', v_usada;
    END IF;

    IF p_predeterminada THEN
        UPDATE presentaciones SET predeterminada = false
         WHERE producto_id = v_producto_id AND id <> p_id;
    END IF;

    UPDATE presentaciones SET
        nombre            = btrim(p_nombre),
        tipo              = p_tipo,
        paquetes          = p_paquetes,
        varas_por_paquete = p_varas_por_paquete,
        predeterminada    = coalesce(p_predeterminada, false)
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_abs_u_presentacion_estado(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_presentacion_estado(p_id integer, p_activa boolean) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM presentaciones WHERE id = p_id) THEN
        RAISE EXCEPTION 'La presentación no existe.';
    END IF;

    UPDATE presentaciones SET activa = p_activa WHERE id = p_id;
    RETURN p_id;
END;
$$;


--
-- Name: sp_abs_u_proveedor(integer, text, text, text, text, text, text, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_proveedor(p_id integer, p_nombre text, p_rut text DEFAULT NULL::text, p_contacto text DEFAULT NULL::text, p_telefono text DEFAULT NULL::text, p_correo text DEFAULT NULL::text, p_direccion text DEFAULT NULL::text, p_notas text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM proveedores WHERE id = p_id) THEN
        RAISE EXCEPTION 'El proveedor no existe.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del proveedor.';
    END IF;

    IF EXISTS (SELECT 1 FROM proveedores
                WHERE lower(nombre) = lower(btrim(p_nombre)) AND id <> p_id) THEN
        RAISE EXCEPTION 'Ya existe otro proveedor con el nombre %.', btrim(p_nombre);
    END IF;

    IF nullif(btrim(coalesce(p_rut, '')), '') IS NOT NULL
       AND EXISTS (SELECT 1 FROM proveedores WHERE rut = btrim(p_rut) AND id <> p_id) THEN
        RAISE EXCEPTION 'Ya existe otro proveedor con el RUT %.', btrim(p_rut);
    END IF;

    UPDATE proveedores SET
        nombre         = btrim(p_nombre),
        rut            = nullif(btrim(coalesce(p_rut, '')), ''),
        contacto       = nullif(btrim(coalesce(p_contacto, '')), ''),
        telefono       = nullif(btrim(coalesce(p_telefono, '')), ''),
        correo         = nullif(btrim(coalesce(p_correo, '')), ''),
        direccion      = nullif(btrim(coalesce(p_direccion, '')), ''),
        notas          = nullif(btrim(coalesce(p_notas, '')), ''),
        actualizado_en = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_abs_u_proveedor_estado(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_abs_u_proveedor_estado(p_id integer, p_activo boolean) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_borradores int;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM proveedores WHERE id = p_id) THEN
        RAISE EXCEPTION 'El proveedor no existe.';
    END IF;

    IF NOT p_activo THEN
        SELECT count(*) INTO v_borradores
          FROM compras WHERE proveedor_id = p_id AND estado = 'borrador';

        IF v_borradores > 0 THEN
            RAISE EXCEPTION 'No se puede desactivar: tiene % compra(s) en borrador sin recibir.', v_borradores;
        END IF;
    END IF;

    UPDATE proveedores
       SET activo = p_activo, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_acc_c_usuario(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_c_usuario(p_id integer) RETURNS TABLE(id integer, nombre text, email text, rol public.rol_usuario, activo boolean, ultimo_acceso timestamp with time zone, creado_en timestamp with time zone, actualizado_en timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
    SELECT u.id, u.nombre, u.email, u.rol, u.activo,
           u.ultimo_acceso, u.creado_en, u.actualizado_en
    FROM usuarios u
    WHERE u.id = p_id;
$$;


--
-- Name: sp_acc_c_usuario_login(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_c_usuario_login(p_email text) RETURNS TABLE(id integer, nombre text, email text, password_hash text, rol public.rol_usuario, activo boolean)
    LANGUAGE sql STABLE
    AS $$
    SELECT u.id, u.nombre, u.email, u.password_hash, u.rol, u.activo
    FROM usuarios u
    WHERE lower(u.email) = lower(btrim(p_email));
$$;


--
-- Name: FUNCTION sp_acc_c_usuario_login(p_email text); Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON FUNCTION public.sp_acc_c_usuario_login(p_email text) IS 'Único punto que expone password_hash. No usar para nada más.';


--
-- Name: sp_acc_c_usuarios(text, public.rol_usuario, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_c_usuarios(p_busqueda text DEFAULT NULL::text, p_rol public.rol_usuario DEFAULT NULL::public.rol_usuario, p_activo boolean DEFAULT NULL::boolean) RETURNS TABLE(id integer, nombre text, email text, rol public.rol_usuario, activo boolean, ultimo_acceso timestamp with time zone, creado_en timestamp with time zone, actualizado_en timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
    SELECT u.id, u.nombre, u.email, u.rol, u.activo,
           u.ultimo_acceso, u.creado_en, u.actualizado_en
    FROM usuarios u
    WHERE (p_busqueda IS NULL
           OR u.nombre ILIKE '%' || p_busqueda || '%'
           OR u.email  ILIKE '%' || p_busqueda || '%')
      AND (p_rol    IS NULL OR u.rol = p_rol)
      AND (p_activo IS NULL OR u.activo = p_activo)
    ORDER BY u.nombre;
$$;


--
-- Name: sp_acc_i_usuario(text, text, text, public.rol_usuario); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_i_usuario(p_nombre text, p_email text, p_password_hash text, p_rol public.rol_usuario) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id int;
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del usuario.';
    END IF;

    IF btrim(coalesce(p_email, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el correo del usuario.';
    END IF;

    IF position('@' in p_email) = 0 THEN
        RAISE EXCEPTION 'El correo no tiene un formato válido.';
    END IF;

    IF btrim(coalesce(p_password_hash, '')) = '' THEN
        RAISE EXCEPTION 'Falta la contraseña.';
    END IF;

    IF EXISTS (SELECT 1 FROM usuarios WHERE lower(email) = lower(btrim(p_email))) THEN
        RAISE EXCEPTION 'Ya existe un usuario con el correo %.', lower(btrim(p_email));
    END IF;

    INSERT INTO usuarios (nombre, email, password_hash, rol, activo)
    VALUES (btrim(p_nombre), lower(btrim(p_email)), p_password_hash, p_rol, true)
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_acc_u_ultimo_acceso(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_u_ultimo_acceso(p_id integer) RETURNS integer
    LANGUAGE sql
    AS $$
    UPDATE usuarios
       SET ultimo_acceso = now(), actualizado_en = now()
     WHERE id = p_id
    RETURNING id;
$$;


--
-- Name: sp_acc_u_usuario(integer, text, text, public.rol_usuario); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_u_usuario(p_id integer, p_nombre text, p_email text, p_rol public.rol_usuario) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM usuarios WHERE id = p_id) THEN
        RAISE EXCEPTION 'El usuario no existe.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del usuario.';
    END IF;

    IF EXISTS (SELECT 1 FROM usuarios
                WHERE lower(email) = lower(btrim(p_email)) AND id <> p_id) THEN
        RAISE EXCEPTION 'Ya existe otro usuario con el correo %.', lower(btrim(p_email));
    END IF;

    -- Si es el último admin activo, no puede degradarse a sí mismo: la
    -- instalación quedaría sin nadie que pueda crear usuarios.
    IF p_rol <> 'admin'
       AND EXISTS (SELECT 1 FROM usuarios WHERE id = p_id AND rol = 'admin' AND activo)
       AND (SELECT count(*) FROM usuarios WHERE rol = 'admin' AND activo) = 1 THEN
        RAISE EXCEPTION 'No se puede cambiar el rol: es el único administrador activo.';
    END IF;

    UPDATE usuarios SET
        nombre         = btrim(p_nombre),
        email          = lower(btrim(p_email)),
        rol            = p_rol,
        actualizado_en = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_acc_u_usuario_estado(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_u_usuario_estado(p_id integer, p_activo boolean) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM usuarios WHERE id = p_id) THEN
        RAISE EXCEPTION 'El usuario no existe.';
    END IF;

    IF NOT p_activo
       AND EXISTS (SELECT 1 FROM usuarios WHERE id = p_id AND rol = 'admin' AND activo)
       AND (SELECT count(*) FROM usuarios WHERE rol = 'admin' AND activo) = 1 THEN
        RAISE EXCEPTION 'No se puede desactivar: es el único administrador activo.';
    END IF;

    UPDATE usuarios
       SET activo = p_activo, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_acc_u_usuario_password(integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_acc_u_usuario_password(p_id integer, p_password_hash text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM usuarios WHERE id = p_id) THEN
        RAISE EXCEPTION 'El usuario no existe.';
    END IF;

    IF btrim(coalesce(p_password_hash, '')) = '' THEN
        RAISE EXCEPTION 'Falta la contraseña.';
    END IF;

    UPDATE usuarios
       SET password_hash = p_password_hash, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_cfg_c_configuracion(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cfg_c_configuracion() RETURNS TABLE(local jsonb, ticket jsonb, venta jsonb, club jsonb, actualizado_en timestamp with time zone, actualizado_por text)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        (SELECT valor FROM configuracion WHERE clave = 'local'),
        (SELECT valor FROM configuracion WHERE clave = 'ticket'),
        (SELECT valor FROM configuracion WHERE clave = 'venta'),
        (SELECT valor FROM configuracion WHERE clave = 'club'),
        -- La más reciente de las cuatro: "cuándo se tocó la configuración"
        -- es una sola pregunta, aunque las filas sean cuatro.
        (SELECT max(actualizado_en) FROM configuracion),
        (SELECT u.nombre FROM configuracion c
         LEFT JOIN usuarios u ON u.id = c.actualizado_por
         ORDER BY c.actualizado_en DESC LIMIT 1);
$$;


--
-- Name: sp_cfg_c_impacto_club(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cfg_c_impacto_club(p_valor_punto integer) RETURNS TABLE(clientes_con_puntos bigint, puntos_vigentes bigint, valor_actual integer, valor_nuevo integer, pasivo_actual bigint, pasivo_nuevo bigint, diferencia bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH v AS (
        SELECT COALESCE((valor->>'valorPunto')::int, 0) AS actual
        FROM configuracion WHERE clave = 'club'
    ),
    p AS (
        SELECT count(*) AS clientes, COALESCE(sum(puntos), 0) AS puntos
        FROM clientes WHERE puntos > 0
    )
    SELECT
        p.clientes,
        p.puntos,
        v.actual,
        p_valor_punto,
        p.puntos * v.actual,
        p.puntos * p_valor_punto,
        p.puntos * p_valor_punto - p.puntos * v.actual
    FROM p, v;
$$;


--
-- Name: sp_cfg_u_seccion(text, jsonb, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cfg_u_seccion(p_clave text, p_valor jsonb, p_usuario_id integer DEFAULT NULL::integer) RETURNS jsonb
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_valor jsonb := p_valor;
BEGIN
    IF p_clave NOT IN ('local', 'ticket', 'venta', 'club') THEN
        RAISE EXCEPTION 'Sección desconocida: %.', p_clave;
    END IF;

    IF p_valor IS NULL OR jsonb_typeof(p_valor) <> 'object' THEN
        RAISE EXCEPTION 'La configuración debe ser un objeto.';
    END IF;

    -- ─── Validaciones por sección ───

    IF p_clave = 'local' THEN
        IF btrim(coalesce(p_valor->>'nombre', '')) = '' THEN
            RAISE EXCEPTION 'El nombre del local es obligatorio: sale impreso en cada ticket.';
        END IF;
    END IF;

    IF p_clave = 'venta' THEN
        IF (p_valor->>'iva')::numeric < 0 OR (p_valor->>'iva')::numeric > 100 THEN
            RAISE EXCEPTION 'El IVA debe estar entre 0 y 100.';
        END IF;

        IF (p_valor->>'descuentoSinAutorizacion')::int < 0 THEN
            RAISE EXCEPTION 'El umbral de descuento no puede ser negativo.';
        END IF;

        IF (p_valor->>'mermaSinAutorizacion')::int < 0 THEN
            RAISE EXCEPTION 'El umbral de merma no puede ser negativo.';
        END IF;
    END IF;

    IF p_clave = 'club' THEN
        IF (p_valor->>'valorPunto')::int < 1 THEN
            RAISE EXCEPTION 'Cada punto tiene que valer al menos $1.';
        END IF;

        IF (p_valor->>'puntosPorPeso')::int < 1 THEN
            RAISE EXCEPTION 'Indica cada cuántos pesos se gana un punto.';
        END IF;

        IF (p_valor->>'canjeMinimo')::int < 1 THEN
            RAISE EXCEPTION 'El canje mínimo debe ser al menos 1 punto.';
        END IF;
    END IF;

    INSERT INTO configuracion (clave, valor, actualizado_por, actualizado_en)
    VALUES (p_clave, v_valor, p_usuario_id, now())
    ON CONFLICT (clave) DO UPDATE
       SET valor = EXCLUDED.valor,
           actualizado_por = EXCLUDED.actualizado_por,
           actualizado_en = now();

    RETURN v_valor;
END;
$_$;


--
-- Name: sp_cli_c_cliente(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_cliente(p_id integer) RETURNS TABLE(id integer, rut text, nombre text, telefono text, correo text, direccion text, cumple_mes smallint, cumple_dia smallint, notas text, puntos integer, valor_puntos integer, activo boolean, compras bigint, total_gastado bigint, ticket_promedio numeric, ultima_compra timestamp with time zone, dias_sin_comprar integer, dias_para_cumple integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.rut, v.nombre, v.telefono, v.correo, v.direccion,
        v.cumple_mes, v.cumple_dia, v.notas,
        v.puntos, v.valor_puntos, v.activo,
        v.compras, v.total_gastado, v.ticket_promedio,
        v.ultima_compra, v.dias_sin_comprar, v.dias_para_cumple,
        v.creado_en, 1::bigint
    FROM vw_clientes v WHERE v.id = p_id;
$$;


--
-- Name: sp_cli_c_cliente_rut(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_cliente_rut(p_rut text) RETURNS TABLE(id integer, rut text, nombre text, telefono text, correo text, direccion text, cumple_mes smallint, cumple_dia smallint, notas text, puntos integer, valor_puntos integer, activo boolean, compras bigint, total_gastado bigint, ticket_promedio numeric, ultima_compra timestamp with time zone, dias_sin_comprar integer, dias_para_cumple integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.rut, v.nombre, v.telefono, v.correo, v.direccion,
        v.cumple_mes, v.cumple_dia, v.notas,
        v.puntos, v.valor_puntos, v.activo,
        v.compras, v.total_gastado, v.ticket_promedio,
        v.ultima_compra, v.dias_sin_comprar, v.dias_para_cumple,
        v.creado_en, 1::bigint
    FROM vw_clientes v
    WHERE fn_cli_normalizar_rut(v.rut) = fn_cli_normalizar_rut(p_rut);
$$;


--
-- Name: sp_cli_c_clientes(text, boolean, boolean, integer, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_clientes(p_buscar text DEFAULT NULL::text, p_activo boolean DEFAULT true, p_con_puntos boolean DEFAULT false, p_cumple_mes integer DEFAULT NULL::integer, p_sin_comprar_dias integer DEFAULT NULL::integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 100) RETURNS TABLE(id integer, rut text, nombre text, telefono text, correo text, direccion text, cumple_mes smallint, cumple_dia smallint, notas text, puntos integer, valor_puntos integer, activo boolean, compras bigint, total_gastado bigint, ticket_promedio numeric, ultima_compra timestamp with time zone, dias_sin_comprar integer, dias_para_cumple integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.rut, v.nombre, v.telefono, v.correo, v.direccion,
        v.cumple_mes, v.cumple_dia, v.notas,
        v.puntos, v.valor_puntos, v.activo,
        v.compras, v.total_gastado, v.ticket_promedio,
        v.ultima_compra, v.dias_sin_comprar, v.dias_para_cumple,
        v.creado_en,
        count(*) OVER ()
    FROM vw_clientes v
    WHERE (p_buscar IS NULL
           OR v.nombre   ILIKE '%' || p_buscar || '%'
           OR v.telefono ILIKE '%' || p_buscar || '%'
           OR v.correo   ILIKE '%' || p_buscar || '%'
           OR fn_cli_normalizar_rut(v.rut) LIKE
              '%' || fn_cli_normalizar_rut(p_buscar) || '%')
      AND (p_activo IS NULL OR v.activo = p_activo)
      AND (NOT COALESCE(p_con_puntos, false) OR v.puntos > 0)
      AND (p_cumple_mes IS NULL OR v.cumple_mes = p_cumple_mes)
      -- Para campañas de recuperación: quiénes se alejaron.
      AND (p_sin_comprar_dias IS NULL
           OR v.dias_sin_comprar >= p_sin_comprar_dias
           OR (v.dias_sin_comprar IS NULL AND v.compras = 0))
    ORDER BY v.nombre
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_cli_c_compras(integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_compras(p_cliente_id integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 20) RETURNS TABLE(id integer, folio text, creado_en timestamp with time zone, total integer, descuento_total integer, medio_pago public.medio_pago, puntos_ganados integer, puntos_canjeados integer, lineas bigint, vendedor text, anulada boolean, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
    ORDER BY v.creado_en DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_cli_c_compras_usuario(integer, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_compras_usuario(p_cliente_id integer, p_usuario_id integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 20) RETURNS TABLE(id integer, folio text, creado_en timestamp with time zone, total integer, descuento_total integer, medio_pago public.medio_pago, puntos_ganados integer, puntos_canjeados integer, lineas bigint, vendedor text, anulada boolean, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_cli_c_cumpleanos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_cumpleanos(p_mes integer DEFAULT NULL::integer) RETURNS TABLE(id integer, nombre text, telefono text, correo text, cumple_mes smallint, cumple_dia smallint, dias_faltantes integer, puntos integer, compras bigint, total_gastado bigint, ultima_compra timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.nombre, v.telefono, v.correo,
        v.cumple_mes, v.cumple_dia,
        v.dias_para_cumple, v.puntos, v.compras,
        v.total_gastado, v.ultima_compra
    FROM vw_clientes v
    WHERE v.activo
      AND v.cumple_mes = COALESCE(p_mes, extract(month FROM CURRENT_DATE)::int)
    ORDER BY v.cumple_dia;
$$;


--
-- Name: sp_cli_c_frecuentes(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_frecuentes(p_cliente_id integer, p_limite integer DEFAULT 5) RETURNS TABLE(producto_id integer, producto text, emoji text, veces bigint, unidades bigint, total bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        vi.producto_id, p.nombre, p.emoji,
        count(DISTINCT vi.venta_id),
        sum(vi.cantidad),
        sum(vi.subtotal)
    FROM venta_items vi
    JOIN ventas v    ON v.id = vi.venta_id
    JOIN productos p ON p.id = vi.producto_id
    WHERE v.cliente_id = p_cliente_id
      AND NOT v.anulada
    GROUP BY vi.producto_id, p.nombre, p.emoji
    ORDER BY 4 DESC, 6 DESC
    LIMIT GREATEST(p_limite, 1);
$$;


--
-- Name: sp_cli_c_puntos(integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_c_puntos(p_cliente_id integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30) RETURNS TABLE(id integer, cantidad integer, saldo_resultante integer, motivo text, venta_id integer, folio text, usuario text, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        pm.id, pm.cantidad, pm.saldo_resultante,
        pm.motivo, pm.venta_id, v.folio,
        u.nombre, pm.creado_en,
        count(*) OVER ()
    FROM puntos_movimientos pm
    LEFT JOIN ventas v   ON v.id = pm.venta_id
    LEFT JOIN usuarios u ON u.id = pm.usuario_id
    WHERE pm.cliente_id = p_cliente_id
    ORDER BY pm.creado_en DESC, pm.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_cli_i_ajuste_puntos(integer, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_i_ajuste_puntos(p_cliente_id integer, p_cantidad integer, p_motivo text, p_usuario_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_saldo  int;
    v_nombre text;
BEGIN
    IF coalesce(p_cantidad, 0) = 0 THEN
        RAISE EXCEPTION 'La cantidad no puede ser cero.';
    END IF;

    IF length(btrim(coalesce(p_motivo, ''))) < 5 THEN
        RAISE EXCEPTION 'Explica el motivo del ajuste, con al menos 5 caracteres.';
    END IF;

    SELECT puntos, nombre INTO v_saldo, v_nombre
      FROM clientes WHERE id = p_cliente_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El cliente no existe.';
    END IF;

    -- Un saldo negativo sería una deuda del cliente con el local, que no es
    -- algo que este sistema sepa manejar.
    IF v_saldo + p_cantidad < 0 THEN
        RAISE EXCEPTION '% tiene % puntos: no se le pueden descontar %.',
            v_nombre, v_saldo, abs(p_cantidad);
    END IF;

    UPDATE clientes
       SET puntos = puntos + p_cantidad, actualizado_en = now()
     WHERE id = p_cliente_id
    RETURNING puntos INTO v_saldo;

    INSERT INTO puntos_movimientos (
        cliente_id, cantidad, saldo_resultante, motivo, usuario_id
    )
    VALUES (
        p_cliente_id, p_cantidad, v_saldo,
        btrim(p_motivo), p_usuario_id
    );

    RETURN p_cliente_id;
END;
$$;


--
-- Name: sp_cli_i_cliente(text, text, text, text, text, integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_i_cliente(p_rut text, p_nombre text, p_telefono text DEFAULT NULL::text, p_correo text DEFAULT NULL::text, p_direccion text DEFAULT NULL::text, p_cumple_mes integer DEFAULT NULL::integer, p_cumple_dia integer DEFAULT NULL::integer, p_notas text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id     int;
    v_quien  text;
BEGIN
    IF btrim(coalesce(p_rut, '')) = '' THEN
        RAISE EXCEPTION 'El RUT es obligatorio: es con lo que se busca la ficha.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Indica el nombre del cliente.';
    END IF;

    -- Se compara normalizado: "76.543.210-3" y "765432103" son el mismo
    -- RUT, y dejar entrar los dos crearía la ficha duplicada que el UNIQUE
    -- sobre el texto crudo no alcanza a impedir.
    SELECT nombre INTO v_quien
      FROM clientes
     WHERE fn_cli_normalizar_rut(rut) = fn_cli_normalizar_rut(p_rut);

    IF FOUND THEN
        RAISE EXCEPTION 'Ese RUT ya está registrado a nombre de %.', v_quien;
    END IF;

    IF p_cumple_mes IS NOT NULL AND (p_cumple_mes < 1 OR p_cumple_mes > 12) THEN
        RAISE EXCEPTION 'El mes de cumpleaños tiene que estar entre 1 y 12.';
    END IF;

    IF p_cumple_dia IS NOT NULL AND (p_cumple_dia < 1 OR p_cumple_dia > 31) THEN
        RAISE EXCEPTION 'El día de cumpleaños tiene que estar entre 1 y 31.';
    END IF;

    -- Uno sin el otro no sirve para nada: la campaña de cumpleaños necesita
    -- la fecha completa.
    IF (p_cumple_mes IS NULL) <> (p_cumple_dia IS NULL) THEN
        RAISE EXCEPTION 'Indica el día y el mes del cumpleaños, o ninguno.';
    END IF;

    INSERT INTO clientes (
        rut, nombre, telefono, correo, direccion,
        cumple_mes, cumple_dia, notas, puntos, activo
    )
    VALUES (
        btrim(p_rut), btrim(p_nombre),
        nullif(btrim(coalesce(p_telefono, '')), ''),
        nullif(btrim(coalesce(p_correo, '')), ''),
        nullif(btrim(coalesce(p_direccion, '')), ''),
        p_cumple_mes, p_cumple_dia,
        nullif(btrim(coalesce(p_notas, '')), ''),
        0, true
    )
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_cli_u_cliente(integer, text, text, text, text, text, integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_u_cliente(p_id integer, p_rut text, p_nombre text, p_telefono text DEFAULT NULL::text, p_correo text DEFAULT NULL::text, p_direccion text DEFAULT NULL::text, p_cumple_mes integer DEFAULT NULL::integer, p_cumple_dia integer DEFAULT NULL::integer, p_notas text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_quien text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM clientes WHERE id = p_id) THEN
        RAISE EXCEPTION 'El cliente no existe.';
    END IF;

    IF btrim(coalesce(p_rut, '')) = '' THEN
        RAISE EXCEPTION 'El RUT es obligatorio.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Indica el nombre del cliente.';
    END IF;

    SELECT nombre INTO v_quien
      FROM clientes
     WHERE fn_cli_normalizar_rut(rut) = fn_cli_normalizar_rut(p_rut)
       AND id <> p_id;

    IF FOUND THEN
        RAISE EXCEPTION 'Ese RUT ya está registrado a nombre de %.', v_quien;
    END IF;

    IF (p_cumple_mes IS NULL) <> (p_cumple_dia IS NULL) THEN
        RAISE EXCEPTION 'Indica el día y el mes del cumpleaños, o ninguno.';
    END IF;

    IF p_cumple_mes IS NOT NULL AND (p_cumple_mes < 1 OR p_cumple_mes > 12) THEN
        RAISE EXCEPTION 'El mes de cumpleaños tiene que estar entre 1 y 12.';
    END IF;

    IF p_cumple_dia IS NOT NULL AND (p_cumple_dia < 1 OR p_cumple_dia > 31) THEN
        RAISE EXCEPTION 'El día de cumpleaños tiene que estar entre 1 y 31.';
    END IF;

    UPDATE clientes SET
        rut            = btrim(p_rut),
        nombre         = btrim(p_nombre),
        telefono       = nullif(btrim(coalesce(p_telefono, '')), ''),
        correo         = nullif(btrim(coalesce(p_correo, '')), ''),
        direccion      = nullif(btrim(coalesce(p_direccion, '')), ''),
        cumple_mes     = p_cumple_mes,
        cumple_dia     = p_cumple_dia,
        notas          = nullif(btrim(coalesce(p_notas, '')), ''),
        actualizado_en = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_cli_u_estado(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cli_u_estado(p_id integer, p_activo boolean) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM clientes WHERE id = p_id) THEN
        RAISE EXCEPTION 'El cliente no existe.';
    END IF;

    UPDATE clientes
       SET activo = p_activo, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: cotizacion_cuotas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cotizacion_cuotas (
    id integer NOT NULL,
    cotizacion_id integer NOT NULL,
    numero integer NOT NULL,
    monto integer NOT NULL,
    vence date NOT NULL,
    notas text,
    CONSTRAINT cuotas_monto_positivo CHECK ((monto > 0)),
    CONSTRAINT cuotas_numero_positivo CHECK ((numero > 0))
);


--
-- Name: cotizacion_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cotizacion_items (
    id integer NOT NULL,
    cotizacion_id integer NOT NULL,
    producto_id integer,
    nombre text NOT NULL,
    precio integer NOT NULL,
    cantidad integer NOT NULL,
    a_medida boolean DEFAULT false NOT NULL,
    CONSTRAINT cotizacion_items_cantidad_positiva CHECK ((cantidad > 0)),
    CONSTRAINT cotizacion_items_precio_no_negativo CHECK ((precio >= 0))
);


--
-- Name: cotizacion_pagos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cotizacion_pagos (
    id integer NOT NULL,
    cotizacion_id integer NOT NULL,
    venta_id integer,
    monto integer NOT NULL,
    medio_pago public.medio_pago NOT NULL,
    fecha timestamp with time zone DEFAULT now() NOT NULL,
    usuario_id integer,
    notas text,
    anulado boolean DEFAULT false NOT NULL,
    anulado_en timestamp with time zone,
    motivo_anulacion text,
    CONSTRAINT pagos_anulacion_completa CHECK (((NOT anulado) OR ((anulado_en IS NOT NULL) AND (motivo_anulacion IS NOT NULL)))),
    CONSTRAINT pagos_monto_positivo CHECK ((monto > 0))
);


--
-- Name: cotizaciones; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cotizaciones (
    id integer NOT NULL,
    folio text NOT NULL,
    cliente_id integer,
    cliente_nombre text NOT NULL,
    tipo_evento text NOT NULL,
    fecha_evento date,
    contacto text,
    traslado integer DEFAULT 0 NOT NULL,
    montaje integer DEFAULT 0 NOT NULL,
    total integer DEFAULT 0 NOT NULL,
    abono integer DEFAULT 0 NOT NULL,
    estado public.estado_cotizacion DEFAULT 'borrador'::public.estado_cotizacion NOT NULL,
    notas text,
    creada_por integer,
    venta_id integer,
    cobrada_en timestamp with time zone,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    motivo_anulacion text,
    anulada_por integer,
    anulada_en timestamp with time zone,
    CONSTRAINT cotizaciones_abono_no_supera_total CHECK ((abono <= total)),
    CONSTRAINT cotizaciones_cliente_no_vacio CHECK ((length(TRIM(BOTH FROM cliente_nombre)) > 0)),
    CONSTRAINT cotizaciones_cobrada_con_venta CHECK (((estado <> 'cobrada'::public.estado_cotizacion) OR (venta_id IS NOT NULL))),
    CONSTRAINT cotizaciones_montos_no_negativos CHECK (((traslado >= 0) AND (montaje >= 0) AND (total >= 0) AND (abono >= 0)))
);


--
-- Name: usuarios; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.usuarios (
    id integer NOT NULL,
    nombre text NOT NULL,
    email text NOT NULL,
    password_hash text NOT NULL,
    rol public.rol_usuario DEFAULT 'vendedor'::public.rol_usuario NOT NULL,
    activo boolean DEFAULT true NOT NULL,
    ultimo_acceso timestamp with time zone,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT usuarios_email_valido CHECK ((email ~* '^[^@\s]+@[^@\s]+\.[^@\s]+$'::text)),
    CONSTRAINT usuarios_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0))
);


--
-- Name: vw_cot_resumen; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_cot_resumen AS
 WITH pagado AS (
         SELECT cotizacion_pagos.cotizacion_id,
            (COALESCE(sum(cotizacion_pagos.monto) FILTER (WHERE (NOT cotizacion_pagos.anulado)), (0)::bigint))::integer AS abonado
           FROM public.cotizacion_pagos
          GROUP BY cotizacion_pagos.cotizacion_id
        ), base AS (
         SELECT c.id,
            c.folio,
            c.cliente_id,
            c.cliente_nombre,
            c.tipo_evento,
            c.fecha_evento,
            c.contacto,
            c.traslado,
            c.montaje,
            c.total,
            c.abono,
            c.estado,
            c.notas,
            c.creada_por,
            c.venta_id,
            c.cobrada_en,
            c.creado_en,
            c.actualizado_en,
            c.motivo_anulacion,
            c.anulada_por,
            c.anulada_en,
            COALESCE(p.abonado, 0) AS abonado,
            GREATEST((c.total - COALESCE(p.abonado, 0)), 0) AS pendiente,
            (EXISTS ( SELECT 1
                   FROM public.cotizacion_cuotas q
                  WHERE (q.cotizacion_id = c.id))) AS con_plan
           FROM (public.cotizaciones c
             LEFT JOIN pagado p ON ((p.cotizacion_id = c.id)))
        ), cobro AS (
         SELECT b_1.id,
            (
                CASE
                    WHEN (b_1.estado <> 'aprobada'::public.estado_cotizacion) THEN (0)::bigint
                    WHEN b_1.con_plan THEN GREATEST((( SELECT COALESCE(sum(q.monto), (0)::bigint) AS "coalesce"
                       FROM public.cotizacion_cuotas q
                      WHERE ((q.cotizacion_id = b_1.id) AND (q.vence <= CURRENT_DATE))) - b_1.abonado), (0)::bigint)
                    WHEN ((b_1.fecha_evento IS NOT NULL) AND (b_1.fecha_evento <= CURRENT_DATE)) THEN (b_1.pendiente)::bigint
                    ELSE (0)::bigint
                END)::integer AS exigible_hoy,
            (
                CASE
                    WHEN (b_1.estado <> 'aprobada'::public.estado_cotizacion) THEN (0)::bigint
                    WHEN b_1.con_plan THEN GREATEST((( SELECT COALESCE(sum(q.monto), (0)::bigint) AS "coalesce"
                       FROM public.cotizacion_cuotas q
                      WHERE ((q.cotizacion_id = b_1.id) AND (q.vence < CURRENT_DATE))) - b_1.abonado), (0)::bigint)
                    WHEN ((b_1.fecha_evento IS NOT NULL) AND (b_1.fecha_evento < CURRENT_DATE)) THEN (b_1.pendiente)::bigint
                    ELSE (0)::bigint
                END)::integer AS vencido,
                CASE
                    WHEN (b_1.estado <> 'aprobada'::public.estado_cotizacion) THEN NULL::date
                    WHEN b_1.con_plan THEN ( SELECT min(x.vence) AS min
                       FROM ( SELECT q.vence,
                                sum(q.monto) OVER (ORDER BY q.numero) AS acumulado
                               FROM public.cotizacion_cuotas q
                              WHERE (q.cotizacion_id = b_1.id)) x
                      WHERE ((x.acumulado > b_1.abonado) AND (x.vence < CURRENT_DATE)))
                    WHEN ((b_1.fecha_evento < CURRENT_DATE) AND (b_1.pendiente > 0)) THEN b_1.fecha_evento
                    ELSE NULL::date
                END AS vencido_desde
           FROM base b_1
        )
 SELECT b.id,
    b.folio,
    b.cliente_id,
    b.cliente_nombre,
    b.tipo_evento,
    b.fecha_evento,
    b.contacto,
    b.traslado,
    b.montaje,
    b.total,
    b.abonado AS abono,
    b.pendiente AS saldo,
        CASE
            WHEN (b.total > 0) THEN round((((b.abonado)::numeric * 100.0) / (b.total)::numeric), 1)
            ELSE (0)::numeric
        END AS porcentaje_pagado,
    (b.estado)::text AS estado,
    b.notas,
    b.creada_por,
    u.nombre AS creada_por_nombre,
    b.venta_id,
    b.cobrada_en,
    b.creado_en,
    b.actualizado_en,
    ( SELECT count(*) AS count
           FROM public.cotizacion_items i
          WHERE (i.cotizacion_id = b.id)) AS lineas,
    (b.fecha_evento - CURRENT_DATE) AS dias_para_evento,
    k.exigible_hoy,
    k.vencido,
    k.vencido_desde,
        CASE
            WHEN (b.estado = 'anulada'::public.estado_cotizacion) THEN 'anulada'::text
            WHEN (b.estado = 'cobrada'::public.estado_cotizacion) THEN 'cobrada'::text
            WHEN (b.pendiente = 0) THEN 'pagada'::text
            WHEN (k.vencido > 0) THEN ((('debe '::text || to_char(k.vencido, 'FM$999G999G999'::text)) || ' desde el '::text) || to_char((k.vencido_desde)::timestamp with time zone, 'DD-MM'::text))
            WHEN (b.estado = 'borrador'::public.estado_cotizacion) THEN 'sin aprobar'::text
            ELSE 'al día'::text
        END AS estado_pago,
    b.motivo_anulacion
   FROM ((base b
     JOIN cobro k ON ((k.id = b.id)))
     LEFT JOIN public.usuarios u ON ((u.id = b.creada_por)));


--
-- Name: sp_cot_c_agenda(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_agenda(p_dias integer, p_usuario_id integer) RETURNS SETOF public.vw_cot_resumen
    LANGUAGE sql STABLE
    AS $$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.estado = 'aprobada'
      AND r.fecha_evento BETWEEN CURRENT_DATE AND CURRENT_DATE + GREATEST(COALESCE(p_dias, 30), 1)
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id)
    ORDER BY r.fecha_evento, r.id;
$$;


--
-- Name: sp_cot_c_cotizacion(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_cotizacion(p_id integer, p_usuario_id integer) RETURNS SETOF public.vw_cot_resumen
    LANGUAGE sql STABLE
    AS $$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.id = p_id
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id);
$$;


--
-- Name: sp_cot_c_cotizaciones(text, text, integer, boolean, integer, date, date, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_cotizaciones(p_buscar text, p_estado text, p_cliente_id integer, p_solo_vencidas boolean, p_proximos_dias integer, p_desde date, p_hasta date, p_usuario_id integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30) RETURNS TABLE(id integer, folio text, cliente_id integer, cliente_nombre text, tipo_evento text, fecha_evento date, contacto text, traslado integer, montaje integer, total integer, abono integer, saldo integer, porcentaje_pagado numeric, estado text, notas text, creada_por integer, creada_por_nombre text, venta_id integer, cobrada_en timestamp with time zone, creado_en timestamp with time zone, actualizado_en timestamp with time zone, lineas bigint, dias_para_evento integer, exigible_hoy integer, vencido integer, vencido_desde date, estado_pago text, motivo_anulacion text, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_cot_c_cuotas(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_cuotas(p_id integer) RETURNS TABLE(id integer, numero integer, monto integer, vence date, notas text, cubierta boolean, dias_para_vencer integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT q.id, q.numero, q.monto, q.vence, q.notas,
           sum(q.monto) OVER (ORDER BY q.numero) <= r.abono,
           (q.vence - CURRENT_DATE)
    FROM cotizacion_cuotas q
    JOIN vw_cot_resumen r ON r.id = q.cotizacion_id
    WHERE q.cotizacion_id = p_id
    ORDER BY q.numero;
$$;


--
-- Name: sp_cot_c_faltantes(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_faltantes(p_id integer) RETURNS TABLE(producto_id integer, emoji text, producto text, comprometido integer, disponible integer, faltante integer)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_cot_c_items(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_items(p_id integer) RETURNS TABLE(id integer, producto_id integer, emoji text, nombre text, precio integer, cantidad integer, subtotal integer, a_medida boolean, disponible integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT i.id, i.producto_id, p.emoji, i.nombre, i.precio, i.cantidad,
           i.precio * i.cantidad, i.a_medida,
           -- Lo que hay hoy entre bodega y mesón. Null en lo hecho a medida.
           CASE WHEN i.a_medida THEN NULL ELSE d.disponible END
    FROM cotizacion_items i
    LEFT JOIN productos p ON p.id = i.producto_id
    LEFT JOIN vw_productos_disponibles d ON d.id = i.producto_id
    WHERE i.cotizacion_id = p_id
    ORDER BY i.id;
$$;


--
-- Name: sp_cot_c_pagos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_pagos(p_id integer) RETURNS TABLE(id integer, monto integer, medio_pago text, fecha timestamp with time zone, venta_id integer, venta_folio text, usuario text, notas text, anulado boolean, anulado_en timestamp with time zone, motivo_anulacion text)
    LANGUAGE sql STABLE
    AS $$
    SELECT g.id, g.monto, g.medio_pago::text, g.fecha, g.venta_id, v.folio,
           u.nombre, g.notas, g.anulado, g.anulado_en, g.motivo_anulacion
    FROM cotizacion_pagos g
    LEFT JOIN ventas v ON v.id = g.venta_id
    LEFT JOIN usuarios u ON u.id = g.usuario_id
    WHERE g.cotizacion_id = p_id
    ORDER BY g.fecha, g.id;
$$;


--
-- Name: sp_cot_c_por_cobrar(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_por_cobrar(p_usuario_id integer) RETURNS SETOF public.vw_cot_resumen
    LANGUAGE sql STABLE
    AS $$
    SELECT r.* FROM vw_cot_resumen r
    WHERE r.vencido > 0
      AND (p_usuario_id IS NULL OR r.creada_por = p_usuario_id)
    ORDER BY r.vencido_desde, r.vencido DESC;
$$;


--
-- Name: sp_cot_c_preparar_cobro(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_preparar_cobro(p_id integer) RETURNS TABLE(producto_id integer, nombre text, cantidad integer, precio integer, es_servicio boolean, disponible integer)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_cot_c_resultado(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_c_resultado(p_id integer) RETURNS TABLE(cobrado integer, boletas bigint, costo_flor integer, margen integer, margen_porcentaje numeric)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_cot_i_cotizacion(integer, text, text, date, text, integer, integer, text, jsonb, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_i_cotizacion(p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text, p_fecha_evento date, p_contacto text, p_traslado integer, p_montaje integer, p_notas text, p_items jsonb, p_usuario_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: sp_cot_i_generar_cuotas(integer, integer, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_i_generar_cuotas(p_id integer, p_cantidad integer, p_primer_vencimiento date, p_cada_dias integer) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
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
$_$;


--
-- Name: sp_cot_i_pago(integer, integer, text, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_i_pago(p_id integer, p_monto integer, p_medio_pago text, p_recibido integer, p_notas text, p_usuario_id integer) RETURNS TABLE(o_pago_id integer, o_venta_id integer, o_venta_folio text, o_monto integer, o_vuelto integer, o_saldo integer)
    LANGUAGE plpgsql
    AS $_$
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
$_$;


--
-- Name: sp_cot_u_anular(integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_u_anular(p_id integer, p_motivo text, p_usuario_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: sp_cot_u_anular_pago(integer, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_u_anular_pago(p_id integer, p_pago_id integer, p_motivo text, p_usuario_id integer) RETURNS TABLE(o_pago_id integer, o_venta_folio text, o_monto integer)
    LANGUAGE plpgsql
    AS $$
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
        -- La anulación de la boleta marca el abono y lo resta.
        SELECT o_folio INTO v_folio
          FROM sp_ven_u_venta_anular(v_g.venta_id, btrim(p_motivo), p_usuario_id);
    ELSE
        UPDATE cotizacion_pagos
           SET anulado = true, anulado_en = now(), motivo_anulacion = btrim(p_motivo)
         WHERE id = p_pago_id;
        UPDATE cotizaciones
           SET abono = GREATEST(abono - v_g.monto, 0), actualizado_en = now()
         WHERE id = p_id;
    END IF;

    RETURN QUERY SELECT p_pago_id, v_folio, v_g.monto;
END;
$$;


--
-- Name: sp_cot_u_aprobar(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_u_aprobar(p_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
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
$_$;


--
-- Name: sp_cot_u_cotizacion(integer, integer, text, text, date, text, integer, integer, text, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_u_cotizacion(p_id integer, p_cliente_id integer, p_cliente_nombre text, p_tipo_evento text, p_fecha_evento date, p_contacto text, p_traslado integer, p_montaje integer, p_notas text, p_items jsonb) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
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
$_$;


--
-- Name: sp_cot_u_cuotas(integer, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_cot_u_cuotas(p_id integer, p_cuotas jsonb) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
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
$_$;


--
-- Name: sp_inv_c_categorias(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_categorias() RETURNS TABLE(id integer, nombre text, productos bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT c.id, c.nombre, count(p.id)
    FROM categorias c
    LEFT JOIN productos p ON p.categoria_id = c.id AND p.activo
    GROUP BY c.id, c.nombre
    ORDER BY c.nombre;
$$;


--
-- Name: sp_inv_c_componentes(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_componentes() RETURNS TABLE(id integer, codigo text, nombre text, emoji text, categoria text, costo numeric, precio numeric, precio_ramo numeric, controla_lotes boolean, disponible integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        p.id, p.codigo, p.nombre, p.emoji, c.nombre,
        COALESCE(p.costo, 0), p.precio, p.precio_ramo,
        p.controla_lotes, COALESCE(p.stock, 0)
    FROM productos p
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE p.activo AND p.tipo = 'simple'
    ORDER BY p.nombre;
$$;


--
-- Name: sp_inv_c_conteo(text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_conteo(p_ubicacion text DEFAULT 'bodega'::text, p_categoria_id integer DEFAULT NULL::integer) RETURNS TABLE(tipo_fila text, producto_id integer, producto text, emoji text, categoria_id integer, categoria text, categoria_orden integer, controla_lotes boolean, lote_id integer, partida_id integer, origen text, fecha_vencimiento date, sistema integer, costo_unitario integer)
    LANGUAGE sql STABLE
    AS $$
    -- ─── Bodega ───
    SELECT 'producto', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, false,
           NULL::int, NULL::int, NULL::text, NULL::date,
           COALESCE(p.stock, 0), fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND NOT p.controla_lotes
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    UNION ALL
    SELECT 'balde', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, true,
           l.id, NULL::int, l.codigo, l.fecha_vencimiento,
           l.varas_disponibles, ROUND(l.costo_por_vara)::int
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
      JOIN lotes l ON l.producto_id = p.id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND p.controla_lotes
       AND l.estado = 'activo' AND l.varas_disponibles > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    -- Flores sin ningún balde activo: una fila para poder cargarlas.
    UNION ALL
    SELECT 'sin_balde', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, true,
           NULL::int, NULL::int, 'sin balde', NULL::date,
           0, fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'bodega'
       AND p.tipo = 'simple' AND p.activo AND p.controla_lotes
       AND NOT EXISTS (SELECT 1 FROM lotes l
                        WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0)
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    -- ─── Vitrina ───
    UNION ALL
    SELECT 'partida', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, p.controla_lotes,
           NULL::int, pm.id, pm.codigo, pm.fecha_vencimiento,
           pm.cantidad_disponible, ROUND(COALESCE(pm.costo_por_vara, 0))::int
      FROM partidas_mostrador pm
      JOIN productos p ON p.id = pm.producto_id
      JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'vitrina' AND pm.cantidad_disponible > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    UNION ALL
    SELECT 'armado', p.id, p.nombre, p.emoji, c.id, c.nombre, c.orden, false,
           NULL::int, NULL::int, NULL::text, NULL::date,
           COALESCE(p.stock_listo, 0), fn_mer_costo_ref(p.id)
      FROM productos p JOIN categorias c ON c.id = p.categoria_id
     WHERE p_ubicacion = 'vitrina'
       AND p.tipo = 'armado' AND p.activo AND COALESCE(p.stock_listo, 0) > 0
       AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)

    ORDER BY 7, 6, 3, 11 NULLS FIRST;
$$;


--
-- Name: sp_inv_c_conteo_lineas(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_conteo_lineas(p_conteo_id integer) RETURNS TABLE(producto_id integer, producto text, emoji text, categoria text, origen text, sistema integer, contado integer, diferencia integer, costo_unitario integer, merma_id integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT cl.producto_id, p.nombre, p.emoji, c.nombre, cl.origen,
           cl.sistema, cl.contado, cl.diferencia, cl.costo_unitario, cl.merma_id
      FROM conteo_lineas cl
      JOIN productos p ON p.id = cl.producto_id
      JOIN categorias c ON c.id = p.categoria_id
     WHERE cl.conteo_id = p_conteo_id
     ORDER BY (cl.diferencia = 0), c.orden, p.nombre, cl.origen;
$$;


--
-- Name: sp_inv_c_conteos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_conteos(p_limite integer DEFAULT 100) RETURNS TABLE(id integer, ubicacion text, detalle text, usuario text, autorizado_por text, creado_en timestamp with time zone, lineas integer, con_diferencia integer, sobrantes integer, faltantes integer, valor_sobrante integer, valor_faltante integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT c.id, c.ubicacion, c.detalle, u.nombre, c.autorizado_por, c.creado_en,
           c.lineas, c.con_diferencia, c.sobrantes, c.faltantes, c.valor_sobrante, c.valor_faltante
      FROM conteos c
      LEFT JOIN usuarios u ON u.id = c.usuario_id
     ORDER BY c.creado_en DESC
     LIMIT GREATEST(COALESCE(p_limite, 100), 1);
$$;


--
-- Name: sp_inv_c_disponibilidad_armado(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_disponibilidad_armado(p_producto_id integer) RETURNS TABLE(componente_id integer, producto text, emoji text, por_unidad integer, controla_lotes boolean, de_primera integer, recuperada integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT r.componente_id, p.nombre, p.emoji, r.cantidad, p.controla_lotes,
           CASE WHEN p.controla_lotes THEN
               COALESCE((SELECT sum(l.varas_disponibles) FROM lotes l
                          WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0
                            AND l.calidad IS NULL
                            AND COALESCE(l.fecha_vencimiento >= CURRENT_DATE, true)), 0)::int
           ELSE COALESCE(p.stock, 0) END,
           CASE WHEN p.controla_lotes THEN
               COALESCE((SELECT sum(l.varas_disponibles) FROM lotes l
                          WHERE l.producto_id = p.id AND l.estado = 'activo' AND l.varas_disponibles > 0
                            AND l.calidad IS NOT NULL), 0)::int
           ELSE 0 END
      FROM recetas r
      JOIN productos p ON p.id = r.componente_id
     WHERE r.producto_id = p_producto_id
     ORDER BY p.nombre;
$$;


--
-- Name: sp_inv_c_lotes_recuperados_armado(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_lotes_recuperados_armado(p_producto_id integer) RETURNS TABLE(lote_id integer, producto_id integer, producto text, codigo text, varas_disponibles integer, dias_en_camara integer, costo_por_vara numeric, calidad public.calidad_reingreso, alerta text)
    LANGUAGE sql STABLE
    AS $$
    SELECT l.id, p.id, p.nombre, l.codigo, l.varas_disponibles,
           (CURRENT_DATE - l.fecha_ingreso)::int, l.costo_por_vara, l.calidad,
           CASE WHEN l.fecha_vencimiento < CURRENT_DATE THEN 'vencido'
                WHEN l.fecha_vencimiento <= CURRENT_DATE + 1 THEN 'por_vencer'
                ELSE 'normal' END
      FROM recetas r
      JOIN productos p ON p.id = r.componente_id
      JOIN lotes l ON l.producto_id = p.id
     WHERE r.producto_id = p_producto_id
       AND p.controla_lotes AND l.estado = 'activo' AND l.varas_disponibles > 0
       AND l.calidad IS NOT NULL
     ORDER BY l.fecha_vencimiento NULLS LAST, l.id;
$$;


--
-- Name: sp_inv_c_movimientos(integer, public.tipo_movimiento, date, date, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_movimientos(p_producto_id integer DEFAULT NULL::integer, p_tipo public.tipo_movimiento DEFAULT NULL::public.tipo_movimiento, p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, lote_codigo text, ubicacion text, tipo public.tipo_movimiento, cantidad integer, stock_resultante integer, motivo text, detalle text, usuario_id integer, usuario text, referencia_tipo text, referencia_id integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_inv_c_producto(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_producto(p_id integer) RETURNS TABLE(id integer, codigo text, nombre text, emoji text, categoria_id integer, categoria text, tipo public.tipo_producto, costo numeric, costo_armado numeric, costo_efectivo numeric, precio numeric, precio_ramo numeric, precio_liquidacion numeric, margen numeric, en_bodega integer, en_venta integer, stock_total integer, stock_listo integer, minimo integer, controla_lotes boolean, activo boolean, bajo_minimo boolean, dias_vida integer, creado_en timestamp with time zone, actualizado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        p.id, p.codigo, p.nombre, p.emoji,
        p.categoria_id, c.nombre, p.tipo,
        p.costo, p.costo_armado, COALESCE(p.costo, p.costo_armado),
        p.precio, p.precio_ramo, p.precio_liquidacion,
        ROUND((p.precio - COALESCE(p.costo, p.costo_armado))::numeric
              / NULLIF(p.precio, 0)::numeric * 100, 1),
        COALESCE(p.stock, p.stock_listo, 0),
        COALESCE(m.stock, 0),
        COALESCE(p.stock, p.stock_listo, 0) + COALESCE(m.stock, 0),
        p.stock_listo, p.minimo, p.controla_lotes, p.activo,
        (COALESCE(p.stock, p.stock_listo, 0) + COALESCE(m.stock, 0)) <= p.minimo,
        p.dias_vida, p.creado_en, p.actualizado_en,
        1::bigint
    FROM productos p
    LEFT JOIN categorias c   ON c.id = p.categoria_id
    LEFT JOIN vw_mostrador m ON m.producto_id = p.id
    WHERE p.id = p_id;
$$;


--
-- Name: sp_inv_c_producto_codigo(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_producto_codigo(p_codigo text) RETURNS TABLE(id integer, codigo text, nombre text, emoji text, categoria_id integer, categoria text, tipo public.tipo_producto, costo numeric, costo_armado numeric, costo_efectivo numeric, precio numeric, precio_ramo numeric, precio_liquidacion numeric, margen numeric, en_bodega integer, en_venta integer, stock_total integer, stock_listo integer, minimo integer, controla_lotes boolean, activo boolean, bajo_minimo boolean, dias_vida integer, creado_en timestamp with time zone, actualizado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        p.id, p.codigo, p.nombre, p.emoji,
        p.categoria_id, c.nombre, p.tipo,
        p.costo, p.costo_armado, COALESCE(p.costo, p.costo_armado),
        p.precio, p.precio_ramo, p.precio_liquidacion,
        ROUND((p.precio - COALESCE(p.costo, p.costo_armado))::numeric
              / NULLIF(p.precio, 0)::numeric * 100, 1),
        COALESCE(p.stock, p.stock_listo, 0),
        COALESCE(m.stock, 0),
        COALESCE(p.stock, p.stock_listo, 0) + COALESCE(m.stock, 0),
        p.stock_listo, p.minimo, p.controla_lotes, p.activo,
        (COALESCE(p.stock, p.stock_listo, 0) + COALESCE(m.stock, 0)) <= p.minimo,
        p.dias_vida, p.creado_en, p.actualizado_en,
        1::bigint
    FROM productos p
    LEFT JOIN categorias c   ON c.id = p.categoria_id
    LEFT JOIN vw_mostrador m ON m.producto_id = p.id
    WHERE lower(p.codigo) = lower(btrim(p_codigo));
$$;


--
-- Name: sp_inv_c_producto_imagen(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_producto_imagen(p_producto_id integer) RETURNS TABLE(archivo text, version bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT archivo, version FROM producto_imagenes WHERE producto_id = p_producto_id;
$$;


--
-- Name: sp_inv_c_producto_imagenes(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_producto_imagenes() RETURNS TABLE(producto_id integer, version bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT producto_id, version FROM producto_imagenes;
$$;


--
-- Name: sp_inv_c_productos(text, integer, public.tipo_producto, boolean, boolean, boolean, boolean, integer, integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_productos(p_buscar text DEFAULT NULL::text, p_categoria_id integer DEFAULT NULL::integer, p_tipo public.tipo_producto DEFAULT NULL::public.tipo_producto, p_activo boolean DEFAULT NULL::boolean, p_bajo_minimo boolean DEFAULT false, p_controla_lotes boolean DEFAULT NULL::boolean, p_solo_en_venta boolean DEFAULT false, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50, p_con_stock boolean DEFAULT false) RETURNS TABLE(id integer, codigo text, nombre text, emoji text, categoria_id integer, categoria text, tipo public.tipo_producto, costo numeric, costo_armado numeric, costo_efectivo numeric, precio numeric, precio_ramo numeric, precio_liquidacion numeric, margen numeric, en_bodega integer, en_venta integer, stock_total integer, stock_listo integer, minimo integer, controla_lotes boolean, activo boolean, bajo_minimo boolean, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_inv_c_receta(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_receta(p_producto_id integer) RETURNS TABLE(componente_id integer, componente text, emoji text, cantidad integer, costo_unitario numeric, costo_linea numeric, precio numeric, controla_lotes boolean, disponible integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        r.componente_id, p.nombre, p.emoji, r.cantidad,
        COALESCE(p.costo, 0),
        r.cantidad * COALESCE(p.costo, 0),
        p.precio,
        p.controla_lotes,
        COALESCE(p.stock, 0)
    FROM recetas r
    JOIN productos p ON p.id = r.componente_id
    WHERE r.producto_id = p_producto_id
    ORDER BY p.nombre;
$$;


--
-- Name: sp_inv_c_resumen(text, integer, public.tipo_producto, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_c_resumen(p_buscar text DEFAULT NULL::text, p_categoria_id integer DEFAULT NULL::integer, p_tipo public.tipo_producto DEFAULT NULL::public.tipo_producto, p_activo boolean DEFAULT NULL::boolean) RETURNS TABLE(productos bigint, activos bigint, bajo_minimo bigint, sin_stock bigint, en_mostrador bigint, unidades bigint, valorizado numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH base AS (
        SELECT
            p.activo, p.minimo, p.nombre, p.codigo,
            p.categoria_id, p.tipo,
            COALESCE(m.stock, 0) AS venta,
            COALESCE(p.stock, p.stock_listo, 0) AS bodega,
            COALESCE(p.costo, p.costo_armado, 0) AS costo_efe
        FROM productos p
        LEFT JOIN vw_mostrador m ON m.producto_id = p.id
        WHERE (p_buscar IS NULL
               OR p.nombre ILIKE '%' || p_buscar || '%'
               OR p.codigo ILIKE '%' || p_buscar || '%')
          AND (p_categoria_id IS NULL OR p.categoria_id = p_categoria_id)
          AND (p_tipo         IS NULL OR p.tipo         = p_tipo)
          AND (p_activo       IS NULL OR p.activo       = p_activo)
    )
    SELECT
        count(*),
        count(*) FILTER (WHERE b.activo),
        count(*) FILTER (WHERE (b.bodega + b.venta) <= b.minimo AND b.activo),
        count(*) FILTER (WHERE (b.bodega + b.venta) = 0 AND b.activo),
        count(*) FILTER (WHERE b.venta > 0),
        COALESCE(sum(b.bodega + b.venta), 0),
        -- Al costo, no al precio: es lo invertido, no lo que se ganaría.
        COALESCE(sum((b.bodega + b.venta) * b.costo_efe), 0)
    FROM base b;
$$;


--
-- Name: sp_inv_d_producto(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_d_producto(p_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF EXISTS (SELECT 1 FROM lotes WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene lotes registrados. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM movimientos_inventario WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene movimientos de inventario. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM venta_items WHERE producto_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto tiene ventas registradas. Desactívalo.';
    END IF;

    IF EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_id OR componente_id = p_id) THEN
        RAISE EXCEPTION 'No se puede eliminar: el producto participa en una receta.';
    END IF;

    DELETE FROM productos WHERE id = p_id;
    RETURN p_id;
END;
$$;


--
-- Name: sp_inv_d_producto_imagen(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_d_producto_imagen(p_producto_id integer) RETURNS text
    LANGUAGE sql
    AS $$
    DELETE FROM producto_imagenes WHERE producto_id = p_producto_id RETURNING archivo;
$$;


--
-- Name: sp_inv_i_armar(integer, integer, integer[], integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_i_armar(p_producto_id integer, p_cantidad integer, p_lotes_autorizados integer[] DEFAULT '{}'::integer[], p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(costo_unitario integer, costo_total integer, listos integer)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_prod     record;
    r          record;
    l          record;
    v_falta    int;
    v_toma     int;
    v_costo    numeric := 0;
    v_costo_c  numeric;
    v_stock    int;
    v_listo    int;
BEGIN
    IF COALESCE(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'Indica cuántas unidades armar.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.tipo <> 'armado' THEN
        RAISE EXCEPTION '% no es un producto armado.', v_prod.nombre;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_producto_id) THEN
        RAISE EXCEPTION '% no tiene receta: no se sabe qué lleva.', v_prod.nombre;
    END IF;

    FOR r IN
        SELECT rc.componente_id, rc.cantidad * p_cantidad AS necesita, p.nombre, p.controla_lotes
          FROM recetas rc JOIN productos p ON p.id = rc.componente_id
         WHERE rc.producto_id = p_producto_id
         ORDER BY p.nombre
    LOOP
        PERFORM 1 FROM productos WHERE id = r.componente_id FOR UPDATE;
        v_falta   := r.necesita;
        v_costo_c := 0;

        IF r.controla_lotes THEN
            -- Primero la recuperada autorizada, después la de primera sin
            -- vencer; en cada grupo, lo que vence antes.
            FOR l IN
                SELECT lo.id, lo.varas_disponibles, lo.costo_por_vara
                  FROM lotes lo
                 WHERE lo.producto_id = r.componente_id
                   AND lo.estado = 'activo' AND lo.varas_disponibles > 0
                   AND (   (lo.calidad IS NOT NULL AND lo.id = ANY (COALESCE(p_lotes_autorizados, '{}')))
                        OR (lo.calidad IS NULL AND COALESCE(lo.fecha_vencimiento >= CURRENT_DATE, true)))
                 ORDER BY (lo.calidad IS NULL), lo.fecha_vencimiento NULLS LAST, lo.fecha_ingreso, lo.id
                 FOR UPDATE
            LOOP
                EXIT WHEN v_falta = 0;
                v_toma := LEAST(v_falta, l.varas_disponibles);

                UPDATE lotes
                   SET varas_disponibles = varas_disponibles - v_toma,
                       estado = CASE WHEN varas_disponibles - v_toma = 0
                                     THEN 'agotado'::estado_lote ELSE estado END,
                       actualizado_en = now()
                 WHERE id = l.id;

                v_costo_c := v_costo_c + v_toma * l.costo_por_vara;
                v_falta   := v_falta - v_toma;
            END LOOP;

            IF v_falta > 0 THEN
                RAISE EXCEPTION 'Faltan % de % en bodega para armar % unidad(es).',
                    v_falta, r.nombre, p_cantidad;
            END IF;
        ELSE
            IF (SELECT COALESCE(stock, 0) FROM productos WHERE id = r.componente_id) < r.necesita THEN
                RAISE EXCEPTION 'Faltan % de % en bodega para armar % unidad(es).',
                    r.necesita - (SELECT COALESCE(stock, 0) FROM productos WHERE id = r.componente_id),
                    r.nombre, p_cantidad;
            END IF;

            v_costo_c := r.necesita * fn_mer_costo_ref(r.componente_id);
        END IF;

        UPDATE productos
           SET stock = COALESCE(stock, 0) - r.necesita, actualizado_en = now()
         WHERE id = r.componente_id
        RETURNING stock INTO v_stock;

        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            r.componente_id, 'consumo', -r.necesita, v_stock,
            'Armado de ' || v_prod.nombre, p_cantidad || ' unidad(es)',
            p_usuario_id, 'armado', p_producto_id
        );

        v_costo := v_costo + v_costo_c;
    END LOOP;

    -- El costo del ramo: promedio ponderado con lo que ya estaba armado.
    UPDATE productos SET
        costo_armado = ROUND((COALESCE(stock_listo, 0) * COALESCE(costo_armado, 0) + v_costo)
                             / (COALESCE(stock_listo, 0) + p_cantidad)),
        stock_listo  = COALESCE(stock_listo, 0) + p_cantidad,
        actualizado_en = now()
    WHERE id = p_producto_id
    RETURNING productos.stock_listo INTO v_listo;

    INSERT INTO movimientos_inventario (
        producto_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, 'armado', p_cantidad, v_listo,
        'Armado', 'Costo ' || ROUND(v_costo / p_cantidad) || ' c/u',
        p_usuario_id, 'armado', p_producto_id
    );

    RETURN QUERY SELECT ROUND(v_costo / p_cantidad)::int, ROUND(v_costo)::int, v_listo;
END;
$$;


--
-- Name: sp_inv_i_categoria(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_i_categoria(p_nombre text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id int;
BEGIN
    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre de la categoría.';
    END IF;

    IF EXISTS (SELECT 1 FROM categorias WHERE lower(nombre) = lower(btrim(p_nombre))) THEN
        RAISE EXCEPTION 'Ya existe la categoría %.', btrim(p_nombre);
    END IF;

    INSERT INTO categorias (nombre) VALUES (btrim(p_nombre))
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_inv_i_conteo(text, jsonb, text, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_i_conteo(p_ubicacion text, p_items jsonb, p_detalle text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer, p_autorizado_por text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
DECLARE
    it         jsonb;
    v_n        int := 0;
    v_conteo   int;
    v_prod     record;
    v_lote     record;
    v_part     record;
    v_lote_id  int;
    v_part_id  int;
    v_contado  int;
    v_sistema  int;
    v_dif      int;
    v_stock    int;
    v_merma    int;
    v_origen   text;
    v_costo    int;
    v_codigo   text;
    v_firma    text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
    v_valor    int;
    v_umbral   int;
    v_detalle  text := COALESCE(nullif(btrim(coalesce(p_detalle, '')), ''), 'Conteo de stock');
BEGIN
    IF p_ubicacion NOT IN ('bodega', 'vitrina') THEN
        RAISE EXCEPTION 'Indica si el conteo es de bodega o de vitrina.';
    END IF;

    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        RAISE EXCEPTION 'No hay nada contado.';
    END IF;

    INSERT INTO conteos (ubicacion, detalle, usuario_id, autorizado_por)
    VALUES (p_ubicacion, v_detalle, p_usuario_id, v_firma)
    RETURNING id INTO v_conteo;

    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n       := v_n + 1;
        v_lote_id := (it->>'loteId')::int;
        v_part_id := (it->>'partidaId')::int;
        v_contado := (it->>'contado')::int;
        v_merma   := NULL;
        v_origen  := NULL;

        IF v_contado IS NULL OR v_contado < 0 THEN
            RAISE EXCEPTION 'Línea %: lo contado no puede ser negativo.', v_n;
        END IF;

        SELECT * INTO v_prod FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;
        IF NOT FOUND THEN
            RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
        END IF;

        -- ─── Lo que dice el sistema, AHORA ───
        IF v_part_id IS NOT NULL THEN
            IF p_ubicacion <> 'vitrina' THEN
                RAISE EXCEPTION 'Las partidas del mostrador se cuentan en la vitrina.';
            END IF;

            SELECT * INTO v_part FROM partidas_mostrador WHERE id = v_part_id FOR UPDATE;
            IF NOT FOUND OR v_part.producto_id <> v_prod.id THEN
                RAISE EXCEPTION 'Línea %: esa partida no es de %.', v_n, v_prod.nombre;
            END IF;

            v_sistema := v_part.cantidad_disponible;
            v_origen  := v_part.codigo;
            v_costo   := ROUND(COALESCE(v_part.costo_por_vara, 0));

        ELSIF v_prod.tipo = 'armado' THEN
            IF p_ubicacion <> 'vitrina' THEN
                RAISE EXCEPTION '% es un ramo armado: se cuenta en la vitrina.', v_prod.nombre;
            END IF;

            v_sistema := COALESCE(v_prod.stock_listo, 0);
            v_costo   := fn_mer_costo_ref(v_prod.id);

        ELSE
            IF p_ubicacion <> 'bodega' THEN
                RAISE EXCEPTION '% se cuenta en bodega.', v_prod.nombre;
            END IF;

            IF v_prod.controla_lotes AND v_lote_id IS NOT NULL THEN
                SELECT * INTO v_lote FROM lotes WHERE id = v_lote_id FOR UPDATE;
                IF NOT FOUND OR v_lote.producto_id <> v_prod.id THEN
                    RAISE EXCEPTION 'Línea %: ese balde no es de %.', v_n, v_prod.nombre;
                END IF;

                v_sistema := v_lote.varas_disponibles;
                v_origen  := v_lote.codigo;
                v_costo   := ROUND(v_lote.costo_por_vara);

            ELSIF v_prod.controla_lotes THEN
                -- Flor sin balde: el sistema dice 0 en un balde que aún no existe.
                v_sistema := 0;
                v_origen  := 'sin balde';
                v_costo   := fn_mer_costo_ref(v_prod.id);

            ELSE
                IF v_lote_id IS NOT NULL THEN
                    RAISE EXCEPTION '% no se maneja por lotes.', v_prod.nombre;
                END IF;

                v_sistema := COALESCE(v_prod.stock, 0);
                v_costo   := fn_mer_costo_ref(v_prod.id);
            END IF;
        END IF;

        v_dif := v_contado - v_sistema;

        IF v_dif > 0 THEN
            -- ─── Sobrante: entra donde se contó ───
            IF v_part_id IS NOT NULL THEN
                UPDATE partidas_mostrador
                   SET cantidad_disponible = cantidad_disponible + v_dif, agotada_en = NULL
                 WHERE id = v_part_id;
                v_stock := NULL;

            ELSIF v_prod.tipo = 'armado' THEN
                UPDATE productos SET stock_listo = COALESCE(stock_listo, 0) + v_dif, actualizado_en = now()
                 WHERE id = v_prod.id RETURNING stock_listo INTO v_stock;

            ELSE
                IF v_prod.controla_lotes AND v_lote_id IS NULL THEN
                    -- El balde nace acá: con vencimiento por sus días de vida y
                    -- sin precio propio (toma el de la ficha al bajar al mesón).
                    v_codigo := fn_abs_codigo_lote();
                    INSERT INTO lotes (
                        codigo, producto_id, fecha_ingreso, fecha_vencimiento,
                        varas_iniciales, varas_disponibles, costo_por_vara, estado, notas
                    )
                    VALUES (
                        v_codigo, v_prod.id, CURRENT_DATE,
                        CASE WHEN v_prod.dias_vida IS NOT NULL THEN CURRENT_DATE + v_prod.dias_vida END,
                        v_dif, v_dif, v_costo, 'activo', 'Creado en conteo · ' || v_detalle
                    )
                    RETURNING id INTO v_lote_id;
                    v_origen := v_codigo;

                ELSIF v_prod.controla_lotes THEN
                    UPDATE lotes
                       SET varas_disponibles = varas_disponibles + v_dif,
                           estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
                           actualizado_en = now()
                     WHERE id = v_lote_id;
                END IF;

                UPDATE productos SET stock = COALESCE(stock, 0) + v_dif, actualizado_en = now()
                 WHERE id = v_prod.id RETURNING stock INTO v_stock;
            END IF;

            INSERT INTO movimientos_inventario (
                producto_id, lote_id, tipo, cantidad, stock_resultante,
                motivo, detalle, usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                v_prod.id, v_lote_id, 'ajuste', v_dif, v_stock,
                'Conteo de stock', 'Sobrante en ' || p_ubicacion || ' · ' || v_detalle,
                p_usuario_id, 'conteo', v_conteo
            );

        ELSIF v_dif < 0 THEN
            -- ─── Faltante: es una merma, con el mismo registro que cualquiera ───
            v_merma := sp_mer_i_merma(
                v_prod.id, v_lote_id, v_part_id, -v_dif,
                'Faltante en conteo', v_detalle || ' (' || p_ubicacion || ')',
                'perdida', 0, NULL,
                NULL, false, v_firma, p_usuario_id);
        END IF;

        INSERT INTO conteo_lineas (
            conteo_id, producto_id, lote_id, partida_id, origen,
            sistema, contado, diferencia, costo_unitario, merma_id
        )
        VALUES (
            v_conteo, v_prod.id, v_lote_id, v_part_id, v_origen,
            v_sistema, v_contado, v_dif, COALESCE(v_costo, 0), v_merma
        );
    END LOOP;

    -- ─── Una firma por el total de faltantes ───
    SELECT COALESCE(sum(m.valor_autorizacion), 0) INTO v_valor
      FROM conteo_lineas cl JOIN mermas m ON m.id = cl.merma_id
     WHERE cl.conteo_id = v_conteo;

    v_umbral := fn_mer_umbral_autorizacion();

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Los faltantes suman $% y necesitan autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    -- ─── El resumen, para el historial ───
    UPDATE conteos c SET
        lineas         = s.lineas,
        con_diferencia = s.con_dif,
        sobrantes      = s.sobrantes,
        faltantes      = s.faltantes,
        valor_sobrante = s.v_sob,
        valor_faltante = s.v_fal
    FROM (
        SELECT count(*) AS lineas,
               count(*) FILTER (WHERE diferencia <> 0) AS con_dif,
               COALESCE(sum(diferencia) FILTER (WHERE diferencia > 0), 0) AS sobrantes,
               COALESCE(-sum(diferencia) FILTER (WHERE diferencia < 0), 0) AS faltantes,
               COALESCE(sum(diferencia * costo_unitario) FILTER (WHERE diferencia > 0), 0) AS v_sob,
               COALESCE(-sum(diferencia * costo_unitario) FILTER (WHERE diferencia < 0), 0) AS v_fal
          FROM conteo_lineas WHERE conteo_id = v_conteo
    ) s
    WHERE c.id = v_conteo;

    RETURN v_conteo;
END;
$_$;


--
-- Name: sp_inv_i_producto(text, text, integer, public.tipo_producto, numeric, numeric, numeric, text, integer, numeric, integer, boolean, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_i_producto(p_codigo text, p_nombre text, p_categoria_id integer, p_tipo public.tipo_producto, p_precio numeric, p_precio_ramo numeric DEFAULT NULL::numeric, p_precio_liquidacion numeric DEFAULT NULL::numeric, p_emoji text DEFAULT NULL::text, p_minimo integer DEFAULT 0, p_costo numeric DEFAULT 0, p_stock integer DEFAULT 0, p_controla_lotes boolean DEFAULT false, p_dias_vida integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id     int;
    v_lotes  boolean := coalesce(p_controla_lotes, false);
BEGIN
    IF btrim(coalesce(p_codigo, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el código del producto.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio < 0 THEN
        RAISE EXCEPTION 'El precio no puede ser negativo.';
    END IF;

    IF p_precio_ramo IS NOT NULL AND p_precio_ramo > p_precio THEN
        RAISE EXCEPTION 'El precio en ramo no puede superar al precio suelto.';
    END IF;

    IF p_precio_liquidacion IS NOT NULL AND p_precio_liquidacion > p_precio THEN
        RAISE EXCEPTION 'El precio de liquidación no puede superar al precio normal.';
    END IF;

    IF EXISTS (SELECT 1 FROM productos WHERE lower(codigo) = lower(btrim(p_codigo))) THEN
        RAISE EXCEPTION 'Ya existe un producto con el código %.', btrim(p_codigo);
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    -- Reglas de forma. Los CHECK las rechazarían igual, pero con un 23514
    -- que el usuario no puede interpretar.
    IF p_tipo = 'armado' THEN
        IF coalesce(p_costo, 0) > 0 THEN
            RAISE EXCEPTION 'Un producto armado no lleva costo propio: se calcula desde su receta.';
        END IF;
        IF coalesce(p_stock, 0) > 0 THEN
            RAISE EXCEPTION 'Un producto armado no lleva stock inicial: las unidades entran armándolas.';
        END IF;
        IF v_lotes THEN
            RAISE EXCEPTION 'Solo los productos simples controlan lotes.';
        END IF;
    END IF;

    INSERT INTO productos (
        codigo, nombre, categoria_id, tipo, emoji,
        precio, precio_ramo, precio_liquidacion, minimo,
        costo, stock, costo_armado, stock_listo,
        controla_lotes, dias_vida, activo
    )
    VALUES (
        btrim(p_codigo), btrim(p_nombre), p_categoria_id, p_tipo,
        coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), '🌿'),
        p_precio, p_precio_ramo, p_precio_liquidacion, coalesce(p_minimo, 0),

        -- Cada tipo llena su par. El otro va NULL explícito, no cero:
        -- los CHECK productos_forma_* exigen NULL.
        CASE WHEN p_tipo = 'simple' THEN coalesce(p_costo, 0) END,
        -- El stock inicial se ignora si controla lotes: esas existencias
        -- entran recibiendo una compra, que es lo que les da procedencia
        -- y vencimiento.
        CASE WHEN p_tipo = 'simple'
             THEN CASE WHEN v_lotes THEN 0 ELSE coalesce(p_stock, 0) END END,
        CASE WHEN p_tipo = 'armado' THEN 0 END,
        CASE WHEN p_tipo = 'armado' THEN 0 END,

        v_lotes, p_dias_vida, true
    )
    RETURNING id INTO v_id;

    -- Alta con stock: deja la huella igual que cualquier otro ingreso, para
    -- que el libro empiece cuadrado desde la primera fila.
    IF p_tipo = 'simple' AND NOT v_lotes AND coalesce(p_stock, 0) > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante, motivo, referencia_tipo
        )
        VALUES (v_id, 'alta', p_stock, p_stock, 'Stock inicial', 'producto');
    END IF;

    RETURN v_id;
END;
$$;


--
-- Name: sp_inv_u_producto(integer, text, integer, numeric, numeric, numeric, text, integer, numeric, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_u_producto(p_id integer, p_nombre text, p_categoria_id integer, p_precio numeric, p_precio_ramo numeric DEFAULT NULL::numeric, p_precio_liquidacion numeric DEFAULT NULL::numeric, p_emoji text DEFAULT NULL::text, p_minimo integer DEFAULT NULL::integer, p_costo numeric DEFAULT NULL::numeric, p_dias_vida integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_tipo tipo_producto;
BEGIN
    SELECT tipo INTO v_tipo FROM productos WHERE id = p_id;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF btrim(coalesce(p_nombre, '')) = '' THEN
        RAISE EXCEPTION 'Debe indicar el nombre del producto.';
    END IF;

    IF p_precio IS NULL OR p_precio < 0 THEN
        RAISE EXCEPTION 'El precio no puede ser negativo.';
    END IF;

    IF p_precio_ramo IS NOT NULL AND p_precio_ramo > p_precio THEN
        RAISE EXCEPTION 'El precio en ramo no puede superar al precio suelto.';
    END IF;

    IF p_precio_liquidacion IS NOT NULL AND p_precio_liquidacion > p_precio THEN
        RAISE EXCEPTION 'El precio de liquidación no puede superar al precio normal.';
    END IF;

    IF v_tipo = 'armado' AND coalesce(p_costo, 0) > 0 THEN
        RAISE EXCEPTION 'Un producto armado no lleva costo propio: se calcula desde su receta.';
    END IF;

    IF p_categoria_id IS NOT NULL
       AND NOT EXISTS (SELECT 1 FROM categorias WHERE id = p_categoria_id) THEN
        RAISE EXCEPTION 'La categoría indicada no existe.';
    END IF;

    UPDATE productos SET
        nombre             = btrim(p_nombre),
        categoria_id       = p_categoria_id,
        precio             = p_precio,
        -- Los precios secundarios SÍ se sobrescriben con NULL: dejar de
        -- tener precio de ramo es una decisión válida, y con coalesce sería
        -- imposible borrarlo una vez puesto.
        precio_ramo        = p_precio_ramo,
        precio_liquidacion = p_precio_liquidacion,
        emoji              = coalesce(nullif(btrim(coalesce(p_emoji, '')), ''), emoji),
        minimo             = coalesce(p_minimo, minimo),
        -- En un armado el costo se queda NULL: lo pone la receta.
        costo              = CASE WHEN tipo = 'simple'
                                  THEN coalesce(p_costo, costo) END,
        dias_vida          = coalesce(p_dias_vida, dias_vida),
        actualizado_en     = now()
    WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_inv_u_producto_estado(integer, boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_u_producto_estado(p_id integer, p_activo boolean) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_ramos text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF NOT p_activo THEN
        SELECT string_agg(pr.nombre, ', ' ORDER BY pr.nombre)
          INTO v_ramos
          FROM recetas r
          JOIN productos pr ON pr.id = r.producto_id
         WHERE r.componente_id = p_id
           AND pr.activo;

        IF v_ramos IS NOT NULL THEN
            RAISE EXCEPTION 'No se puede desactivar: es ingrediente de %.', v_ramos;
        END IF;
    END IF;

    UPDATE productos
       SET activo = p_activo, actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_inv_u_producto_imagen(integer, text, bigint); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_u_producto_imagen(p_producto_id integer, p_archivo text, p_version bigint) RETURNS text
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_anterior text;
BEGIN
    IF NOT EXISTS (SELECT 1 FROM productos WHERE id = p_producto_id) THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    SELECT archivo INTO v_anterior FROM producto_imagenes WHERE producto_id = p_producto_id;

    INSERT INTO producto_imagenes (producto_id, archivo, version, actualizado_en)
    VALUES (p_producto_id, p_archivo, p_version, now())
    ON CONFLICT (producto_id) DO UPDATE
        SET archivo = EXCLUDED.archivo,
            version = EXCLUDED.version,
            actualizado_en = now();

    RETURN v_anterior;
END;
$$;


--
-- Name: sp_inv_u_receta(integer, jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_inv_u_receta(p_producto_id integer, p_lineas jsonb) RETURNS TABLE(o_producto_id integer, o_componentes integer, o_costo_total numeric)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_prod   record;
    it       jsonb;
    v_comp   record;
    v_cant   int;
    v_n      int := 0;
    v_costo  numeric := 0;
BEGIN
    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.tipo <> 'armado' THEN
        RAISE EXCEPTION '% no es un producto armado: no lleva receta.', v_prod.nombre;
    END IF;

    DELETE FROM recetas WHERE producto_id = p_producto_id;

    FOR it IN SELECT * FROM jsonb_array_elements(COALESCE(p_lineas, '[]'::jsonb))
    LOOP
        v_cant := COALESCE((it->>'cantidad')::int, 0);

        IF v_cant < 1 THEN
            RAISE EXCEPTION 'Cada componente tiene que llevar al menos 1 unidad.';
        END IF;

        SELECT * INTO v_comp
          FROM productos WHERE id = (it->>'componenteId')::int;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'Uno de los componentes no existe.';
        END IF;

        -- Un armado dentro de otro obligaría a resolver el costo de forma
        -- recursiva, y ese cálculo se desincroniza al primer cambio de
        -- precio de un componente de tercer nivel.
        IF v_comp.tipo <> 'simple' THEN
            RAISE EXCEPTION '% es un armado: solo pueden entrar productos simples.',
                v_comp.nombre;
        END IF;

        IF v_comp.id = p_producto_id THEN
            RAISE EXCEPTION 'Un producto no puede llevarse a sí mismo.';
        END IF;

        INSERT INTO recetas (producto_id, componente_id, cantidad)
        VALUES (p_producto_id, v_comp.id, v_cant)
        -- Si el formulario manda el mismo componente dos veces, se suma en
        -- vez de reventar: es más probable que sea un descuido que una
        -- intención.
        ON CONFLICT (producto_id, componente_id)
        DO UPDATE SET cantidad = recetas.cantidad + EXCLUDED.cantidad;

        v_n := v_n + 1;
        v_costo := v_costo + (v_cant * COALESCE(v_comp.costo, 0));
    END LOOP;

    RETURN QUERY SELECT p_producto_id, v_n, v_costo;
END;
$$;


--
-- Name: sp_lot_c_costo_promedio(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_costo_promedio() RETURNS TABLE(producto_id integer, producto text, emoji text, lotes bigint, varas bigint, costo_promedio numeric, costo_minimo numeric, costo_maximo numeric, valor_total numeric)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        l.producto_id, p.nombre, p.emoji,
        count(*),
        sum(l.varas_disponibles),
        ROUND(sum(l.varas_disponibles * l.costo_por_vara)
              / NULLIF(sum(l.varas_disponibles), 0), 2),
        min(l.costo_por_vara),
        max(l.costo_por_vara),
        ROUND(sum(l.varas_disponibles * l.costo_por_vara))
    FROM lotes l
    JOIN productos p ON p.id = l.producto_id
    WHERE l.estado = 'activo' AND l.varas_disponibles > 0
    GROUP BY l.producto_id, p.nombre, p.emoji
    ORDER BY p.nombre;
$$;


--
-- Name: sp_lot_c_etiquetas(integer[]); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_etiquetas(p_ids integer[]) RETURNS TABLE(lote_id integer, codigo text, producto text, emoji text, proveedor text, presentacion text, fecha_ingreso date, fecha_vencimiento date, varas integer, ubicacion text)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.codigo, v.producto, v.emoji, v.proveedor, v.presentacion,
        v.fecha_ingreso, v.fecha_vencimiento,
        v.varas_iniciales, v.ubicacion
    FROM vw_lotes_calculado v
    WHERE v.id = ANY(p_ids)
    ORDER BY v.producto, v.id;
$$;


--
-- Name: sp_lot_c_lote(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_lote(p_id integer) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, proveedor text, presentacion text, compra_folio text, documento text, fecha_ingreso date, fecha_vencimiento date, dias_en_camara integer, dias_para_vencer integer, varas_iniciales integer, varas_disponibles integer, porcentaje_vendido numeric, costo_por_vara numeric, valor_restante numeric, precio_venta integer, precio_unitario integer, rebaja integer, rebaja_porcentaje numeric, orden_fifo integer, alerta text, es_recuperado boolean, requiere_escaneo boolean, calidad public.calidad_reingreso, lote_origen text, ubicacion text, notas text, estado public.estado_lote, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.codigo, v.producto_id, v.producto, v.emoji,
        v.proveedor, v.presentacion, v.compra_folio, v.documento,
        v.fecha_ingreso, v.fecha_vencimiento,
        v.dias_en_camara, v.dias_para_vencer,
        v.varas_iniciales, v.varas_disponibles, v.porcentaje_vendido,
        v.costo_por_vara, v.valor_restante,
        v.precio_venta, v.precio_unitario, v.rebaja, v.rebaja_porcentaje,
        v.orden_fifo, fn_lot_alerta(v.dias_para_vencer, v.rezagado),
        v.es_recuperado, v.requiere_escaneo,
        v.calidad, v.lote_origen, v.ubicacion, v.notas, v.estado,
        1::bigint
    FROM vw_lotes_calculado v
    WHERE v.id = p_id;
$$;


--
-- Name: sp_lot_c_lote_codigo(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_lote_codigo(p_codigo text) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, proveedor text, presentacion text, compra_folio text, documento text, fecha_ingreso date, fecha_vencimiento date, dias_en_camara integer, dias_para_vencer integer, varas_iniciales integer, varas_disponibles integer, porcentaje_vendido numeric, costo_por_vara numeric, valor_restante numeric, precio_venta integer, precio_unitario integer, rebaja integer, rebaja_porcentaje numeric, orden_fifo integer, alerta text, es_recuperado boolean, requiere_escaneo boolean, calidad public.calidad_reingreso, lote_origen text, ubicacion text, notas text, estado public.estado_lote, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH entrada AS (
        SELECT CASE
                 WHEN btrim(p_codigo) LIKE 'CLB1*%' THEN split_part(btrim(p_codigo), '*', 5)
                 WHEN btrim(p_codigo) LIKE 'CLB1|%' THEN split_part(btrim(p_codigo), '|', 5)
                 ELSE upper(btrim(p_codigo))
               END AS cod
    )
    SELECT
        v.id, v.codigo, v.producto_id, v.producto, v.emoji,
        v.proveedor, v.presentacion, v.compra_folio, v.documento,
        v.fecha_ingreso, v.fecha_vencimiento,
        v.dias_en_camara, v.dias_para_vencer,
        v.varas_iniciales, v.varas_disponibles, v.porcentaje_vendido,
        v.costo_por_vara, v.valor_restante,
        v.precio_venta, v.precio_unitario, v.rebaja, v.rebaja_porcentaje,
        v.orden_fifo, fn_lot_alerta(v.dias_para_vencer, v.rezagado),
        v.es_recuperado, v.requiere_escaneo,
        v.calidad, v.lote_origen, v.ubicacion, v.notas, v.estado,
        1::bigint
    FROM entrada e
    JOIN vw_lotes_calculado v ON v.codigo = e.cod;
$$;


--
-- Name: sp_lot_c_lotes(text, integer, integer, text, boolean, boolean, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_lotes(p_buscar text DEFAULT NULL::text, p_producto_id integer DEFAULT NULL::integer, p_proveedor_id integer DEFAULT NULL::integer, p_alerta text DEFAULT NULL::text, p_solo_rezagados boolean DEFAULT false, p_historial boolean DEFAULT false, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, proveedor text, presentacion text, fecha_ingreso date, fecha_vencimiento date, dias_en_camara integer, dias_para_vencer integer, varas_iniciales integer, varas_disponibles integer, porcentaje_vendido numeric, costo_por_vara numeric, valor_restante numeric, precio_venta integer, precio_unitario integer, rebaja integer, rebaja_porcentaje numeric, orden_fifo integer, alerta text, es_recuperado boolean, requiere_escaneo boolean, calidad public.calidad_reingreso, lote_origen text, ubicacion text, estado public.estado_lote, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.codigo, v.producto_id, v.producto, v.emoji,
        v.proveedor, v.presentacion,
        v.fecha_ingreso, v.fecha_vencimiento,
        v.dias_en_camara, v.dias_para_vencer,
        v.varas_iniciales, v.varas_disponibles, v.porcentaje_vendido,
        v.costo_por_vara, v.valor_restante,
        v.precio_venta, v.precio_unitario, v.rebaja, v.rebaja_porcentaje,
        v.orden_fifo,
        fn_lot_alerta(v.dias_para_vencer, v.rezagado),
        v.es_recuperado, v.requiere_escaneo,
        v.calidad, v.lote_origen, v.ubicacion, v.estado,
        count(*) OVER ()
    FROM vw_lotes_calculado v
    WHERE (p_historial OR (v.estado = 'activo' AND v.varas_disponibles > 0))
      AND (p_buscar IS NULL
           OR v.codigo    ILIKE '%' || p_buscar || '%'
           OR v.producto  ILIKE '%' || p_buscar || '%'
           OR v.proveedor ILIKE '%' || p_buscar || '%')
      AND (p_producto_id  IS NULL OR v.producto_id  = p_producto_id)
      AND (p_proveedor_id IS NULL OR EXISTS (
              SELECT 1 FROM lotes l WHERE l.id = v.id AND l.proveedor_id = p_proveedor_id))
      AND (p_alerta IS NULL OR fn_lot_alerta(v.dias_para_vencer, v.rezagado) = p_alerta)
      AND (NOT COALESCE(p_solo_rezagados, false) OR v.rezagado)
    -- Lo urgente primero: lo que vence antes, y los sin fecha al final.
    ORDER BY v.dias_para_vencer NULLS LAST, v.fecha_ingreso, v.id
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_lot_c_movimientos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_movimientos(p_lote_id integer) RETURNS TABLE(id integer, fecha timestamp with time zone, tipo public.tipo_movimiento, cantidad integer, motivo text, detalle text, usuario text)
    LANGUAGE sql STABLE
    AS $$
    SELECT m.id, m.creado_en, m.tipo, m.cantidad, m.motivo, m.detalle, u.nombre
    FROM movimientos_inventario m
    LEFT JOIN usuarios u ON u.id = m.usuario_id
    WHERE m.lote_id = p_lote_id
    ORDER BY m.creado_en DESC, m.id DESC;
$$;


--
-- Name: sp_lot_c_por_vencer(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_por_vencer(p_dias integer DEFAULT 3) RETURNS TABLE(id integer, codigo text, producto text, emoji text, varas_disponibles integer, valor_restante numeric, fecha_vencimiento date, dias_para_vencer integer, alerta text)
    LANGUAGE sql STABLE
    AS $$
    SELECT v.id, v.codigo, v.producto, v.emoji,
           v.varas_disponibles, v.valor_restante,
           v.fecha_vencimiento, v.dias_para_vencer,
           fn_lot_alerta(v.dias_para_vencer, v.rezagado)
    FROM vw_lotes_calculado v
    WHERE v.estado = 'activo'
      AND v.varas_disponibles > 0
      AND v.dias_para_vencer IS NOT NULL
      AND v.dias_para_vencer <= GREATEST(p_dias, 0)
    ORDER BY v.dias_para_vencer, v.valor_restante DESC;
$$;


--
-- Name: sp_lot_c_recuperados(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_recuperados() RETURNS TABLE(id integer, codigo text, producto text, emoji text, varas_disponibles integer, dias_en_camara integer, calidad public.calidad_reingreso, lote_origen text, precio_venta integer, rebaja integer, rebaja_porcentaje numeric)
    LANGUAGE sql STABLE
    AS $$
    SELECT v.id, v.codigo, v.producto, v.emoji,
           v.varas_disponibles, v.dias_en_camara,
           v.calidad, v.lote_origen,
           v.precio_venta, v.rebaja, v.rebaja_porcentaje
    FROM vw_lotes_calculado v
    WHERE v.estado = 'activo'
      AND v.varas_disponibles > 0
      AND v.es_recuperado
    ORDER BY v.dias_en_camara DESC;
$$;


--
-- Name: sp_lot_c_rezagados(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_rezagados() RETURNS TABLE(id integer, codigo text, producto text, emoji text, varas_disponibles integer, valor_restante numeric, dias_en_camara integer, dias_para_vencer integer, alerta text)
    LANGUAGE sql STABLE
    AS $$
    SELECT v.id, v.codigo, v.producto, v.emoji,
           v.varas_disponibles, v.valor_restante,
           v.dias_en_camara, v.dias_para_vencer,
           fn_lot_alerta(v.dias_para_vencer, v.rezagado)
    FROM vw_lotes_calculado v
    WHERE v.estado = 'activo'
      AND v.varas_disponibles > 0
      AND v.rezagado
      -- Un vencido ya no es "por liquidar": es merma, y va en otra lista.
      AND COALESCE(v.dias_para_vencer, 999) >= 0
    ORDER BY v.dias_para_vencer NULLS LAST, v.valor_restante DESC;
$$;


--
-- Name: sp_lot_c_validar(text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_c_validar(p_codigo text, p_cantidad integer DEFAULT 1) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, precio_venta integer, precio_producto integer, rebaja integer, varas_disponibles integer, fecha_vencimiento date, dias_para_vencer integer, es_recuperado boolean, requiere_escaneo boolean, calidad public.calidad_reingreso, estado public.estado_lote, puede boolean, motivo text, advertencia text)
    LANGUAGE sql STABLE
    AS $$
    WITH entrada AS (
        SELECT CASE
                 WHEN btrim(p_codigo) LIKE 'CLB1*%' THEN split_part(btrim(p_codigo), '*', 5)
                 WHEN btrim(p_codigo) LIKE 'CLB1|%' THEN split_part(btrim(p_codigo), '|', 5)
                 ELSE upper(btrim(p_codigo))
               END AS cod
    )
    SELECT
        v.id, v.codigo, v.producto_id, v.producto, v.emoji,
        v.precio_venta, v.precio_producto, v.rebaja,
        v.varas_disponibles, v.fecha_vencimiento, v.dias_para_vencer,
        v.es_recuperado, v.requiere_escaneo, v.calidad, v.estado,

        (v.estado = 'activo'
         AND v.varas_disponibles >= GREATEST(p_cantidad, 1)
         AND COALESCE(v.dias_para_vencer, 999) >= 0),

        -- Por qué no, cuando la respuesta es no: un "no se puede" sin razón
        -- deja al vendedor con el cliente enfrente y sin qué decirle.
        CASE
            WHEN v.estado = 'agotado'    THEN 'Este lote ya se agotó.'
            WHEN v.estado = 'descartado' THEN 'Este lote fue descartado.'
            WHEN v.varas_disponibles = 0 THEN 'No quedan varas en este lote.'
            WHEN v.varas_disponibles < GREATEST(p_cantidad, 1)
                THEN 'Solo quedan ' || v.varas_disponibles || ' varas en este lote.'
            WHEN COALESCE(v.dias_para_vencer, 999) < 0
                THEN 'Venció el ' || to_char(v.fecha_vencimiento, 'DD-MM-YYYY') || '.'
        END,

        -- Se puede vender, pero conviene decirlo en voz alta.
        CASE
            WHEN v.dias_para_vencer = 0 THEN 'Vence hoy.'
            WHEN v.dias_para_vencer BETWEEN 1 AND 2
                THEN 'Vence en ' || v.dias_para_vencer || ' día(s).'
            WHEN v.es_recuperado
                THEN 'Flor recuperada' ||
                     COALESCE(' · calidad ' || v.calidad::text, '') ||
                     ' · precio rebajado.'
        END

    FROM entrada e
    JOIN vw_lotes_calculado v ON v.codigo = e.cod;
$$;


--
-- Name: sp_lot_u_ubicacion(integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_lot_u_ubicacion(p_id integer, p_ubicacion text, p_usuario_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
BEGIN
    IF NOT EXISTS (SELECT 1 FROM lotes WHERE id = p_id) THEN
        RAISE EXCEPTION 'El lote no existe.';
    END IF;

    IF btrim(coalesce(p_ubicacion, '')) = '' THEN
        RAISE EXCEPTION 'Indica dónde está el paquete.';
    END IF;

    UPDATE lotes
       SET ubicacion = btrim(p_ubicacion),
           actualizado_en = now()
     WHERE id = p_id;

    RETURN p_id;
END;
$$;


--
-- Name: sp_mer_c_escanear(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_escanear(p_codigo text) RETURNS TABLE(origen text, lote_id integer, partida_id integer, codigo text, producto_id integer, producto text, emoji text, controla_lotes boolean, disponible integer, costo_unitario numeric, precio integer, fecha_ingreso date, fecha_vencimiento date, dias_para_vencer integer, valor_total numeric, ubicacion text, proveedor text, puede_mermar boolean, motivo_bloqueo text, sugerencia text)
    LANGUAGE sql STABLE
    AS $$
    WITH entrada AS (
        -- El código legible es siempre el último campo del QR.
        SELECT CASE
                 WHEN btrim(p_codigo) LIKE 'CLB1*%' THEN split_part(btrim(p_codigo), '*', 5)
                 WHEN btrim(p_codigo) LIKE 'CLB1|%' THEN split_part(btrim(p_codigo), '|', 5)
                 ELSE upper(btrim(p_codigo))
               END AS cod
    ),
    lote AS (
        SELECT
            'lote'::text AS origen,
            l.id AS lote_id, NULL::int AS partida_id, l.codigo,
            l.producto_id, p.nombre AS producto, p.emoji, p.controla_lotes,
            l.varas_disponibles AS disponible,
            l.costo_por_vara AS costo_unitario,
            COALESCE(l.precio_unitario, p.precio) AS precio,
            l.fecha_ingreso, l.fecha_vencimiento,
            CASE WHEN l.fecha_vencimiento IS NOT NULL
                 THEN (l.fecha_vencimiento - CURRENT_DATE)::int END AS dias,
            l.ubicacion, pr.nombre AS proveedor,
            l.estado::text AS estado
        FROM entrada e
        JOIN lotes l ON l.codigo = e.cod
        JOIN productos p ON p.id = l.producto_id
        LEFT JOIN proveedores pr ON pr.id = l.proveedor_id
    ),
    partida AS (
        SELECT
            'partida'::text, NULL::int, pm.id, pm.codigo,
            pm.producto_id, p.nombre, p.emoji, p.controla_lotes,
            pm.cantidad_disponible,
            COALESCE(pm.costo_por_vara, 0),
            pm.precio_unitario,
            pm.traspasado_en::date, pm.fecha_vencimiento,
            CASE WHEN pm.fecha_vencimiento IS NOT NULL
                 THEN (pm.fecha_vencimiento - CURRENT_DATE)::int END,
            'Mostrador'::text, NULL::text,
            CASE WHEN pm.cantidad_disponible > 0 THEN 'activo' ELSE 'agotado' END
        FROM entrada e
        JOIN partidas_mostrador pm ON pm.codigo = e.cod
        JOIN productos p ON p.id = pm.producto_id
    ),
    todo AS (
        SELECT * FROM lote UNION ALL SELECT * FROM partida
    )
    SELECT
        t.origen, t.lote_id, t.partida_id, t.codigo,
        t.producto_id, t.producto, t.emoji, t.controla_lotes,
        t.disponible, t.costo_unitario, t.precio,
        t.fecha_ingreso, t.fecha_vencimiento, t.dias,

        -- Lo que vale lo que queda ahí. Es el número que decide si la
        -- merma necesita autorización.
        ROUND(t.disponible * t.costo_unitario),

        t.ubicacion, t.proveedor,

        (t.estado = 'activo' AND t.disponible > 0),

        CASE
            WHEN t.disponible = 0 THEN 'Ya no queda nada acá.'
            WHEN t.estado = 'descartado' THEN 'Este lote ya fue dado de baja.'
        END,

        -- Lo que conviene hacer, según lo que se escaneó.
        CASE
            WHEN t.dias IS NOT NULL AND t.dias < 0
                THEN 'Vencido hace ' || abs(t.dias) || ' día(s): corresponde darlo de baja entero.'
            WHEN t.dias IS NOT NULL AND t.dias <= 1
                THEN 'Vence mañana: si no se va a vender, mejor liquidarlo que botarlo.'
            WHEN t.origen = 'partida'
                THEN 'Está en el mostrador. Si se puede recuperar, vuelve a bodega con precio rebajado.'
        END
    FROM todo t;
$$;


--
-- Name: sp_mer_c_evidencia(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_evidencia(p_id integer) RETURNS TABLE(foto bytea, tipo_mime text, reporte_id integer, usuario_id integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT e.foto, e.tipo_mime, e.reporte_id, r.usuario_id
      FROM merma_evidencias e
      JOIN merma_reportes r ON r.id = e.reporte_id
     WHERE e.id = p_id;
$$;


--
-- Name: sp_mer_c_evidencias(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_evidencias(p_reporte_id integer) RETURNS TABLE(id integer, tipo_mime text, bytes integer, sha256 text, repetida boolean, creado_en timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
    SELECT e.id, e.tipo_mime, e.bytes, e.sha256,
           EXISTS (SELECT 1 FROM merma_evidencias o WHERE o.sha256 = e.sha256 AND o.id <> e.id),
           e.creado_en
      FROM merma_evidencias e
     WHERE e.reporte_id = p_reporte_id
     ORDER BY e.id;
$$;


--
-- Name: sp_mer_c_merma(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_merma(p_id integer, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, partida_id integer, origen_codigo text, origen text, cantidad integer, motivo text, detalle text, costo_unitario integer, costo_total integer, destino public.destino_merma, cantidad_recuperada integer, cantidad_perdida integer, calidad_reingreso public.calidad_reingreso, lote_recuperacion_id integer, lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer, escaneado boolean, autorizado_por text, usuario text, revertida boolean, revertida_por text, revertida_en timestamp with time zone, motivo_reversion text, desarme_grupo integer, reporte_id integer, fotos integer, usuario_id integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        m.id, m.producto_id, p.nombre, p.emoji,
        m.lote_id, m.partida_id,
        COALESCE(l.codigo, pm.codigo),
        CASE WHEN m.partida_id IS NOT NULL    THEN 'mostrador'
             WHEN m.lote_id IS NOT NULL       THEN 'bodega'
             WHEN m.desarme_grupo IS NOT NULL THEN 'desarme'
             ELSE 'stock' END,
        m.cantidad, m.motivo, m.detalle,
        m.costo_unitario, m.costo_total, m.destino,
        m.cantidad_recuperada, m.cantidad - m.cantidad_recuperada,
        m.calidad_reingreso, m.lote_recuperacion_id, lr.codigo,
        m.costo_recuperado_unitario, m.costo_perdido,
        m.escaneado, m.autorizado_por,
        u.nombre, m.revertida, ur.nombre, m.revertida_en,
        m.motivo_reversion, m.desarme_grupo,
        m.reporte_id,
        (SELECT count(*)::int FROM merma_evidencias e WHERE e.reporte_id = m.reporte_id),
        m.usuario_id,
        m.creado_en, 1::bigint
    FROM mermas m
    JOIN productos p       ON p.id = m.producto_id
    LEFT JOIN lotes l      ON l.id = m.lote_id
    LEFT JOIN partidas_mostrador pm ON pm.id = m.partida_id
    LEFT JOIN lotes lr     ON lr.id = m.lote_recuperacion_id
    LEFT JOIN usuarios u   ON u.id = m.usuario_id
    LEFT JOIN usuarios ur  ON ur.id = m.revertida_por
    WHERE m.id = p_id
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id);
$$;


--
-- Name: sp_mer_c_mermas(text, integer, integer, text, public.destino_merma, boolean, date, date, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_mermas(p_buscar text DEFAULT NULL::text, p_producto_id integer DEFAULT NULL::integer, p_lote_id integer DEFAULT NULL::integer, p_motivo text DEFAULT NULL::text, p_destino public.destino_merma DEFAULT NULL::public.destino_merma, p_revertida boolean DEFAULT false, p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, partida_id integer, origen_codigo text, origen text, cantidad integer, motivo text, detalle text, costo_unitario integer, costo_total integer, destino public.destino_merma, cantidad_recuperada integer, cantidad_perdida integer, calidad_reingreso public.calidad_reingreso, lote_recuperacion_id integer, lote_recuperacion text, costo_recuperado_unitario integer, costo_perdido integer, escaneado boolean, autorizado_por text, usuario text, revertida boolean, revertida_por text, revertida_en timestamp with time zone, motivo_reversion text, desarme_grupo integer, reporte_id integer, fotos integer, usuario_id integer, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        m.id,
        m.producto_id, p.nombre, p.emoji,
        m.lote_id, m.partida_id,
        COALESCE(l.codigo, pm.codigo),
        CASE WHEN m.partida_id IS NOT NULL    THEN 'mostrador'
             WHEN m.lote_id IS NOT NULL       THEN 'bodega'
             WHEN m.desarme_grupo IS NOT NULL THEN 'desarme'
             ELSE 'stock' END,
        m.cantidad, m.motivo, m.detalle,
        m.costo_unitario, m.costo_total,
        m.destino,
        m.cantidad_recuperada,
        m.cantidad - m.cantidad_recuperada,
        m.calidad_reingreso,
        m.lote_recuperacion_id, lr.codigo,
        m.costo_recuperado_unitario, m.costo_perdido,
        m.escaneado, m.autorizado_por,
        u.nombre,
        m.revertida, ur.nombre, m.revertida_en,
        m.motivo_reversion, m.desarme_grupo,
        m.reporte_id,
        (SELECT count(*)::int FROM merma_evidencias e WHERE e.reporte_id = m.reporte_id),
        m.usuario_id,
        m.creado_en,
        count(*) OVER ()
    FROM mermas m
    JOIN productos p       ON p.id = m.producto_id
    LEFT JOIN lotes l      ON l.id = m.lote_id
    LEFT JOIN partidas_mostrador pm ON pm.id = m.partida_id
    LEFT JOIN lotes lr     ON lr.id = m.lote_recuperacion_id
    LEFT JOIN usuarios u   ON u.id = m.usuario_id
    LEFT JOIN usuarios ur  ON ur.id = m.revertida_por
    WHERE (p_buscar IS NULL
           OR p.nombre ILIKE '%' || p_buscar || '%'
           OR m.motivo ILIKE '%' || p_buscar || '%'
           OR l.codigo ILIKE '%' || p_buscar || '%'
           OR pm.codigo ILIKE '%' || p_buscar || '%')
      AND (p_producto_id IS NULL OR m.producto_id = p_producto_id)
      AND (p_lote_id     IS NULL OR m.lote_id     = p_lote_id)
      AND (p_motivo      IS NULL OR m.motivo      = p_motivo)
      AND (p_destino     IS NULL OR m.destino     = p_destino)
      AND (p_revertida   IS NULL OR m.revertida   = p_revertida)
      AND (p_desde IS NULL OR m.creado_en >= p_desde)
      AND (p_hasta IS NULL OR m.creado_en <  p_hasta + 1)
      AND (p_usuario_id  IS NULL OR m.usuario_id  = p_usuario_id)
    ORDER BY m.creado_en DESC, m.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_mer_c_motivos(boolean); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_motivos(p_todos boolean DEFAULT false) RETURNS TABLE(id integer, motivo text, categoria text, requiere_detalle boolean, destino_sugerido public.destino_merma, activo boolean, orden integer, usos bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_mer_c_patron_horario(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_patron_horario(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(hora integer, registros bigint, costo_perdido bigint, sin_escanear bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        extract(hour FROM m.creado_en)::int,
        count(*),
        sum(m.costo_perdido),
        count(*) FILTER (WHERE NOT m.escaneado)
    FROM mermas m
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
    GROUP BY 1
    ORDER BY 1;
$$;


--
-- Name: sp_mer_c_patron_usuario(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_patron_usuario(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(usuario_id integer, usuario text, rol public.rol_usuario, registros bigint, unidades bigint, costo_perdido bigint, sin_escanear bigint, porcentaje_sin_escanear numeric, monto_promedio numeric, producto_top text, motivo_top text)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_mer_c_plan_desarme(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_plan_desarme(p_producto_id integer, p_cantidad integer DEFAULT 1) RETURNS TABLE(componente_id integer, componente text, emoji text, controla_lotes boolean, por_unidad integer, total integer, costo_unitario numeric, precio integer, dias_vida integer)
    LANGUAGE sql STABLE
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


--
-- Name: sp_mer_c_reportes_pendientes(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_reportes_pendientes(p_limite integer DEFAULT 50) RETURNS TABLE(reporte_id integer, tipo text, motivo text, detalle text, usuario text, creado_en timestamp with time zone, autorizado_por text, lineas text, costo_perdido integer, fotos integer, foto_repetida boolean, sin_escanear integer)
    LANGUAGE sql STABLE
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


--
-- Name: sp_mer_c_resumen(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_resumen(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(desde date, hasta date, registros bigint, unidades bigint, unidades_perdidas bigint, unidades_recuperadas bigint, costo_total bigint, costo_perdido bigint, costo_recuperado bigint, costo_botado bigint, costo_desvalorizado bigint, costo_devuelto bigint, ventas_periodo bigint, porcentaje_sobre_ventas numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH rango AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE)      AS h
    ),
    m AS (
        SELECT me.*
        FROM mermas me, rango r
        WHERE NOT me.revertida
          AND me.creado_en >= r.d
          AND me.creado_en <  r.h + 1
          AND (p_usuario_id IS NULL OR me.usuario_id = p_usuario_id)
    ),
    -- Las ventas del local son del administrador. Para cualquier otro no
    -- se calculan: el porcentaje sobre ventas es un número del negocio,
    -- no de una persona.
    v AS (
        SELECT CASE WHEN p_usuario_id IS NULL
                    THEN COALESCE(sum(ve.total), 0) END AS vendido
        FROM ventas ve, rango r
        WHERE p_usuario_id IS NULL
          AND NOT ve.anulada
          AND ve.creado_en >= r.d
          AND ve.creado_en <  r.h + 1
    )
    SELECT
        r.d, r.h,
        (SELECT count(*) FROM m),
        (SELECT COALESCE(sum(cantidad), 0) FROM m),
        (SELECT COALESCE(sum(cantidad - cantidad_recuperada), 0) FROM m),
        (SELECT COALESCE(sum(cantidad_recuperada), 0) FROM m),
        (SELECT COALESCE(sum(costo_total), 0) FROM m),
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m),

        -- Lo que sigue valiendo algo porque volvió al stock rebajado.
        (SELECT COALESCE(sum(cantidad_recuperada * COALESCE(costo_recuperado_unitario, 0)), 0)
         FROM m),

        -- Se botó de verdad: no volvió nada.
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m WHERE destino = 'perdida'),

        -- Volvió pero vale menos: solo se perdió la rebaja.
        (SELECT COALESCE(sum(costo_perdido), 0) FROM m WHERE destino = 'reingreso'),

        -- El proveedor lo abona: NO es costo, y contarlo como merma haría
        -- ver mal a quien compró bien.
        (SELECT COALESCE(sum(costo_total), 0) FROM m WHERE destino = 'devolucion_proveedor'),

        v.vendido,

        -- Sobre 5% en una florería es señal de que se compra más de lo que
        -- se alcanza a vender.
        CASE WHEN v.vendido > 0
             THEN ROUND((SELECT COALESCE(sum(costo_perdido), 0) FROM m)::numeric
                        / v.vendido * 100, 2)
             END
    FROM rango r, v;
$$;


--
-- Name: sp_mer_c_resumen_categoria(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_resumen_categoria(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(categoria text, registros bigint, unidades bigint, costo_perdido bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_mer_c_resumen_destino(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_resumen_destino(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(destino public.destino_merma, registros bigint, unidades bigint, costo_perdido bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT m.destino, count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.destino
    ORDER BY 4 DESC;
$$;


--
-- Name: sp_mer_c_resumen_motivo(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_resumen_motivo(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(motivo text, registros bigint, unidades bigint, costo_perdido bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT m.motivo, count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.motivo
    ORDER BY 4 DESC
    LIMIT 10;
$$;


--
-- Name: sp_mer_c_resumen_producto(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_resumen_producto(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(producto_id integer, producto text, emoji text, registros bigint, unidades bigint, unidades_recuperadas bigint, costo_perdido bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT m.producto_id, p.nombre, p.emoji,
           count(*), COALESCE(sum(m.cantidad), 0),
           COALESCE(sum(m.cantidad_recuperada), 0),
           COALESCE(sum(m.costo_perdido), 0)
    FROM mermas m
    JOIN productos p ON p.id = m.producto_id
    WHERE NOT m.revertida
      AND m.creado_en >= COALESCE(p_desde, CURRENT_DATE - 30)
      AND m.creado_en <  COALESCE(p_hasta, CURRENT_DATE) + 1
      AND (p_usuario_id IS NULL OR m.usuario_id = p_usuario_id)
    GROUP BY m.producto_id, p.nombre, p.emoji
    ORDER BY 7 DESC
    LIMIT 15;
$$;


--
-- Name: sp_mer_c_sin_escanear(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_c_sin_escanear(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(id integer, producto text, emoji text, cantidad integer, costo_perdido integer, motivo text, detalle text, lote_codigo text, usuario text, creado_en timestamp with time zone)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_mer_i_desarme(integer, integer, text, text, jsonb, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_desarme(p_producto_id integer, p_cantidad integer, p_motivo text, p_detalle text, p_lineas jsonb, p_usuario_id integer DEFAULT NULL::integer, p_autorizado_por text DEFAULT NULL::text) RETURNS TABLE(o_producto_id integer, o_producto text, o_desarmados integer, o_mermas integer, o_recuperadas integer, o_perdidas integer)
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_prod      record;
    r           record;
    it          jsonb;
    v_rec       int;
    v_perd      int;
    v_calidad   calidad_reingreso;
    v_merma_id  int;
    v_mermas    int := 0;
    v_tot_rec   int := 0;
    v_tot_perd  int := 0;
    v_grupo     int;
    v_umbral    int;
    v_valor     int;
    v_mot       motivos_merma;
    v_firma     text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
BEGIN
    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'Indica cuántas unidades desarmar.';
    END IF;

    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica el motivo del desarme.';
    END IF;

    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.tipo <> 'armado' THEN
        RAISE EXCEPTION '% no es un producto armado: no hay nada que desarmar.', v_prod.nombre;
    END IF;

    IF COALESCE(v_prod.stock_listo, 0) < p_cantidad THEN
        RAISE EXCEPTION 'Solo hay % de % armados.', COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
    END IF;

    IF NOT EXISTS (SELECT 1 FROM recetas WHERE producto_id = p_producto_id) THEN
        RAISE EXCEPTION '% no tiene receta: no se sabe qué componentes lleva.', v_prod.nombre;
    END IF;

    -- ═══ Autorización ═══
    -- La misma regla que una merma suelta: lo que vale lo que sale, contra
    -- el umbral. Sin esto, desarmar era la forma de mermar caro sin firma.
    v_umbral := fn_mer_umbral_autorizacion();

    -- Costo real de cada componente (en las flores, el de sus lotes); sin
    -- costo, su precio de venta, por la misma razón que en la merma suelta.
    SELECT COALESCE(sum(rc.cantidad * p_cantidad *
                        COALESCE(NULLIF(fn_mer_costo_ref(p.id), 0), p.precio)), 0)
      INTO v_valor
      FROM recetas rc
      JOIN productos p ON p.id = rc.componente_id
     WHERE rc.producto_id = p_producto_id;

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Un desarme de $% necesita autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    v_grupo := nextval('seq_mer_desarme_grupo');

    -- ═══ Las unidades armadas salen del stock ═══
    UPDATE productos
       SET stock_listo = stock_listo - p_cantidad, actualizado_en = now()
     WHERE id = p_producto_id;

    -- ═══ Cada componente, según lo que decidió la persona ═══
    FOR r IN
        SELECT rc.componente_id, rc.cantidad * p_cantidad AS esperado, p.nombre,
               p.controla_lotes AS con_lotes
        FROM recetas rc
        JOIN productos p ON p.id = rc.componente_id
        WHERE rc.producto_id = p_producto_id
    LOOP
        SELECT value INTO it
          FROM jsonb_array_elements(COALESCE(p_lineas, '[]'::jsonb)) value
         WHERE (value->>'componenteId')::int = r.componente_id
         LIMIT 1;

        IF it IS NULL THEN
            RAISE EXCEPTION 'Falta decir qué pasa con las % varas de %.', r.esperado, r.nombre;
        END IF;

        v_rec     := GREATEST(COALESCE((it->>'recuperadas')::int, 0), 0);
        v_perd    := GREATEST(COALESCE((it->>'perdidas')::int, 0), 0);
        v_calidad := nullif(it->>'calidad', '')::calidad_reingreso;

        -- Cada vara tiene que tener un destino: si la suma no cuadra, el
        -- inventario queda mintiendo.
        IF v_rec + v_perd <> r.esperado THEN
            RAISE EXCEPTION
                '% : la receta dice % varas y estás repartiendo % (% recuperadas + % perdidas).',
                r.nombre, r.esperado, v_rec + v_perd, v_rec, v_perd;
        END IF;

        IF v_rec > 0 AND v_calidad IS NULL THEN
            RAISE EXCEPTION 'Indica en qué estado vuelven las % varas de %.', v_rec, r.nombre;
        END IF;

        -- Una merma por componente. El stock de los componentes NO se tocó
        -- al armar el ramo —se descontó entonces— así que acá solo entra lo
        -- que se recupera.
        --
        -- Se pasa lote_id NULL: las varas ya perdieron su identidad de lote
        -- cuando entraron al arreglo, y adivinar de cuál venían sería
        -- inventar trazabilidad.
        INSERT INTO mermas (
            producto_id, lote_id, cantidad, motivo, motivo_id, detalle,
            costo_unitario, costo_total, usuario_id,
            destino, cantidad_recuperada, calidad_reingreso, costo_recuperado_unitario,
            autorizado_por, desarme_grupo, desarme_producto_id, desarme_cantidad
        )
        SELECT
            r.componente_id, NULL, r.esperado,
            v_mot.nombre, v_mot.id,
            'Desarme de ' || p_cantidad || ' × ' || v_prod.nombre ||
            COALESCE(' · ' || nullif(btrim(coalesce(p_detalle, '')), ''), ''),
            fn_mer_costo_ref(p.id),
            r.esperado * fn_mer_costo_ref(p.id),
            p_usuario_id,
            CASE WHEN v_rec > 0 THEN 'reingreso'::destino_merma
                 ELSE 'perdida'::destino_merma END,
            v_rec, v_calidad,
            -- costo_perdido lo calcula la tabla a partir de este valor: sin
            -- él, lo recuperado contaría como pérdida entera.
            -- Sin lotes vuelve tal cual a su stock, sin rebaja: no hay lote
            -- rebajado donde ponerlo.
            CASE WHEN v_rec > 0 AND r.con_lotes
                 THEN ROUND(fn_mer_costo_ref(p.id) * (1 - fn_mer_rebaja(v_calidad)))
                 WHEN v_rec > 0
                 THEN fn_mer_costo_ref(p.id)
            END,
            v_firma, v_grupo, p_producto_id, p_cantidad
        FROM productos p WHERE p.id = r.componente_id
        RETURNING id INTO v_merma_id;

        -- El lote de recuperación, con vida útil recortada. Solo si el
        -- componente se maneja por lotes: si no, un lote suyo quedaría
        -- fantasma, porque al venderlo se descuenta del stock y no del lote.
        IF v_rec > 0 AND r.con_lotes THEN
            WITH nuevo AS (
                INSERT INTO lotes (
                    codigo, producto_id, fecha_ingreso, fecha_vencimiento,
                    varas_iniciales, varas_disponibles, costo_por_vara,
                    estado, precio_unitario, calidad, notas
                )
                SELECT
                    fn_abs_codigo_lote(), r.componente_id,
                    CURRENT_DATE,
                    CURRENT_DATE + fn_mer_dias_vida_recuperado(v_calidad),
                    v_rec, v_rec,
                    ROUND(fn_mer_costo_ref(p.id) * (1 - fn_mer_rebaja(v_calidad))),
                    'activo',
                    ROUND(p.precio * (1 - fn_mer_rebaja(v_calidad))),
                    v_calidad,
                    'Recuperado del desarme de ' || v_prod.nombre
                FROM productos p WHERE p.id = r.componente_id
                RETURNING id
            )
            UPDATE mermas SET lote_recuperacion_id = (SELECT id FROM nuevo)
             WHERE id = v_merma_id;
        END IF;

        IF v_rec > 0 THEN
            UPDATE productos
               SET stock = COALESCE(stock, 0) + v_rec, actualizado_en = now()
             WHERE id = r.componente_id;
        END IF;

        -- Los componentes ya salieron del historial al armar el ramo: lo
        -- que se pierde ahora no vuelve a restar (antes se restaba dos
        -- veces). Lo único que mueve su stock es lo que se recupera, que
        -- entra. Es el espejo exacto de la reversa (sp_mer_u_revertir).
        IF v_rec > 0 THEN
            INSERT INTO movimientos_inventario (
                producto_id, tipo, cantidad, motivo, detalle,
                usuario_id, referencia_tipo, referencia_id
            )
            VALUES (
                r.componente_id, 'entrada', v_rec,
                'Recuperado del desarme de ' || v_prod.nombre,
                v_rec || ' recuperadas, ' || v_perd || ' perdidas',
                p_usuario_id, 'desarme', v_merma_id
            );
        END IF;

        v_mermas   := v_mermas + 1;
        v_tot_rec  := v_tot_rec + v_rec;
        v_tot_perd := v_tot_perd + v_perd;
    END LOOP;

    INSERT INTO movimientos_inventario (
        producto_id, tipo, cantidad, motivo, detalle,
        usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, 'merma', -p_cantidad,
        'Desarme · ' || btrim(p_motivo),
        v_tot_rec || ' varas recuperadas de ' || (v_tot_rec + v_tot_perd),
        p_usuario_id, 'desarme', p_producto_id
    );

    RETURN QUERY
    SELECT p_producto_id, v_prod.nombre, p_cantidad, v_mermas, v_tot_rec, v_tot_perd;
END;
$_$;


--
-- Name: sp_mer_i_descarte_lote(integer, text, text, boolean, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_descarte_lote(p_lote_id integer, p_motivo text, p_detalle text DEFAULT NULL::text, p_es_devolucion boolean DEFAULT false, p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_lote record;
BEGIN
    SELECT * INTO v_lote FROM lotes WHERE id = p_lote_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El lote no existe.';
    END IF;

    IF v_lote.varas_disponibles = 0 THEN
        RAISE EXCEPTION 'El lote % ya está agotado: no queda nada que descartar.', v_lote.codigo;
    END IF;

    RETURN sp_mer_i_merma(
        v_lote.producto_id, p_lote_id, NULL, v_lote.varas_disponibles,
        p_motivo, p_detalle,
        CASE WHEN p_es_devolucion THEN 'devolucion_proveedor'::destino_merma
             ELSE 'perdida'::destino_merma END,
        0, NULL,
        v_lote.codigo, false, p_autorizado_por, p_usuario_id
    );
END;
$$;


--
-- Name: sp_mer_i_merma(integer, integer, integer, integer, text, text, public.destino_merma, integer, public.calidad_reingreso, text, boolean, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_merma(p_producto_id integer, p_lote_id integer, p_partida_id integer, p_cantidad integer, p_motivo text, p_detalle text DEFAULT NULL::text, p_destino public.destino_merma DEFAULT 'perdida'::public.destino_merma, p_cantidad_recuperada integer DEFAULT 0, p_calidad public.calidad_reingreso DEFAULT NULL::public.calidad_reingreso, p_codigo_escaneado text DEFAULT NULL::text, p_escaneado boolean DEFAULT false, p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_prod        record;
    v_lote        record;
    v_part        record;
    v_id          int;
    v_costo_u     int;
    v_recup       int := GREATEST(COALESCE(p_cantidad_recuperada, 0), 0);
    v_costo_rec_u int := 0;
    v_lote_rec    int;
    v_codigo      text;
    v_precio_rec  int;
    v_stock       int;
    v_umbral      int;
    v_valor       int;
    v_precio_ref  int;
    v_valor_aut   int;
    v_origen      text;
    -- Lo que el lote de recuperación hereda del origen. Van en variables y
    -- no leídos de v_lote / v_part: el registro que no se cargó (el de la
    -- otra rama) hace caer la función entera al tocarlo.
    v_proveedor   int;
    v_mot         motivos_merma;
    v_orig_lote   int;
    v_orig_codigo text;
BEGIN
    IF coalesce(p_cantidad, 0) < 1 THEN
        RAISE EXCEPTION 'La cantidad debe ser al menos 1.';
    END IF;

    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica el motivo de la merma.';
    END IF;

    -- Del catálogo, y con detalle si el motivo lo pide.
    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    IF p_lote_id IS NOT NULL AND p_partida_id IS NOT NULL THEN
        RAISE EXCEPTION 'La merma sale de un lote o de una partida, no de los dos.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    -- ═══ De dónde salen las varas ═══

    IF p_partida_id IS NOT NULL THEN
        -- ─── Del mostrador ───
        v_origen := 'partida';

        SELECT * INTO v_part
          FROM partidas_mostrador WHERE id = p_partida_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'La partida no existe.';
        END IF;

        IF v_part.producto_id <> p_producto_id THEN
            RAISE EXCEPTION 'Esa partida es de otro producto.';
        END IF;

        IF v_part.cantidad_disponible < p_cantidad THEN
            RAISE EXCEPTION 'La partida % tiene % y estás mermando %.',
                v_part.codigo, v_part.cantidad_disponible, p_cantidad;
        END IF;

        v_costo_u := ROUND(COALESCE(v_part.costo_por_vara, 0));
        v_precio_ref := v_part.precio_unitario;
        v_orig_lote   := v_part.lote_id;
        v_orig_codigo := v_part.codigo;
        SELECT proveedor_id INTO v_proveedor FROM lotes WHERE id = v_part.lote_id;

        UPDATE partidas_mostrador
           SET cantidad_disponible = cantidad_disponible - p_cantidad,
               agotada_en = CASE WHEN cantidad_disponible - p_cantidad = 0
                                 THEN now() ELSE agotada_en END
         WHERE id = p_partida_id;

    ELSIF p_lote_id IS NOT NULL THEN
        -- ─── De bodega ───
        v_origen := 'lote';

        SELECT * INTO v_lote FROM lotes WHERE id = p_lote_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'El lote no existe.';
        END IF;

        IF v_lote.producto_id <> p_producto_id THEN
            RAISE EXCEPTION 'Ese lote es de otro producto.';
        END IF;

        IF v_lote.varas_disponibles < p_cantidad THEN
            RAISE EXCEPTION 'El lote % tiene % varas y estás mermando %.',
                v_lote.codigo, v_lote.varas_disponibles, p_cantidad;
        END IF;

        v_costo_u := ROUND(v_lote.costo_por_vara);
        v_precio_ref := COALESCE(v_lote.precio_unitario, v_prod.precio);
        v_orig_lote   := p_lote_id;
        v_orig_codigo := v_lote.codigo;
        v_proveedor   := v_lote.proveedor_id;

        UPDATE lotes
           SET varas_disponibles = varas_disponibles - p_cantidad,
               estado = CASE WHEN varas_disponibles - p_cantidad = 0
                             THEN 'agotado'::estado_lote ELSE estado END,
               actualizado_en = now()
         WHERE id = p_lote_id;

    ELSE
        -- ─── Sin origen: lo que no controla lotes, o armados ───
        v_origen := 'stock';
        v_costo_u := ROUND(COALESCE(v_prod.costo, v_prod.costo_armado, 0));
        v_precio_ref := v_prod.precio;

        IF v_prod.tipo = 'armado' THEN
            IF COALESCE(v_prod.stock_listo, 0) < p_cantidad THEN
                RAISE EXCEPTION 'Solo hay % de % armados.',
                    COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
            END IF;
        ELSIF COALESCE(v_prod.stock, 0) < p_cantidad THEN
            RAISE EXCEPTION 'Solo hay % de % en bodega.',
                COALESCE(v_prod.stock, 0), v_prod.nombre;
        END IF;

        -- Un producto que controla lotes SIN indicar de cuál es la puerta
        -- abierta: nadie sabría de qué balde salieron esas varas, y el
        -- stock del producto dejaría de cuadrar con la suma de sus lotes.
        IF v_prod.controla_lotes AND v_prod.tipo <> 'armado' THEN
            RAISE EXCEPTION
                '% se maneja por lotes: escanea el balde o la partida de donde sale.',
                v_prod.nombre;
        END IF;
    END IF;

    -- ═══ Autorización ═══
    --
    -- Sobre el umbral hace falta la clave de una administradora. Es el
    -- mismo mecanismo del descuento en la venta: quien se lleva flor de a
    -- poco no puede escalar sin un cómplice.
    --
    -- Quién puede autorizar y si la clave es correcta lo verifica la API,
    -- que es donde vive BCrypt; acá solo se exige que venga firmado.
    v_umbral := fn_mer_umbral_autorizacion();
    v_valor  := p_cantidad * v_costo_u;

    -- Sin costo cargado, la merma valdría $0 y nunca pediría firma: es
    -- justo el producto del que nadie sabe cuánto costó. Para decidir la
    -- firma se usa entonces el precio de venta; el costo registrado sigue
    -- siendo el real (cero), para no inflar los reportes.
    v_valor_aut := p_cantidad * CASE WHEN v_costo_u > 0 THEN v_costo_u
                                     ELSE COALESCE(v_precio_ref, 0) END;

    IF v_valor_aut > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION
            'Una merma de $% necesita autorización: el tope sin firma es $%.%',
            v_valor_aut, v_umbral,
            CASE WHEN v_costo_u = 0 THEN ' (valorizada a precio de venta: el producto no tiene costo)' ELSE '' END;
    END IF;

    -- ═══ Validar el reingreso ═══
    -- Recuperar crea un lote nuevo con precio rebajado. Un producto que no
    -- se maneja por lotes no tiene dónde ponerlo: el lote quedaba fantasma
    -- (nunca se descuenta al vender y el inventario lo contaba dos veces), y
    -- en un armado las unidades desaparecían. Ahí lo que se salva se deja
    -- donde está y se registra solo lo perdido.
    IF p_destino = 'reingreso' AND (NOT v_prod.controla_lotes OR v_prod.tipo = 'armado') THEN
        RAISE EXCEPTION
            '% no se maneja por lotes: lo que se salva queda donde está. Registra como merma solo lo que se pierde.',
            v_prod.nombre;
    END IF;

    IF p_destino = 'reingreso' THEN
        IF v_recup < 1 THEN
            RAISE EXCEPTION 'Indica cuántas varas se recuperan.';
        END IF;

        IF v_recup > p_cantidad THEN
            RAISE EXCEPTION 'No se pueden recuperar % de % varas.', v_recup, p_cantidad;
        END IF;

        IF p_calidad IS NULL THEN
            RAISE EXCEPTION 'Indica en qué estado vuelve la flor.';
        END IF;
    ELSE
        v_recup := 0;
    END IF;


    -- ═══ El costo de lo recuperado ═══
    -- costo_perdido lo calcula la tabla (columna generada): cero en una
    -- devolución al proveedor, y en un reingreso solo la rebaja de lo que
    -- vuelve más lo que se perdió entero.
    IF p_destino = 'reingreso' THEN
        v_costo_rec_u := ROUND(v_costo_u * (1 - fn_mer_rebaja(p_calidad)));
    END IF;

    -- ═══ El lote de recuperación ═══
    IF v_recup > 0 THEN
        v_codigo := fn_abs_codigo_lote();
        v_precio_rec := ROUND(v_prod.precio * (1 - fn_mer_rebaja(p_calidad)));

        INSERT INTO lotes (
            codigo, producto_id, proveedor_id,
            fecha_ingreso, fecha_vencimiento,
            varas_iniciales, varas_disponibles, costo_por_vara,
            estado, precio_unitario, origen_lote_id, calidad, notas
        )
        VALUES (
            v_codigo, p_producto_id,
            v_proveedor,
            CURRENT_DATE,
            CURRENT_DATE + fn_mer_dias_vida_recuperado(p_calidad),
            v_recup, v_recup, v_costo_rec_u,
            'activo', v_precio_rec,
            v_orig_lote, p_calidad,
            'Recuperado de ' || COALESCE(v_orig_codigo, 'stock') ||
            ' · ' || btrim(p_motivo)
        )
        RETURNING id INTO v_lote_rec;
    END IF;

    -- ═══ El stock del producto ═══
    --
    -- Solo se descuenta si salió de bodega o del stock directo. Una merma
    -- del mostrador ya descontó de la partida, y el stock del producto no
    -- incluye lo que está adelante.
    IF v_prod.tipo = 'armado' THEN
        UPDATE productos
           SET stock_listo = stock_listo - p_cantidad, actualizado_en = now()
         WHERE id = p_producto_id;

    ELSIF v_origen = 'partida' THEN
        -- Solo entra lo que se recupera: vuelve a bodega.
        IF v_recup > 0 THEN
            UPDATE productos
               SET stock = COALESCE(stock, 0) + v_recup, actualizado_en = now()
             WHERE id = p_producto_id
            RETURNING stock INTO v_stock;
        END IF;

    ELSE
        UPDATE productos
           SET stock = COALESCE(stock, 0) - p_cantidad + v_recup,
               actualizado_en = now()
         WHERE id = p_producto_id
        RETURNING stock INTO v_stock;
    END IF;

    -- ═══ El registro ═══
    INSERT INTO mermas (
        producto_id, lote_id, partida_id, cantidad, motivo, motivo_id, detalle,
        costo_unitario, costo_total, usuario_id,
        destino, cantidad_recuperada, calidad_reingreso,
        lote_recuperacion_id, costo_recuperado_unitario,
        codigo_escaneado, escaneado, autorizado_por, valor_autorizacion
    )
    VALUES (
        p_producto_id, p_lote_id, p_partida_id, p_cantidad,
        v_mot.nombre, v_mot.id, nullif(btrim(coalesce(p_detalle, '')), ''),
        v_costo_u, v_valor, p_usuario_id,
        p_destino, v_recup, p_calidad,
        v_lote_rec, CASE WHEN v_recup > 0 THEN v_costo_rec_u END,
        nullif(btrim(coalesce(p_codigo_escaneado, '')), ''),
        -- "Escaneado" lo dice el teléfono (lector vs. tipeo), y eso no se
        -- puede comprobar acá. Lo que sí: que el código leído sea el del
        -- balde o la partida de donde sale. Una merma a mano, sin balde, no
        -- puede declararse escaneada.
        COALESCE(p_escaneado, false)
            AND v_orig_codigo IS NOT NULL
            AND upper(btrim(coalesce(p_codigo_escaneado, ''))) = upper(v_orig_codigo),
        nullif(btrim(coalesce(p_autorizado_por, '')), ''),
        v_valor_aut
    )
    RETURNING id INTO v_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, p_lote_id, 'merma', -p_cantidad, v_stock - v_recup,
        v_mot.nombre,
        CASE WHEN v_recup > 0
             THEN v_recup || ' recuperadas en ' || v_codigo
             ELSE COALESCE(p_detalle, v_origen) END,
        p_usuario_id, 'merma', v_id
    );

    -- Lo recuperado vuelve a entrar, en su lote nuevo. Sin esta fila el
    -- historial decía -cantidad mientras el stock bajaba solo lo perdido.
    IF v_recup > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, lote_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            p_producto_id, v_lote_rec, 'entrada', v_recup, v_stock,
            'Recuperado de merma', v_mot.nombre || ' · ' || v_codigo,
            p_usuario_id, 'merma', v_id
        );
    END IF;

    RETURN v_id;
END;
$_$;


--
-- Name: sp_mer_i_motivo(text, text, boolean, public.destino_merma); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_motivo(p_nombre text, p_categoria text, p_requiere_detalle boolean DEFAULT false, p_destino_sugerido public.destino_merma DEFAULT NULL::public.destino_merma) RETURNS integer
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: sp_mer_i_reporte(text, jsonb, text, text, bytea[], text[], boolean, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_reporte(p_tipo text, p_lineas jsonb, p_motivo text, p_detalle text, p_fotos bytea[], p_tipos_foto text[], p_solo_vitrina boolean DEFAULT false, p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(reporte_id integer, mermas integer, valor integer, foto_repetida boolean)
    LANGUAGE plpgsql
    AS $_$
DECLARE
    it        jsonb;
    v_n       int := 0;
    v_rep     int;
    v_mot     motivos_merma;
    v_merma   int;
    v_prod    record;
    v_fotos   int := COALESCE(array_length(p_fotos, 1), 0);
    v_valor   int;
    v_umbral  int;
    v_firma   text := nullif(btrim(coalesce(p_autorizado_por, '')), '');
    v_repe    boolean := false;
    v_sha     text;
BEGIN
    IF p_tipo NOT IN ('puntual', 'incidente') THEN
        RAISE EXCEPTION 'Tipo de reporte desconocido: %.', p_tipo;
    END IF;

    IF p_lineas IS NULL OR jsonb_array_length(p_lineas) = 0 THEN
        RAISE EXCEPTION 'Indica qué se dañó.';
    END IF;

    IF p_tipo = 'puntual' AND jsonb_array_length(p_lineas) > 1 THEN
        RAISE EXCEPTION 'Una merma puntual es de una sola cosa. Para varias, registra un incidente.';
    END IF;

    -- La foto es la evidencia: sin ella, "se marchitó" no se puede revisar.
    IF v_fotos < 1 THEN
        RAISE EXCEPTION 'Agrega al menos una foto de lo que se dañó.';
    END IF;

    IF v_fotos > 3 THEN
        RAISE EXCEPTION 'Son máximo 3 fotos por reporte.';
    END IF;

    v_mot := fn_mer_motivo(p_motivo, p_detalle);

    INSERT INTO merma_reportes (tipo, motivo_id, detalle, usuario_id, autorizado_por)
    VALUES (p_tipo, v_mot.id, nullif(btrim(coalesce(p_detalle, '')), ''), p_usuario_id, v_firma)
    RETURNING id INTO v_rep;

    -- ─── Las líneas ───
    FOR it IN SELECT * FROM jsonb_array_elements(p_lineas)
    LOOP
        v_n := v_n + 1;

        IF p_solo_vitrina THEN
            SELECT tipo, nombre INTO v_prod FROM productos WHERE id = (it->>'productoId')::int;

            IF (it->>'partidaId') IS NULL AND v_prod.tipo IS DISTINCT FROM 'armado' THEN
                RAISE EXCEPTION
                    'Línea %: desde la vitrina se registra lo del mostrador (partidas PAR-…) o un ramo armado. Lo de bodega lo registra bodega.',
                    v_n;
            END IF;
        END IF;

        -- Cada línea pasa por la misma función que una merma suelta: mismo
        -- costo, mismas reglas de lote, partida, recuperación y firma.
        v_merma := sp_mer_i_merma(
            (it->>'productoId')::int,
            (it->>'loteId')::int,
            (it->>'partidaId')::int,
            (it->>'cantidad')::int,
            v_mot.nombre, p_detalle,
            COALESCE(nullif(it->>'destino', ''), 'perdida')::destino_merma,
            COALESCE((it->>'cantidadRecuperada')::int, 0),
            nullif(it->>'calidad', '')::calidad_reingreso,
            it->>'codigoEscaneado',
            COALESCE((it->>'escaneado')::boolean, false),
            v_firma, p_usuario_id);

        UPDATE mermas SET reporte_id = v_rep WHERE id = v_merma;
    END LOOP;

    -- ─── Una firma por el total ───
    -- Cada línea ya exigió firma si sola pasaba el tope; acá se mira la
    -- suma, que es lo que un incidente puede escalar de a poco.
    SELECT COALESCE(sum(m.valor_autorizacion), 0) INTO v_valor
      FROM mermas m WHERE m.reporte_id = v_rep;

    v_umbral := fn_mer_umbral_autorizacion();

    IF v_valor > v_umbral AND v_firma IS NULL THEN
        RAISE EXCEPTION
            'Este reporte suma $% y necesita autorización: el tope sin firma es $%.',
            v_valor, v_umbral;
    END IF;

    -- ─── Las fotos ───
    FOR i IN 1 .. v_fotos LOOP
        v_sha := encode(sha256(p_fotos[i]), 'hex');

        IF EXISTS (SELECT 1 FROM merma_evidencias e WHERE e.sha256 = v_sha) THEN
            v_repe := true;
        END IF;

        INSERT INTO merma_evidencias (reporte_id, foto, tipo_mime, sha256)
        VALUES (v_rep, p_fotos[i], COALESCE(p_tipos_foto[i], 'image/jpeg'), v_sha);
    END LOOP;

    RETURN QUERY SELECT v_rep, v_n, v_valor, v_repe;
END;
$_$;


--
-- Name: sp_mer_i_reporte_desarme(integer, integer, text, text, jsonb, bytea[], text[], text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_i_reporte_desarme(p_producto_id integer, p_cantidad integer, p_motivo text, p_detalle text, p_lineas jsonb, p_fotos bytea[], p_tipos_foto text[], p_autorizado_por text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer) RETURNS TABLE(reporte_id integer, mermas integer, recuperadas integer, perdidas integer, foto_repetida boolean)
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


--
-- Name: sp_mer_u_deshacer_reporte(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_u_deshacer_reporte(p_reporte_id integer, p_usuario_id integer) RETURNS integer
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


--
-- Name: sp_mer_u_motivo(integer, text, text, boolean, public.destino_merma, boolean, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_u_motivo(p_id integer, p_nombre text, p_categoria text, p_requiere_detalle boolean, p_destino_sugerido public.destino_merma, p_activo boolean, p_orden integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
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
$$;


--
-- Name: sp_mer_u_reporte_revisado(integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_u_reporte_revisado(p_reporte_id integer, p_usuario_id integer, p_nota text DEFAULT NULL::text) RETURNS integer
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


--
-- Name: sp_mer_u_revertir(integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_mer_u_revertir(p_id integer, p_motivo text, p_usuario_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_m      record;
    v_rec    record;
    v_prod   record;
    v_x      record;
    v_stock  int;
    v_motivo text := btrim(coalesce(p_motivo, ''));
BEGIN
    IF v_motivo = '' THEN
        RAISE EXCEPTION 'Explica por qué se revierte.';
    END IF;

    SELECT * INTO v_m FROM mermas WHERE id = p_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La merma no existe.';
    END IF;

    IF v_m.revertida THEN
        RAISE EXCEPTION 'Esta merma ya fue revertida.';
    END IF;

    -- ═════════════════════════════════════════════════════════════
    -- Un desarme se revierte ENTERO: las mermas de sus componentes no
    -- existen por separado. Se anulan los lotes recuperados, sale del stock
    -- de cada componente lo que había vuelto, y el armado regresa listo.
    -- ═════════════════════════════════════════════════════════════
    IF v_m.desarme_grupo IS NOT NULL THEN
        FOR v_x IN
            SELECT me.*, l.codigo AS rec_codigo,
                   l.varas_iniciales AS rec_ini, l.varas_disponibles AS rec_disp
              FROM mermas me
              LEFT JOIN lotes l ON l.id = me.lote_recuperacion_id
             WHERE me.desarme_grupo = v_m.desarme_grupo
             ORDER BY me.id
               FOR UPDATE OF me
        LOOP
            IF v_x.revertida THEN
                CONTINUE;
            END IF;

            IF v_x.lote_recuperacion_id IS NOT NULL THEN
                IF v_x.rec_disp < v_x.rec_ini THEN
                    RAISE EXCEPTION
                        'No se puede revertir el desarme: ya se vendieron % varas del lote recuperado %.',
                        v_x.rec_ini - v_x.rec_disp, v_x.rec_codigo;
                END IF;

                UPDATE lotes
                   SET estado = 'descartado',
                       varas_disponibles = 0,
                       notas = COALESCE(notas || ' · ', '') || 'Anulado al revertir el desarme de la merma ' || p_id,
                       actualizado_en = now()
                 WHERE id = v_x.lote_recuperacion_id;
            END IF;

            -- Lo que había vuelto a bodega sale otra vez: esas varas regresan
            -- al armado. Lo perdido no se toca, nunca había entrado.
            IF v_x.cantidad_recuperada > 0 THEN
                UPDATE productos
                   SET stock = COALESCE(stock, 0) - v_x.cantidad_recuperada,
                       actualizado_en = now()
                 WHERE id = v_x.producto_id
                RETURNING stock INTO v_stock;

                INSERT INTO movimientos_inventario (
                    producto_id, tipo, cantidad, stock_resultante,
                    motivo, detalle, usuario_id, referencia_tipo, referencia_id
                )
                VALUES (
                    v_x.producto_id, 'ajuste', -v_x.cantidad_recuperada, v_stock,
                    'Reversa de desarme', v_motivo,
                    p_usuario_id, 'merma_reversa', v_x.id
                );
            END IF;

            UPDATE mermas SET
                revertida        = true,
                revertida_por    = p_usuario_id,
                revertida_en     = now(),
                motivo_reversion = v_motivo
            WHERE id = v_x.id;
        END LOOP;

        UPDATE productos
           SET stock_listo = COALESCE(stock_listo, 0) + v_m.desarme_cantidad,
               actualizado_en = now()
         WHERE id = v_m.desarme_producto_id
        RETURNING stock_listo INTO v_stock;

        INSERT INTO movimientos_inventario (
            producto_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            v_m.desarme_producto_id, 'ajuste', v_m.desarme_cantidad, v_stock,
            'Reversa de desarme', v_motivo,
            p_usuario_id, 'merma_reversa', p_id
        );

        RETURN p_id;
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = v_m.producto_id FOR UPDATE;

    -- Un lote dado de baja ya no existe para el inventario: devolverle varas
    -- las dejaría en un lote muerto mientras el stock del producto sube, y
    -- nadie podría venderlas ni mermarlas. Si de verdad quedaron flores, se
    -- ingresan como entrada nueva.
    IF v_m.lote_id IS NOT NULL
       AND EXISTS (SELECT 1 FROM lotes WHERE id = v_m.lote_id AND estado = 'descartado') THEN
        RAISE EXCEPTION
            'No se puede revertir: el lote % ya fue dado de baja. Si quedaron flores, ingrésalas como entrada.',
            (SELECT codigo FROM lotes WHERE id = v_m.lote_id);
    END IF;

    -- ═══ El lote de recuperación, si lo hubo ═══
    IF v_m.lote_recuperacion_id IS NOT NULL THEN
        SELECT * INTO v_rec FROM lotes WHERE id = v_m.lote_recuperacion_id FOR UPDATE;

        IF v_rec.varas_disponibles < v_rec.varas_iniciales THEN
            RAISE EXCEPTION
                'No se puede revertir: ya se vendieron % varas del lote recuperado %.',
                v_rec.varas_iniciales - v_rec.varas_disponibles, v_rec.codigo;
        END IF;

        -- Se descarta en vez de borrarse: el código pudo imprimirse en una
        -- etiqueta y quedar pegado a un balde. Un código que desaparece y
        -- después se reutiliza es peor que uno muerto.
        UPDATE lotes
           SET estado = 'descartado',
               varas_disponibles = 0,
               notas = COALESCE(notas || ' · ', '') || 'Anulado al revertir la merma ' || p_id,
               actualizado_en = now()
         WHERE id = v_m.lote_recuperacion_id;
    END IF;

    -- ═══ Devolver al origen ═══
    IF v_m.partida_id IS NOT NULL THEN
        -- Del mostrador: las varas vuelven a la partida.
        UPDATE partidas_mostrador
           SET cantidad_disponible = cantidad_disponible + v_m.cantidad,
               agotada_en = NULL
         WHERE id = v_m.partida_id;

    ELSIF v_m.lote_id IS NOT NULL THEN
        UPDATE lotes
           SET varas_disponibles = varas_disponibles + v_m.cantidad,
               estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
               actualizado_en = now()
         WHERE id = v_m.lote_id;
    END IF;

    -- ═══ El stock del producto, el espejo exacto del registro ═══
    IF v_prod.tipo = 'armado' THEN
        UPDATE productos
           SET stock_listo = COALESCE(stock_listo, 0) + v_m.cantidad,
               actualizado_en = now()
         WHERE id = v_m.producto_id
        RETURNING stock_listo INTO v_stock;

    ELSIF v_m.partida_id IS NOT NULL THEN
        -- Una merma del mostrador no descontó el stock de bodega: solo le
        -- sumó lo recuperado. Revertirla es sacar eso, nada más.
        IF v_m.cantidad_recuperada > 0 THEN
            UPDATE productos
               SET stock = COALESCE(stock, 0) - v_m.cantidad_recuperada,
                   actualizado_en = now()
             WHERE id = v_m.producto_id
            RETURNING stock INTO v_stock;
        END IF;

    ELSE
        -- Entra lo mermado, sale lo que se había recuperado.
        UPDATE productos
           SET stock = COALESCE(stock, 0) + v_m.cantidad - v_m.cantidad_recuperada,
               actualizado_en = now()
         WHERE id = v_m.producto_id
        RETURNING stock INTO v_stock;
    END IF;

    UPDATE mermas SET
        revertida        = true,
        revertida_por    = p_usuario_id,
        revertida_en     = now(),
        motivo_reversion = v_motivo
    WHERE id = p_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        v_m.producto_id, v_m.lote_id, 'ajuste', v_m.cantidad,
        v_stock + v_m.cantidad_recuperada,
        'Reversa de merma', v_motivo,
        p_usuario_id, 'merma_reversa', p_id
    );

    -- El lote recuperado se anuló: esas varas salen. Es el espejo de la
    -- entrada que dejó el registro.
    IF v_m.cantidad_recuperada > 0 THEN
        INSERT INTO movimientos_inventario (
            producto_id, lote_id, tipo, cantidad, stock_resultante,
            motivo, detalle, usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            v_m.producto_id, v_m.lote_recuperacion_id, 'ajuste', -v_m.cantidad_recuperada, v_stock,
            'Reversa de merma', 'Anula lo recuperado · ' || v_motivo,
            p_usuario_id, 'merma_reversa', p_id
        );
    END IF;

    RETURN p_id;
END;
$$;


--
-- Name: sp_pub_c_catalogo(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_pub_c_catalogo() RETURNS TABLE(id integer, nombre text, emoji text, categoria text, precio integer, version bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT p.id, p.nombre, p.emoji, c.nombre, p.precio, i.version
    FROM productos p
    JOIN producto_imagenes i ON i.producto_id = p.id
    LEFT JOIN categorias c ON c.id = p.categoria_id
    WHERE p.activo
    ORDER BY c.nombre NULLS LAST, p.nombre;
$$;


--
-- Name: sp_rep_c_alertas(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_alertas() RETURNS TABLE(tipo text, urgencia text, mensaje text, cantidad bigint, monto bigint)
    LANGUAGE sql STABLE
    AS $_$
    WITH todas AS (

        -- ─── Vencido: es pérdida ya ocurrida, solo falta registrarla ───
        SELECT
            'lote_vencido'::text AS a_tipo,
            'alta'::text         AS a_urgencia,
            CASE WHEN count(*) = 1
                 THEN 'Hay un lote vencido de ' || max(p.nombre) ||
                      ' por ' || to_char(sum(l.varas_disponibles * l.costo_por_vara), 'FM$999G999')
                 ELSE count(*) || ' lotes vencidos por ' ||
                      to_char(sum(l.varas_disponibles * l.costo_por_vara), 'FM$999G999')
            END                  AS a_mensaje,
            count(*)             AS a_cantidad,
            ROUND(sum(l.varas_disponibles * l.costo_por_vara))::bigint AS a_monto
        FROM lotes l
        JOIN productos p ON p.id = l.producto_id
        WHERE l.estado = 'activo'
          AND l.varas_disponibles > 0
          AND l.fecha_vencimiento < CURRENT_DATE
        HAVING count(*) > 0

        UNION ALL

        -- ─── Por vencer: todavía se puede vender o liquidar ───
        SELECT
            'lote_por_vencer', 'media',
            count(*) || ' lote(s) vencen en 2 días · ' ||
            to_char(sum(l.varas_disponibles * l.costo_por_vara), 'FM$999G999') || ' en juego',
            count(*),
            ROUND(sum(l.varas_disponibles * l.costo_por_vara))::bigint
        FROM lotes l
        WHERE l.estado = 'activo'
          AND l.varas_disponibles > 0
          AND l.fecha_vencimiento BETWEEN CURRENT_DATE AND CURRENT_DATE + 2
        HAVING count(*) > 0

        UNION ALL

        -- ─── Rezagado: quedó atrás cuando se abrió uno más nuevo ───
        --
        -- No está vencido, pero nadie lo está vendiendo. Si no se saca
        -- adelante, termina en merma.
        SELECT
            'lote_rezagado', 'media',
            count(*) || ' lote(s) quedaron atrás: hay uno más nuevo abierto del mismo producto',
            count(*),
            ROUND(sum(l.varas_disponibles * l.costo_por_vara))::bigint
        FROM lotes l
        WHERE l.estado = 'activo'
          AND l.varas_disponibles > 0
          AND EXISTS (
              SELECT 1 FROM lotes l2
              WHERE l2.producto_id = l.producto_id
                AND l2.estado = 'activo'
                AND l2.varas_disponibles < l2.varas_iniciales
                AND l2.fecha_ingreso > l.fecha_ingreso
          )
        HAVING count(*) > 0

        UNION ALL

        -- ─── Bajo mínimo: hay que comprar ───
        SELECT
            'bajo_minimo', 'media',
            CASE WHEN count(*) = 1
                 THEN max(p.nombre) || ' está bajo el mínimo'
                 ELSE count(*) || ' productos bajo el mínimo'
            END,
            count(*), 0::bigint
        FROM productos p
        LEFT JOIN vw_mostrador m ON m.producto_id = p.id
        WHERE p.activo
          AND p.minimo > 0
          AND (COALESCE(p.stock, p.stock_listo, 0) + COALESCE(m.stock, 0)) <= p.minimo
        HAVING count(*) > 0

        UNION ALL

        -- ─── Caja abierta desde ayer ───
        --
        -- Arrastra las ventas de hoy al arqueo de ayer, y ese arqueo deja
        -- de significar algo.
        SELECT
            'caja_sin_cerrar', 'alta',
            'La caja lleva abierta desde el ' ||
            to_char(c.abierta_en, 'DD-MM') || ': ciérrala antes de seguir vendiendo',
            1::bigint, 0::bigint
        FROM cajas c
        WHERE c.estado = 'abierta'
          AND c.abierta_en::date < CURRENT_DATE

        UNION ALL

        -- ─── Merma alta: se compra más de lo que se vende ───
        SELECT
            'merma_alta', 'media',
            'La merma del mes va en ' ||
            ROUND(sum(me.costo_perdido)::numeric / NULLIF(v.vendido, 0) * 100, 1) ||
            '% de lo vendido',
            count(*),
            sum(me.costo_perdido)::bigint
        FROM mermas me
        CROSS JOIN LATERAL (
            SELECT COALESCE(sum(total), 0) AS vendido FROM ventas
            WHERE creado_en >= date_trunc('month', CURRENT_DATE) AND NOT anulada
        ) v
        WHERE NOT me.revertida
          AND me.creado_en >= date_trunc('month', CURRENT_DATE)
        GROUP BY v.vendido
        HAVING v.vendido > 0
           AND sum(me.costo_perdido)::numeric / v.vendido > 0.05

        UNION ALL

        -- ─── Cumpleaños: la razón por la que existe esa columna ───
        SELECT
            'cumpleanos', 'baja',
            CASE WHEN count(*) = 1
                 THEN max(c.nombre) || ' cumple años esta semana'
                 ELSE count(*) || ' clientes cumplen años esta semana'
            END,
            count(*), 0::bigint
        FROM clientes c
        WHERE c.activo
          AND c.cumple_mes IS NOT NULL
          AND c.cumple_dia IS NOT NULL
          AND make_date(extract(year FROM CURRENT_DATE)::int, c.cumple_mes, c.cumple_dia)
              BETWEEN CURRENT_DATE AND CURRENT_DATE + 7
        HAVING count(*) > 0
    )
    SELECT t.a_tipo, t.a_urgencia, t.a_mensaje, t.a_cantidad, t.a_monto
    FROM todas t
    ORDER BY
        CASE t.a_urgencia WHEN 'alta' THEN 1 WHEN 'media' THEN 2 ELSE 3 END,
        t.a_monto DESC;
$_$;


--
-- Name: sp_rep_c_categorias(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_categorias(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(categoria_id integer, categoria text, unidades bigint, ingresos bigint, costo bigint, utilidad bigint, margen numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH r AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE) AS h
    )
    SELECT
        p.categoria_id, COALESCE(c.nombre, 'Sin categoría'),
        sum(vi.cantidad),
        sum(vi.subtotal),
        COALESCE(sum(cc.costo), 0)::bigint,
        (sum(vi.subtotal) - COALESCE(sum(cc.costo), 0))::bigint,
        CASE WHEN sum(vi.subtotal) > 0
             THEN ROUND((sum(vi.subtotal) - COALESCE(sum(cc.costo), 0))::numeric
                        / sum(vi.subtotal) * 100, 1)
             ELSE 0 END
    FROM venta_items vi
    JOIN ventas v    ON v.id = vi.venta_id
    JOIN productos p ON p.id = vi.producto_id
    LEFT JOIN categorias c ON c.id = p.categoria_id
    LEFT JOIN LATERAL (
        SELECT sum(vc.cantidad * vc.costo_unitario) AS costo
        FROM venta_consumos vc
        WHERE vc.venta_id = vi.venta_id AND vc.producto_id = vi.producto_id
    ) cc ON true,
    r
    WHERE NOT v.anulada
      AND v.creado_en >= r.d AND v.creado_en < r.h + 1
    GROUP BY p.categoria_id, c.nombre
    ORDER BY 6 DESC;
$$;


--
-- Name: sp_rep_c_equipo(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_equipo(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(usuario_id integer, usuario text, rol public.rol_usuario, boletas bigint, vendido bigint, ticket_promedio bigint, unidades bigint, descuentos_dados bigint, anuladas bigint, turnos bigint, turnos_descuadrados bigint, diferencia_acumulada bigint, diferencia_absoluta bigint, mermas bigint, merma_costo bigint, mermas_sin_escanear bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH r AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE) AS h
    )
    SELECT
        u.id, u.nombre, u.rol,

        COALESCE(v.boletas, 0),
        COALESCE(v.vendido, 0),
        CASE WHEN COALESCE(v.boletas, 0) > 0
             THEN ROUND(v.vendido::numeric / v.boletas) ELSE 0 END::bigint,
        COALESCE(v.unidades, 0),
        COALESCE(v.descuentos, 0),
        COALESCE(v.anuladas, 0),

        COALESCE(c.turnos, 0),
        COALESCE(c.descuadrados, 0),
        COALESCE(c.dif, 0),
        COALESCE(c.dif_abs, 0),

        COALESCE(m.n, 0),
        COALESCE(m.costo, 0),
        COALESCE(m.sin_escanear, 0)

    FROM usuarios u, r
    LEFT JOIN LATERAL (
        SELECT
            count(*) FILTER (WHERE NOT ve.anulada) AS boletas,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada), 0) AS vendido,
            COALESCE(sum(ve.descuento_manual) FILTER (WHERE NOT ve.anulada), 0) AS descuentos,
            count(*) FILTER (WHERE ve.anulada) AS anuladas,
            (SELECT COALESCE(sum(vi.cantidad), 0)
             FROM venta_items vi JOIN ventas v2 ON v2.id = vi.venta_id
             WHERE v2.usuario_id = u.id AND NOT v2.anulada
               AND v2.creado_en >= r.d AND v2.creado_en < r.h + 1) AS unidades
        FROM ventas ve
        WHERE ve.usuario_id = u.id
          AND ve.creado_en >= r.d AND ve.creado_en < r.h + 1
    ) v ON true
    LEFT JOIN LATERAL (
        SELECT
            count(*) AS turnos,
            count(*) FILTER (WHERE ca.diferencia <> 0) AS descuadrados,
            COALESCE(sum(ca.diferencia), 0) AS dif,
            COALESCE(sum(abs(ca.diferencia)), 0) AS dif_abs
        FROM cajas ca
        WHERE ca.abierta_por = u.id AND ca.estado = 'cerrada'
          AND ca.abierta_en >= r.d AND ca.abierta_en < r.h + 1
    ) c ON true
    LEFT JOIN LATERAL (
        SELECT
            count(*) AS n,
            COALESCE(sum(me.costo_perdido), 0) AS costo,
            count(*) FILTER (WHERE NOT me.escaneado) AS sin_escanear
        FROM mermas me
        WHERE me.usuario_id = u.id AND NOT me.revertida
          AND me.creado_en >= r.d AND me.creado_en < r.h + 1
    ) m ON true
    WHERE u.activo
    ORDER BY COALESCE(v.vendido, 0) DESC;
$$;


--
-- Name: sp_rep_c_eventos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_eventos(p_dias integer DEFAULT 14) RETURNS TABLE(tipo text, fecha date, dias integer, titulo text, detalle text, monto bigint, referencia text)
    LANGUAGE sql STABLE
    AS $$
    WITH cumples AS (
        SELECT
            'cumpleanos'::text AS e_tipo,
            make_date(extract(year FROM CURRENT_DATE)::int, c.cumple_mes, c.cumple_dia) AS e_fecha,
            c.nombre           AS e_titulo,
            CASE WHEN c.compras > 0
                 THEN 'Ha comprado ' || c.compras || ' vez(ces)'
                 ELSE 'Sin compras todavía' END AS e_detalle,
            0::bigint          AS e_monto,
            c.telefono         AS e_referencia
        FROM vw_clientes c
        WHERE c.activo
          AND c.cumple_mes IS NOT NULL
          AND c.cumple_dia IS NOT NULL

        UNION ALL

        SELECT
            'vencimiento',
            l.fecha_vencimiento,
            p.nombre,
            l.varas_disponibles || ' varas en ' || l.codigo,
            ROUND(l.varas_disponibles * l.costo_por_vara)::bigint,
            l.codigo
        FROM lotes l
        JOIN productos p ON p.id = l.producto_id
        WHERE l.estado = 'activo'
          AND l.varas_disponibles > 0
          AND l.fecha_vencimiento IS NOT NULL
    )
    SELECT
        c.e_tipo, c.e_fecha, (c.e_fecha - CURRENT_DATE)::int,
        c.e_titulo, c.e_detalle, c.e_monto, c.e_referencia
    FROM cumples c
    WHERE c.e_fecha BETWEEN CURRENT_DATE AND CURRENT_DATE + GREATEST(p_dias, 1)
    ORDER BY c.e_fecha, c.e_monto DESC
    LIMIT 20;
$$;


--
-- Name: sp_rep_c_inventario(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_inventario() RETURNS TABLE(productos bigint, productos_activos bigint, bajo_minimo bigint, sin_stock bigint, lotes_activos bigint, varas_en_bodega bigint, varas_en_mostrador bigint, valor_bodega numeric, valor_mostrador numeric, valor_total numeric, valor_venta numeric, lotes_vencidos bigint, valor_vencido numeric, lotes_por_vencer bigint, valor_por_vencer numeric, dias_de_stock numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH lotes_act AS (
        SELECT
            count(*) AS n,
            COALESCE(sum(varas_disponibles), 0) AS varas,
            COALESCE(sum(varas_disponibles * costo_por_vara), 0) AS valor
        FROM lotes WHERE estado = 'activo' AND varas_disponibles > 0
    ),
    mostrador AS (
        SELECT
            COALESCE(sum(cantidad_disponible), 0) AS varas,
            COALESCE(sum(cantidad_disponible * COALESCE(costo_por_vara, 0)), 0) AS valor,
            COALESCE(sum(cantidad_disponible * precio_unitario), 0) AS venta
        FROM partidas_mostrador WHERE cantidad_disponible > 0
    ),
    prods AS (
        SELECT
            count(*) AS n,
            count(*) FILTER (WHERE activo) AS activos,
            count(*) FILTER (
                WHERE activo AND minimo > 0
                  AND COALESCE(stock, stock_listo, 0) <= minimo
            ) AS bajo,
            count(*) FILTER (WHERE activo AND COALESCE(stock, stock_listo, 0) = 0) AS sin
        FROM productos
    ),
    venta_bodega AS (
        SELECT COALESCE(sum(l.varas_disponibles * COALESCE(l.precio_unitario, p.precio)), 0) AS v
        FROM lotes l JOIN productos p ON p.id = l.producto_id
        WHERE l.estado = 'activo' AND l.varas_disponibles > 0
    ),
    vencidos AS (
        SELECT
            count(*) AS n,
            COALESCE(sum(varas_disponibles * costo_por_vara), 0) AS valor
        FROM lotes
        WHERE estado = 'activo' AND varas_disponibles > 0
          AND fecha_vencimiento < CURRENT_DATE
    ),
    por_vencer AS (
        SELECT
            count(*) AS n,
            COALESCE(sum(varas_disponibles * costo_por_vara), 0) AS valor
        FROM lotes
        WHERE estado = 'activo' AND varas_disponibles > 0
          AND fecha_vencimiento BETWEEN CURRENT_DATE AND CURRENT_DATE + 3
    ),
    ritmo AS (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) / 30.0 AS costo_diario
        FROM venta_consumos vc
        JOIN ventas v ON v.id = vc.venta_id
        WHERE NOT v.anulada AND v.creado_en >= CURRENT_DATE - 30
    )
    SELECT
        p.n, p.activos, p.bajo, p.sin,
        la.n, la.varas, m.varas,
        la.valor, m.valor, la.valor + m.valor,
        vb.v + m.venta,
        ve.n, ve.valor,
        pv.n, pv.valor,
        CASE WHEN ri.costo_diario > 0
             THEN ROUND((la.valor + m.valor) / ri.costo_diario, 1)
             ELSE NULL END
    FROM lotes_act la, mostrador m, prods p, venta_bodega vb,
         vencidos ve, por_vencer pv, ritmo ri;
$$;


--
-- Name: sp_rep_c_inventario_categorias(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_inventario_categorias() RETURNS TABLE(categoria_id integer, categoria text, productos bigint, varas bigint, valor numeric, parte numeric)
    LANGUAGE sql STABLE
    AS $$
    WITH por_cat AS (
        SELECT
            p.categoria_id,
            COALESCE(c.nombre, 'Sin categoría') AS categoria,
            count(DISTINCT p.id) AS productos,
            COALESCE(sum(l.varas_disponibles), 0) AS varas,
            COALESCE(sum(l.varas_disponibles * l.costo_por_vara), 0) AS valor
        FROM productos p
        LEFT JOIN categorias c ON c.id = p.categoria_id
        LEFT JOIN lotes l ON l.producto_id = p.id
                         AND l.estado = 'activo' AND l.varas_disponibles > 0
        GROUP BY p.categoria_id, c.nombre
    ),
    total AS (SELECT GREATEST(sum(valor), 1) AS v FROM por_cat)
    SELECT
        pc.categoria_id, pc.categoria, pc.productos, pc.varas, pc.valor,
        ROUND(pc.valor / t.v * 100, 1)
    FROM por_cat pc, total t
    WHERE pc.valor > 0
    ORDER BY pc.valor DESC;
$$;


--
-- Name: sp_rep_c_panel(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_panel() RETURNS TABLE(hoy_boletas bigint, hoy_vendido bigint, hoy_ticket_promedio bigint, hoy_unidades bigint, hoy_costo bigint, hoy_utilidad bigint, hoy_margen numeric, hoy_anuladas bigint, hoy_descuentos bigint, hoy_clientes_nuevos bigint, sem_boletas bigint, sem_vendido bigint, variacion numeric, caja_id integer, caja_abierta_por text, caja_abierta_en timestamp with time zone, caja_fondo integer, caja_efectivo integer, caja_en_cajon integer, caja_boletas bigint, mes_vendido bigint, mes_boletas bigint, inventario_valorizado numeric, clientes_activos bigint, puntos_por_pagar bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH hoy AS (
        SELECT
            count(*) AS boletas,
            COALESCE(sum(v.total), 0) AS vendido,
            COALESCE(sum(v.descuento_total), 0) AS descuentos
        FROM ventas v
        WHERE v.creado_en::date = CURRENT_DATE AND NOT v.anulada
    ),
    hoy_detalle AS (
        SELECT COALESCE(sum(vi.cantidad), 0) AS unidades
        FROM venta_items vi
        JOIN ventas v ON v.id = vi.venta_id
        WHERE v.creado_en::date = CURRENT_DATE AND NOT v.anulada
    ),
    -- El costo sale de los consumos: es lo que valían las varas que
    -- efectivamente salieron de la cámara, no el costo de ficha del
    -- producto.
    hoy_costo AS (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) AS costo
        FROM venta_consumos vc
        JOIN ventas v ON v.id = vc.venta_id
        WHERE v.creado_en::date = CURRENT_DATE AND NOT v.anulada
    ),
    hoy_anuladas AS (
        SELECT count(*) AS n FROM ventas
        WHERE creado_en::date = CURRENT_DATE AND anulada
    ),
    semana AS (
        SELECT
            count(*) AS boletas,
            COALESCE(sum(v.total), 0) AS vendido
        FROM ventas v
        WHERE v.creado_en::date = CURRENT_DATE - 7 AND NOT v.anulada
    ),
    caja AS (
        SELECT
            c.id, u.nombre AS quien, c.abierta_en, c.fondo_inicial,
            COALESCE(e.efectivo, 0)::int AS efectivo,
            COALESCE(e.boletas, 0) AS boletas
        FROM cajas c
        LEFT JOIN usuarios u ON u.id = c.abierta_por
        LEFT JOIN LATERAL (
            SELECT
                sum(v.total) FILTER (WHERE v.medio_pago = 'efectivo') AS efectivo,
                count(*) AS boletas
            FROM ventas v
            WHERE v.caja_id = c.id AND NOT v.anulada
        ) e ON true
        WHERE c.estado = 'abierta'
        LIMIT 1
    ),
    mes AS (
        SELECT
            count(*) AS boletas,
            COALESCE(sum(v.total), 0) AS vendido
        FROM ventas v
        WHERE v.creado_en >= date_trunc('month', CURRENT_DATE)
          AND NOT v.anulada
    ),
    inventario AS (
        -- Al costo, no al precio: es lo invertido, no lo que se ganaría.
        SELECT COALESCE(sum(l.varas_disponibles * l.costo_por_vara), 0) AS valor
        FROM lotes l WHERE l.estado = 'activo'
    ),
    cli AS (
        SELECT
            count(*) FILTER (WHERE activo) AS activos,
            COALESCE(sum(puntos), 0) AS puntos,
            count(*) FILTER (WHERE creado_en::date = CURRENT_DATE) AS nuevos
        FROM clientes
    )
    SELECT
        h.boletas,
        h.vendido,
        CASE WHEN h.boletas > 0 THEN ROUND(h.vendido::numeric / h.boletas) ELSE 0 END::bigint,
        hd.unidades,
        hc.costo::bigint,
        (h.vendido - hc.costo)::bigint,
        CASE WHEN h.vendido > 0
             THEN ROUND((h.vendido - hc.costo)::numeric / h.vendido * 100, 1)
             ELSE 0 END,
        ha.n,
        h.descuentos,
        cl.nuevos,

        s.boletas,
        s.vendido,

        CASE WHEN s.vendido > 0
             THEN ROUND((h.vendido - s.vendido)::numeric / s.vendido * 100, 1)
             -- Sin base con qué comparar, 0 en vez de null: un "+∞%" no le
             -- dice nada a nadie.
             ELSE 0 END,

        c.id,
        c.quien,
        c.abierta_en,
        c.fondo_inicial,
        c.efectivo,
        (COALESCE(c.fondo_inicial, 0) + COALESCE(c.efectivo, 0))::int,
        c.boletas,

        m.vendido,
        m.boletas,
        i.valor,
        cl.activos,
        -- Lo que el local debe si todos canjearan mañana, al valor de hoy.
        (cl.puntos * COALESCE(fn_ven_config('club', 'valorPunto', 10)::int, 10))::bigint

    FROM hoy h
    CROSS JOIN hoy_detalle hd
    CROSS JOIN hoy_costo hc
    CROSS JOIN hoy_anuladas ha
    CROSS JOIN semana s
    CROSS JOIN mes m
    CROSS JOIN inventario i
    CROSS JOIN cli cl
    -- LEFT: si no hay turno abierto, las columnas de caja quedan en null y
    -- el front muestra "abre la caja" en vez de ceros.
    LEFT JOIN caja c ON true;
$$;


--
-- Name: sp_rep_c_panel_usuario(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_panel_usuario(p_usuario_id integer) RETURNS TABLE(hoy_boletas bigint, hoy_vendido bigint, hoy_ticket_promedio bigint, hoy_unidades bigint, hoy_costo bigint, hoy_utilidad bigint, hoy_margen numeric, hoy_anuladas bigint, hoy_descuentos bigint, hoy_clientes_nuevos bigint, sem_boletas bigint, sem_vendido bigint, variacion numeric, caja_id integer, caja_abierta_por text, caja_abierta_en timestamp with time zone, caja_fondo integer, caja_efectivo integer, caja_en_cajon integer, caja_boletas bigint, mes_vendido bigint, mes_boletas bigint, inventario_valorizado numeric, clientes_activos bigint, puntos_por_pagar bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_rep_c_productos(date, date, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_productos(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_limite integer DEFAULT 20) RETURNS TABLE(producto_id integer, producto text, emoji text, categoria text, boletas bigint, unidades bigint, ingresos bigint, costo bigint, utilidad bigint, margen numeric, peso numeric, merma_unidades bigint, merma_costo bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH r AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE) AS h
    ),
    ventas_prod AS (
        SELECT
            vi.producto_id,
            count(DISTINCT vi.venta_id) AS boletas,
            sum(vi.cantidad) AS unidades,
            sum(vi.subtotal) AS ingresos
        FROM venta_items vi
        JOIN ventas v ON v.id = vi.venta_id, r
        WHERE NOT v.anulada
          AND v.creado_en >= r.d AND v.creado_en < r.h + 1
          AND vi.producto_id IS NOT NULL
        GROUP BY vi.producto_id
    ),
    costo_prod AS (
        SELECT
            vc.producto_id,
            sum(vc.cantidad * vc.costo_unitario) AS costo
        FROM venta_consumos vc
        JOIN ventas v ON v.id = vc.venta_id, r
        WHERE NOT v.anulada
          AND v.creado_en >= r.d AND v.creado_en < r.h + 1
        GROUP BY vc.producto_id
    ),
    merma_prod AS (
        SELECT
            me.producto_id,
            sum(me.cantidad) AS unidades,
            sum(me.costo_perdido) AS costo
        FROM mermas me, r
        WHERE NOT me.revertida
          AND me.creado_en >= r.d AND me.creado_en < r.h + 1
        GROUP BY me.producto_id
    ),
    total AS (
        SELECT GREATEST(sum(vp.ingresos) - COALESCE(sum(cp.costo), 0), 1) AS utilidad
        FROM ventas_prod vp
        LEFT JOIN costo_prod cp ON cp.producto_id = vp.producto_id
    )
    SELECT
        vp.producto_id, p.nombre, p.emoji, c.nombre,
        vp.boletas, vp.unidades, vp.ingresos,
        COALESCE(cp.costo, 0)::bigint,
        (vp.ingresos - COALESCE(cp.costo, 0))::bigint,
        CASE WHEN vp.ingresos > 0
             THEN ROUND((vp.ingresos - COALESCE(cp.costo, 0))::numeric / vp.ingresos * 100, 1)
             ELSE 0 END,
        ROUND((vp.ingresos - COALESCE(cp.costo, 0))::numeric / t.utilidad * 100, 1),
        COALESCE(mp.unidades, 0),
        COALESCE(mp.costo, 0)::bigint
    FROM ventas_prod vp
    JOIN productos p       ON p.id = vp.producto_id
    LEFT JOIN categorias c ON c.id = p.categoria_id
    LEFT JOIN costo_prod cp ON cp.producto_id = vp.producto_id
    LEFT JOIN merma_prod mp ON mp.producto_id = vp.producto_id
    CROSS JOIN total t
    ORDER BY 9 DESC
    LIMIT GREATEST(p_limite, 1);
$$;


--
-- Name: sp_rep_c_resultado(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_resultado(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(desde date, hasta date, dias integer, boletas bigint, unidades bigint, ingresos bigint, neto bigint, iva bigint, costo_ventas bigint, utilidad_bruta bigint, margen numeric, descuentos bigint, descuento_promo bigint, descuento_manual bigint, descuento_canje bigint, merma bigint, resultado bigint, ticket_promedio bigint, boletas_por_dia numeric, anuladas bigint, monto_anulado bigint, efectivo bigint, debito bigint, credito bigint, transferencia bigint, clientes_distintos bigint, con_ficha bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH r AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE) AS h
    ),
    v AS (
        SELECT ve.* FROM ventas ve, r
        WHERE NOT ve.anulada
          AND ve.creado_en >= r.d
          -- +1 día porque creado_en lleva hora: sin eso, "hasta el 22"
          -- deja fuera todo lo que se vendió ese día.
          AND ve.creado_en < r.h + 1
    ),
    costo AS (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) AS total
        FROM venta_consumos vc WHERE vc.venta_id IN (SELECT id FROM v)
    ),
    unidades AS (
        SELECT COALESCE(sum(vi.cantidad), 0) AS total
        FROM venta_items vi WHERE vi.venta_id IN (SELECT id FROM v)
    ),
    anul AS (
        SELECT count(*) AS n, COALESCE(sum(ve.total), 0) AS monto
        FROM ventas ve, r
        WHERE ve.anulada AND ve.creado_en >= r.d AND ve.creado_en < r.h + 1
    ),
    mer AS (
        SELECT COALESCE(sum(me.costo_perdido), 0) AS total
        FROM mermas me, r
        WHERE NOT me.revertida AND me.creado_en >= r.d AND me.creado_en < r.h + 1
    )
    SELECT
        r.d, r.h, (r.h - r.d + 1)::int,

        (SELECT count(*) FROM v),
        u.total,
        (SELECT COALESCE(sum(total), 0) FROM v),
        (SELECT COALESCE(sum(neto), 0) FROM v),
        (SELECT COALESCE(sum(iva_monto), 0) FROM v),

        c.total::bigint,
        ((SELECT COALESCE(sum(total), 0) FROM v) - c.total)::bigint,
        CASE WHEN (SELECT sum(total) FROM v) > 0
             THEN ROUND(((SELECT sum(total) FROM v) - c.total)::numeric
                        / (SELECT sum(total) FROM v) * 100, 1)
             ELSE 0 END,

        (SELECT COALESCE(sum(descuento_total), 0) FROM v),
        (SELECT COALESCE(sum(descuento_promo), 0) FROM v),
        (SELECT COALESCE(sum(descuento_manual), 0) FROM v),
        (SELECT COALESCE(sum(descuento_canje), 0) FROM v),

        m.total::bigint,
        ((SELECT COALESCE(sum(total), 0) FROM v) - c.total - m.total)::bigint,

        CASE WHEN (SELECT count(*) FROM v) > 0
             THEN ROUND((SELECT sum(total) FROM v)::numeric / (SELECT count(*) FROM v))
             ELSE 0 END::bigint,
        ROUND((SELECT count(*) FROM v)::numeric / GREATEST(r.h - r.d + 1, 1), 1),
        a.n, a.monto,

        (SELECT COALESCE(sum(total) FILTER (WHERE medio_pago = 'efectivo'), 0) FROM v),
        (SELECT COALESCE(sum(total) FILTER (WHERE medio_pago = 'debito'), 0) FROM v),
        (SELECT COALESCE(sum(total) FILTER (WHERE medio_pago = 'credito'), 0) FROM v),
        (SELECT COALESCE(sum(total) FILTER (WHERE medio_pago = 'transferencia'), 0) FROM v),

        (SELECT count(DISTINCT cliente_id) FROM v WHERE cliente_id IS NOT NULL),
        (SELECT count(*) FROM v WHERE cliente_id IS NOT NULL)

    FROM r, costo c, unidades u, anul a, mer m;
$$;


--
-- Name: sp_rep_c_resultado_dias(date, date); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_resultado_dias(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date) RETURNS TABLE(dia date, boletas bigint, vendido bigint, costo bigint, utilidad bigint)
    LANGUAGE sql STABLE
    AS $$
    WITH r AS (
        SELECT COALESCE(p_desde, CURRENT_DATE - 30) AS d,
               COALESCE(p_hasta, CURRENT_DATE) AS h
    ),
    serie AS (
        SELECT generate_series(r.d, r.h, '1 day'::interval)::date AS dia FROM r
    )
    SELECT
        s.dia,
        COALESCE(v.boletas, 0),
        COALESCE(v.vendido, 0),
        COALESCE(c.costo, 0)::bigint,
        (COALESCE(v.vendido, 0) - COALESCE(c.costo, 0))::bigint
    FROM serie s
    LEFT JOIN LATERAL (
        SELECT count(*) AS boletas, COALESCE(sum(total), 0) AS vendido
        FROM ventas WHERE creado_en::date = s.dia AND NOT anulada
    ) v ON true
    LEFT JOIN LATERAL (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) AS costo
        FROM venta_consumos vc
        JOIN ventas ve ON ve.id = vc.venta_id
        WHERE ve.creado_en::date = s.dia AND NOT ve.anulada
    ) c ON true
    ORDER BY s.dia;
$$;


--
-- Name: sp_rep_c_turno(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_rep_c_turno(p_caja_id integer) RETURNS TABLE(caja_id integer, estado public.estado_caja, abierta_en timestamp with time zone, cerrada_en timestamp with time zone, abierta_por text, cerrada_por text, minutos integer, fondo_inicial integer, efectivo bigint, debito bigint, credito bigint, transferencia bigint, total_vendido bigint, boletas bigint, anuladas bigint, unidades bigint, ticket_promedio bigint, costo bigint, utilidad bigint, margen numeric, efectivo_esperado integer, efectivo_contado integer, diferencia integer, nota_cierre text, hora_pico integer, boletas_hora_pico bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        c.id, c.estado, c.abierta_en, c.cerrada_en,
        ua.nombre, uc.nombre,
        CASE WHEN c.cerrada_en IS NOT NULL
             THEN EXTRACT(epoch FROM c.cerrada_en - c.abierta_en)::int / 60
             ELSE EXTRACT(epoch FROM now() - c.abierta_en)::int / 60 END,

        c.fondo_inicial,
        COALESCE(v.efectivo, 0), COALESCE(v.debito, 0),
        COALESCE(v.credito, 0), COALESCE(v.transferencia, 0),
        COALESCE(v.total, 0),

        COALESCE(v.boletas, 0), COALESCE(v.anuladas, 0),
        COALESCE(u.unidades, 0),
        CASE WHEN COALESCE(v.boletas, 0) > 0
             THEN ROUND(v.total::numeric / v.boletas) ELSE 0 END::bigint,

        COALESCE(k.costo, 0)::bigint,
        (COALESCE(v.total, 0) - COALESCE(k.costo, 0))::bigint,
        CASE WHEN COALESCE(v.total, 0) > 0
             THEN ROUND((v.total - COALESCE(k.costo, 0))::numeric / v.total * 100, 1)
             ELSE 0 END,

        c.efectivo_esperado, c.efectivo_contado, c.diferencia, c.nota_cierre,

        pico.hora, pico.n

    FROM cajas c
    LEFT JOIN usuarios ua ON ua.id = c.abierta_por
    LEFT JOIN usuarios uc ON uc.id = c.cerrada_por
    LEFT JOIN LATERAL (
        SELECT
            count(*) FILTER (WHERE NOT ve.anulada) AS boletas,
            count(*) FILTER (WHERE ve.anulada) AS anuladas,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada), 0) AS total,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada AND ve.medio_pago = 'efectivo'), 0) AS efectivo,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada AND ve.medio_pago = 'debito'), 0) AS debito,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada AND ve.medio_pago = 'credito'), 0) AS credito,
            COALESCE(sum(ve.total) FILTER (WHERE NOT ve.anulada AND ve.medio_pago = 'transferencia'), 0) AS transferencia
        FROM ventas ve WHERE ve.caja_id = c.id
    ) v ON true
    LEFT JOIN LATERAL (
        SELECT COALESCE(sum(vi.cantidad), 0) AS unidades
        FROM venta_items vi JOIN ventas ve ON ve.id = vi.venta_id
        WHERE ve.caja_id = c.id AND NOT ve.anulada
    ) u ON true
    LEFT JOIN LATERAL (
        SELECT COALESCE(sum(vc.cantidad * vc.costo_unitario), 0) AS costo
        FROM venta_consumos vc JOIN ventas ve ON ve.id = vc.venta_id
        WHERE ve.caja_id = c.id AND NOT ve.anulada
    ) k ON true
    LEFT JOIN LATERAL (
        SELECT extract(hour FROM ve.creado_en)::int AS hora, count(*) AS n
        FROM ventas ve
        WHERE ve.caja_id = c.id AND NOT ve.anulada
        GROUP BY 1 ORDER BY 2 DESC LIMIT 1
    ) pico ON true
    WHERE c.id = p_caja_id;
$$;


--
-- Name: sp_ven_c_caja(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_caja(p_id integer) RETURNS TABLE(id integer, estado public.estado_caja, fondo_inicial integer, abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer, cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text, efectivo integer, debito integer, credito integer, transferencia integer, total_vendido integer, boletas bigint, anuladas bigint, total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer, en_cajon integer, efectivo_esperado integer, efectivo_contado integer, diferencia integer, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        r.id, r.estado, r.fondo_inicial,
        r.abierta_en, r.abierta_por, r.abierta_por_id,
        r.cerrada_en, r.cerrada_por, r.nota_cierre,
        r.efectivo::int, r.debito::int, r.credito::int, r.transferencia::int,
        r.total_vendido::int, r.boletas, r.anuladas,
        r.total_descuentos::int, r.puntos_otorgados::int, r.puntos_canjeados::int,
        r.en_cajon::int, r.efectivo_esperado::int, r.efectivo_contado, r.diferencia,
        1::bigint
    FROM vw_cajas_resumen r
    WHERE r.id = p_id;
$$;


--
-- Name: sp_ven_c_caja_actual(); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_caja_actual() RETURNS TABLE(id integer, estado public.estado_caja, fondo_inicial integer, abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer, cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text, efectivo integer, debito integer, credito integer, transferencia integer, total_vendido integer, boletas bigint, anuladas bigint, total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer, en_cajon integer, efectivo_esperado integer, efectivo_contado integer, diferencia integer, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        r.id, r.estado, r.fondo_inicial,
        r.abierta_en, r.abierta_por, r.abierta_por_id,
        r.cerrada_en, r.cerrada_por, r.nota_cierre,
        r.efectivo::int, r.debito::int, r.credito::int, r.transferencia::int,
        r.total_vendido::int, r.boletas, r.anuladas,
        r.total_descuentos::int, r.puntos_otorgados::int, r.puntos_canjeados::int,
        r.en_cajon::int, r.efectivo_esperado::int, r.efectivo_contado, r.diferencia,
        1::bigint
    FROM vw_cajas_resumen r
    WHERE r.estado = 'abierta';
$$;


--
-- Name: sp_ven_c_caja_usuario(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_caja_usuario(p_caja_id integer, p_usuario_id integer) RETURNS TABLE(id integer, estado public.estado_caja, fondo_inicial integer, abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer, cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text, efectivo integer, debito integer, credito integer, transferencia integer, total_vendido integer, boletas bigint, anuladas bigint, total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer, en_cajon integer, efectivo_esperado integer, efectivo_contado integer, diferencia integer, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_ven_c_cajas(date, date, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_cajas(p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_usuario_id integer DEFAULT NULL::integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30) RETURNS TABLE(id integer, estado public.estado_caja, fondo_inicial integer, abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer, cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text, efectivo integer, debito integer, credito integer, transferencia integer, total_vendido integer, boletas bigint, anuladas bigint, total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer, en_cajon integer, efectivo_esperado integer, efectivo_contado integer, diferencia integer, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        r.id, r.estado, r.fondo_inicial,
        r.abierta_en, r.abierta_por, r.abierta_por_id,
        r.cerrada_en, r.cerrada_por, r.nota_cierre,
        r.efectivo::int, r.debito::int, r.credito::int, r.transferencia::int,
        r.total_vendido::int, r.boletas, r.anuladas,
        r.total_descuentos::int, r.puntos_otorgados::int, r.puntos_canjeados::int,
        r.en_cajon::int, r.efectivo_esperado::int, r.efectivo_contado, r.diferencia,
        count(*) OVER ()
    FROM vw_cajas_resumen r
    WHERE (p_desde IS NULL OR r.abierta_en >= p_desde)
      -- +1 día porque abierta_en lleva hora: sin eso, "hasta el 22" deja
      -- fuera todo lo que pasó ese día después de medianoche.
      AND (p_hasta IS NULL OR r.abierta_en <  p_hasta + 1)
      -- Un vendedor solo ve los turnos que él abrió o cerró.
      AND (p_usuario_id IS NULL
           OR r.abierta_por_id = p_usuario_id
           OR EXISTS (SELECT 1 FROM cajas c2
                       WHERE c2.id = r.id AND c2.cerrada_por = p_usuario_id))
    ORDER BY r.abierta_en DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_ven_c_cajas_usuario(date, date, integer, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_cajas_usuario(p_desde date, p_hasta date, p_usuario_id integer, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 30) RETURNS TABLE(id integer, estado public.estado_caja, fondo_inicial integer, abierta_en timestamp with time zone, abierta_por text, abierta_por_id integer, cerrada_en timestamp with time zone, cerrada_por text, nota_cierre text, efectivo integer, debito integer, credito integer, transferencia integer, total_vendido integer, boletas bigint, anuladas bigint, total_descuentos integer, puntos_otorgados integer, puntos_canjeados integer, en_cajon integer, efectivo_esperado integer, efectivo_contado integer, diferencia integer, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
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
$$;


--
-- Name: sp_ven_c_partida(text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_partida(p_codigo text) RETURNS TABLE(id integer, codigo text, producto_id integer, producto text, emoji text, lote_id integer, lote_codigo text, cantidad_disponible integer, precio_unitario integer, precio_producto integer, precio_ramo numeric, precio_liquidacion numeric, rebaja integer, fecha_vencimiento date, dias_para_vencer integer, dias_en_meson integer, vendible boolean, motivo text, advertencia text)
    LANGUAGE sql STABLE
    AS $$
    WITH entrada AS (
        SELECT CASE
                 WHEN btrim(p_codigo) LIKE 'CLB1*%' THEN split_part(btrim(p_codigo), '*', 5)
                 WHEN btrim(p_codigo) LIKE 'CLB1|%' THEN split_part(btrim(p_codigo), '|', 5)
                 ELSE upper(btrim(p_codigo))
               END AS cod
    )
    SELECT
        v.id, v.codigo,
        v.producto_id, v.producto, v.emoji,
        v.lote_id, v.lote_codigo,
        v.cantidad_disponible,
        v.precio_unitario, v.precio_producto,
        v.precio_ramo, v.precio_liquidacion,
        (v.precio_producto - v.precio_unitario),
        v.fecha_vencimiento, v.dias_para_vencer, v."dias_en_mesón",
        v.vendible,

        -- Por qué no, cuando la respuesta es no.
        CASE
            WHEN v.cantidad_disponible = 0 THEN 'Esta partida ya se vendió entera.'
            WHEN COALESCE(v.dias_para_vencer, 999) < 0
                THEN 'Venció el ' || to_char(v.fecha_vencimiento, 'DD-MM-YYYY') || '.'
        END,

        -- Se puede vender, pero conviene decirlo en voz alta.
        CASE
            WHEN v.dias_para_vencer = 0 THEN 'Vence hoy.'
            WHEN v.dias_para_vencer BETWEEN 1 AND 2
                THEN 'Vence en ' || v.dias_para_vencer || ' día(s).'
            WHEN v.precio_unitario < v.precio_producto
                THEN 'Precio rebajado: ' || (v.precio_producto - v.precio_unitario) || ' menos que el normal.'
            WHEN v."dias_en_mesón" >= 3
                THEN 'Lleva ' || v."dias_en_mesón" || ' días en el mesón.'
        END

    FROM entrada e
    JOIN vw_partidas v ON v.codigo = e.cod;
$$;


--
-- Name: sp_ven_c_partidas(text, integer, boolean, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_partidas(p_buscar text DEFAULT NULL::text, p_producto_id integer DEFAULT NULL::integer, p_historial boolean DEFAULT false, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50) RETURNS TABLE(id integer, codigo text, qr text, producto_id integer, producto text, emoji text, lote_id integer, lote_codigo text, cantidad_inicial integer, cantidad_disponible integer, precio_unitario integer, precio_producto integer, costo_por_vara numeric, fecha_vencimiento date, dias_para_vencer integer, dias_en_meson integer, traspasado_en timestamp with time zone, traspasado_por text, vendible boolean, notas text, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.codigo, v.qr,
        v.producto_id, v.producto, v.emoji,
        v.lote_id, v.lote_codigo,
        v.cantidad_inicial, v.cantidad_disponible,
        v.precio_unitario, v.precio_producto, v.costo_por_vara,
        v.fecha_vencimiento, v.dias_para_vencer, v."dias_en_mesón",
        v.traspasado_en, v.traspasado_por,
        v.vendible, v.notas,
        count(*) OVER ()
    FROM vw_partidas v
    WHERE (p_historial OR v.cantidad_disponible > 0)
      AND (p_buscar IS NULL
           OR v.codigo      ILIKE '%' || p_buscar || '%'
           OR v.producto    ILIKE '%' || p_buscar || '%'
           OR v.lote_codigo ILIKE '%' || p_buscar || '%')
      AND (p_producto_id IS NULL OR v.producto_id = p_producto_id)
    -- Lo que vence antes primero: es lo que hay que vender ya.
    ORDER BY v.dias_para_vencer NULLS LAST, v.traspasado_en
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_ven_c_partidas_producto(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_partidas_producto(p_producto_id integer) RETURNS TABLE(id integer, codigo text, lote_id integer, cantidad_disponible integer, precio_unitario integer, costo_por_vara numeric, fecha_vencimiento date, dias_para_vencer integer, orden integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.codigo, v.lote_id,
        v.cantidad_disponible, v.precio_unitario, v.costo_por_vara,
        v.fecha_vencimiento, v.dias_para_vencer,
        row_number() OVER (
            ORDER BY v.fecha_vencimiento NULLS LAST, v.traspasado_en, v.id)::int
    FROM vw_partidas v
    WHERE v.producto_id = p_producto_id
      AND v.cantidad_disponible > 0
      AND v.vendible
    ORDER BY v.fecha_vencimiento NULLS LAST, v.traspasado_en, v.id;
$$;


--
-- Name: sp_ven_c_promociones_aplicables(jsonb); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_promociones_aplicables(p_items jsonb) RETURNS TABLE(id integer, nombre text, descripcion text, tipo public.tipo_promocion, valor integer, alcance public.alcance_promocion, minimo integer, descuento integer)
    LANGUAGE sql STABLE
    AS $$
    SELECT p.id, p.nombre, p.descripcion, p.tipo, p.valor, p.alcance,
           p.minimo, fn_ven_descuento_promo(p.id, p_items)
    FROM promociones p
    WHERE p.activa
      AND fn_ven_descuento_promo(p.id, p_items) > 0
    -- La que más conviene primero: es la que el vendedor va a elegir.
    ORDER BY fn_ven_descuento_promo(p.id, p_items) DESC, p.nombre;
$$;


--
-- Name: sp_ven_c_venta(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_venta(p_id integer) RETURNS TABLE(id integer, folio text, numero_atencion text, caja_id integer, usuario_id integer, usuario text, cliente_id integer, cliente text, cliente_rut text, promocion_id integer, promocion text, cotizacion_id integer, bruto integer, descuento_promo integer, descuento_manual integer, descuento_canje integer, descuento_total integer, iva_tasa numeric, neto integer, iva_monto integer, total integer, medio_pago public.medio_pago, recibido integer, vuelto integer, autorizado_por text, puntos_ganados integer, puntos_canjeados integer, abono_previo integer, anulada boolean, motivo_anulacion text, anulada_por integer, anulador text, anulada_en timestamp with time zone, creado_en timestamp with time zone, costo_total numeric, margen numeric, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.folio, v.numero_atencion,
        v.caja_id, v.usuario_id, u.nombre,
        v.cliente_id, c.nombre, c.rut,
        v.promocion_id, pr.nombre, v.cotizacion_id,
        v.bruto, v.descuento_promo, v.descuento_manual, v.descuento_canje,
        v.descuento_total, v.iva_tasa, v.neto, v.iva_monto, v.total,
        v.medio_pago, v.recibido, v.vuelto,
        v.autorizado_por,
        v.puntos_ganados, v.puntos_canjeados,
        v.abono_previo,
        v.anulada, v.motivo_anulacion, v.anulada_por, uan.nombre,
        v.anulada_en, v.creado_en,
        -- El costo real de esta boleta sale de los consumos, no del costo de
        -- ficha: es lo que efectivamente valían las varas que salieron.
        COALESCE(cst.costo, 0),
        CASE WHEN v.total > 0
             THEN ROUND((v.total - COALESCE(cst.costo, 0))::numeric
                        / v.total::numeric * 100, 1) END,
        1::bigint
    FROM ventas v
    LEFT JOIN usuarios u     ON u.id   = v.usuario_id
    LEFT JOIN usuarios uan   ON uan.id = v.anulada_por
    LEFT JOIN clientes c     ON c.id   = v.cliente_id
    LEFT JOIN promociones pr ON pr.id  = v.promocion_id
    LEFT JOIN LATERAL (
        SELECT sum(vc.cantidad * vc.costo_unitario) AS costo
        FROM venta_consumos vc WHERE vc.venta_id = v.id
    ) cst ON true
    WHERE v.id = p_id;
$$;


--
-- Name: sp_ven_c_venta_consumos(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_venta_consumos(p_venta_id integer) RETURNS TABLE(id integer, producto_id integer, producto text, emoji text, lote_id integer, lote_codigo text, tipo public.tipo_consumo, cantidad integer, costo_unitario numeric, costo_total numeric)
    LANGUAGE sql STABLE
    AS $$
    SELECT vc.id, vc.producto_id, p.nombre, p.emoji,
           vc.lote_id, l.codigo, vc.tipo,
           vc.cantidad, vc.costo_unitario,
           ROUND(vc.cantidad * vc.costo_unitario)
    FROM venta_consumos vc
    JOIN productos p  ON p.id = vc.producto_id
    LEFT JOIN lotes l ON l.id = vc.lote_id
    WHERE vc.venta_id = p_venta_id
    ORDER BY vc.id;
$$;


--
-- Name: sp_ven_c_venta_items(integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_venta_items(p_venta_id integer) RETURNS TABLE(id integer, producto_id integer, nombre text, emoji text, precio_unitario integer, cantidad integer, subtotal integer, es_servicio boolean)
    LANGUAGE sql STABLE
    AS $$
    SELECT vi.id, vi.producto_id, vi.nombre, p.emoji,
           vi.precio_unitario, vi.cantidad, vi.subtotal, vi.es_servicio
    FROM venta_items vi
    LEFT JOIN productos p ON p.id = vi.producto_id
    WHERE vi.venta_id = p_venta_id
    ORDER BY vi.id;
$$;


--
-- Name: sp_ven_c_ventas(text, integer, integer, public.medio_pago, date, date, boolean, integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_c_ventas(p_buscar text DEFAULT NULL::text, p_usuario_id integer DEFAULT NULL::integer, p_cliente_id integer DEFAULT NULL::integer, p_medio_pago public.medio_pago DEFAULT NULL::public.medio_pago, p_desde date DEFAULT NULL::date, p_hasta date DEFAULT NULL::date, p_incluir_anuladas boolean DEFAULT false, p_pagina integer DEFAULT 1, p_tamano integer DEFAULT 50) RETURNS TABLE(id integer, folio text, numero_atencion text, caja_id integer, usuario_id integer, usuario text, cliente_id integer, cliente text, bruto integer, descuento_total integer, total integer, medio_pago public.medio_pago, recibido integer, vuelto integer, puntos_ganados integer, puntos_canjeados integer, lineas bigint, unidades bigint, anulada boolean, motivo_anulacion text, anulada_en timestamp with time zone, creado_en timestamp with time zone, total_filas bigint)
    LANGUAGE sql STABLE
    AS $$
    SELECT
        v.id, v.folio, v.numero_atencion,
        v.caja_id, v.usuario_id, u.nombre,
        v.cliente_id, c.nombre,
        v.bruto, v.descuento_total, v.total,
        v.medio_pago, v.recibido, v.vuelto,
        v.puntos_ganados, v.puntos_canjeados,
        (SELECT count(*) FROM venta_items vi WHERE vi.venta_id = v.id),
        (SELECT COALESCE(sum(vi.cantidad), 0) FROM venta_items vi WHERE vi.venta_id = v.id),
        v.anulada, v.motivo_anulacion, v.anulada_en,
        v.creado_en,
        count(*) OVER ()
    FROM ventas v
    LEFT JOIN usuarios u ON u.id = v.usuario_id
    LEFT JOIN clientes c ON c.id = v.cliente_id
    WHERE (p_buscar IS NULL
           OR v.folio  ILIKE '%' || p_buscar || '%'
           OR c.nombre ILIKE '%' || p_buscar || '%'
           OR c.rut    ILIKE '%' || p_buscar || '%')
      AND (p_usuario_id IS NULL OR v.usuario_id = p_usuario_id)
      AND (p_cliente_id IS NULL OR v.cliente_id = p_cliente_id)
      AND (p_medio_pago IS NULL OR v.medio_pago = p_medio_pago)
      AND (p_desde IS NULL OR v.creado_en >= p_desde)
      -- +1 día porque creado_en lleva hora: sin eso, "hasta el 22" deja
      -- fuera todo lo que se vendió ese día.
      AND (p_hasta IS NULL OR v.creado_en < p_hasta + 1)
      AND (p_incluir_anuladas OR NOT v.anulada)
    ORDER BY v.creado_en DESC, v.id DESC
    LIMIT  GREATEST(p_tamano, 1)
    OFFSET GREATEST(p_pagina - 1, 0) * GREATEST(p_tamano, 1);
$$;


--
-- Name: sp_ven_i_caja(integer, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_i_caja(p_fondo_inicial integer, p_usuario_id integer) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id     int;
    v_quien  text;
BEGIN
    IF p_usuario_id IS NULL OR p_usuario_id <= 0 THEN
        RAISE EXCEPTION 'Sesión inválida.';
    END IF;

    IF coalesce(p_fondo_inicial, 0) < 0 THEN
        RAISE EXCEPTION 'El fondo inicial no puede ser negativo.';
    END IF;

    -- El índice único ya lo impide, pero un 23505 crudo no le dice nada a
    -- quien está parado en el mesón. Acá el mensaje nombra a la persona,
    -- que es lo que permite ir a buscarla.
    SELECT u.nombre INTO v_quien
      FROM cajas c JOIN usuarios u ON u.id = c.abierta_por
     WHERE c.estado = 'abierta';

    IF FOUND THEN
        RAISE EXCEPTION 'Ya hay una caja abierta por %. Hay que cerrarla antes de abrir otra.', v_quien;
    END IF;

    INSERT INTO cajas (estado, fondo_inicial, abierta_por)
    VALUES ('abierta', coalesce(p_fondo_inicial, 0), p_usuario_id)
    RETURNING id INTO v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_ven_i_traspaso(text, integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_i_traspaso(p_lote text, p_cantidad integer, p_usuario_id integer DEFAULT NULL::integer, p_notas text DEFAULT NULL::text) RETURNS TABLE(o_partida_id integer, o_codigo text, o_qr text, o_producto_id integer, o_producto text, o_lote_codigo text, o_cantidad integer, o_en_bodega integer, o_en_mostrador integer, o_precio integer, o_vencimiento date)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_cod        text;
    v_lote       record;
    v_partida_id int;
    v_codigo     text;
    v_bodega     int;
    v_mostrador  int;
BEGIN
    IF p_cantidad IS NULL OR p_cantidad < 1 THEN
        RAISE EXCEPTION 'Indica cuántas varas bajar.';
    END IF;

    -- El código legible es siempre el último campo del QR.
    v_cod := CASE
               WHEN btrim(p_lote) LIKE 'CLB1*%' THEN split_part(btrim(p_lote), '*', 5)
               WHEN btrim(p_lote) LIKE 'CLB1|%' THEN split_part(btrim(p_lote), '|', 5)
               ELSE upper(btrim(p_lote))
             END;

    -- FOR UPDATE: dos personas bajando del mismo balde a la vez leerían
    -- las mismas varas disponibles y dejarían el lote en negativo.
    SELECT l.*, p.nombre AS producto, p.precio AS precio_producto, p.tipo
      INTO v_lote
      FROM lotes l JOIN productos p ON p.id = l.producto_id
     WHERE l.codigo = v_cod
       FOR UPDATE OF l;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay ningún lote con el código %.', v_cod;
    END IF;

    IF v_lote.estado <> 'activo' THEN
        RAISE EXCEPTION 'El lote % está %.', v_cod, v_lote.estado;
    END IF;

    IF v_lote.varas_disponibles < p_cantidad THEN
        RAISE EXCEPTION 'El lote % tiene % varas y estás bajando %.',
            v_cod, v_lote.varas_disponibles, p_cantidad;
    END IF;

    -- Bajar flor vencida al mesón es ponerla a la venta. Si de verdad se
    -- quiere liquidar, primero hay que registrar la merma con reingreso,
    -- que le pone precio de segunda.
    IF v_lote.fecha_vencimiento IS NOT NULL
       AND v_lote.fecha_vencimiento < CURRENT_DATE THEN
        RAISE EXCEPTION 'El lote % venció el %. Regístralo como merma en vez de bajarlo.',
            v_cod, to_char(v_lote.fecha_vencimiento, 'DD-MM-YYYY');
    END IF;

    -- Sale del lote
    UPDATE lotes
       SET varas_disponibles = varas_disponibles - p_cantidad,
           estado = CASE WHEN varas_disponibles - p_cantidad = 0
                         THEN 'agotado'::estado_lote ELSE estado END,
           actualizado_en = now()
     WHERE id = v_lote.id
    RETURNING varas_disponibles INTO v_bodega;

    -- Y del stock de bodega del producto
    UPDATE productos
       SET stock = coalesce(stock, 0) - p_cantidad,
           actualizado_en = now()
     WHERE id = v_lote.producto_id;

    -- Nace la partida, con su código propio
    v_codigo := fn_ven_codigo_partida();

    INSERT INTO partidas_mostrador (
        codigo, producto_id, lote_id,
        cantidad_inicial, cantidad_disponible,
        precio_unitario, costo_por_vara, fecha_vencimiento,
        traspasado_por, notas
    )
    VALUES (
        v_codigo, v_lote.producto_id, v_lote.id,
        p_cantidad, p_cantidad,
        -- El precio del lote si lo tiene; si no, el del producto.
        COALESCE(v_lote.precio_unitario, v_lote.precio_producto),
        v_lote.costo_por_vara, v_lote.fecha_vencimiento,
        p_usuario_id, nullif(btrim(coalesce(p_notas, '')), '')
    )
    RETURNING id INTO v_partida_id;

    SELECT COALESCE(sum(cantidad_disponible), 0)::int INTO v_mostrador
      FROM partidas_mostrador WHERE producto_id = v_lote.producto_id;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        v_lote.producto_id, v_lote.id, 'traspaso', p_cantidad, v_bodega,
        'Traspaso a mostrador',
        'Lote ' || v_cod || ' → partida ' || v_codigo,
        p_usuario_id, 'partida', v_partida_id
    );

    RETURN QUERY
    SELECT v_partida_id, v_codigo,
           'CLB1*' || v_partida_id || '*' || v_lote.producto_id
                   || '*' || to_char(now(), 'YYYYMMDD') || '*' || v_codigo,
           v_lote.producto_id, v_lote.producto, v_cod,
           p_cantidad, v_bodega, v_mostrador,
           COALESCE(v_lote.precio_unitario, v_lote.precio_producto)::int,
           v_lote.fecha_vencimiento;
END;
$$;


--
-- Name: sp_ven_i_traspaso_simple(integer, integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_i_traspaso_simple(p_producto_id integer, p_cantidad integer, p_usuario_id integer DEFAULT NULL::integer, p_notas text DEFAULT NULL::text) RETURNS TABLE(o_partida_id integer, o_codigo text, o_qr text, o_producto_id integer, o_producto text, o_lote_codigo text, o_cantidad integer, o_en_bodega integer, o_en_mostrador integer, o_precio integer, o_vencimiento date)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_prod       record;
    v_partida_id int;
    v_codigo     text;
    v_bodega     int;
    v_mostrador  int;
BEGIN
    IF p_cantidad IS NULL OR p_cantidad < 1 THEN
        RAISE EXCEPTION 'Indica cuántas unidades bajar.';
    END IF;

    SELECT * INTO v_prod FROM productos WHERE id = p_producto_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'El producto no existe.';
    END IF;

    IF v_prod.controla_lotes THEN
        RAISE EXCEPTION '% controla lotes: hay que bajarlo escaneando un balde.', v_prod.nombre;
    END IF;

    IF v_prod.tipo = 'armado' THEN
        RAISE EXCEPTION 'Los productos armados no se traspasan: se arman y se ponen en vitrina.';
    END IF;

    IF coalesce(v_prod.stock, 0) < p_cantidad THEN
        RAISE EXCEPTION 'Solo hay % en bodega y estás bajando %.', coalesce(v_prod.stock, 0), p_cantidad;
    END IF;

    UPDATE productos
       SET stock = stock - p_cantidad, actualizado_en = now()
     WHERE id = p_producto_id
    RETURNING stock INTO v_bodega;

    v_codigo := fn_ven_codigo_partida();

    INSERT INTO partidas_mostrador (
        codigo, producto_id, lote_id,
        cantidad_inicial, cantidad_disponible,
        precio_unitario, costo_por_vara,
        traspasado_por, notas
    )
    VALUES (
        v_codigo, p_producto_id, NULL,
        p_cantidad, p_cantidad,
        v_prod.precio, v_prod.costo,
        p_usuario_id, nullif(btrim(coalesce(p_notas, '')), '')
    )
    RETURNING id INTO v_partida_id;

    SELECT COALESCE(sum(cantidad_disponible), 0)::int INTO v_mostrador
      FROM partidas_mostrador WHERE producto_id = p_producto_id;

    INSERT INTO movimientos_inventario (
        producto_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        p_producto_id, 'traspaso', p_cantidad, v_bodega,
        'Traspaso a mostrador', 'Partida ' || v_codigo,
        p_usuario_id, 'partida', v_partida_id
    );

    RETURN QUERY
    SELECT v_partida_id, v_codigo,
           'CLB1*' || v_partida_id || '*' || p_producto_id
                   || '*' || to_char(now(), 'YYYYMMDD') || '*' || v_codigo,
           p_producto_id, v_prod.nombre, NULL::text,
           p_cantidad, v_bodega, v_mostrador,
           v_prod.precio, NULL::date;
END;
$$;


--
-- Name: sp_ven_i_venta(jsonb, public.medio_pago, integer, integer, integer, integer, integer, integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_i_venta(p_items jsonb, p_medio_pago public.medio_pago, p_usuario_id integer, p_cliente_id integer DEFAULT NULL::integer, p_promocion_id integer DEFAULT NULL::integer, p_descuento_manual integer DEFAULT 0, p_puntos_canjeados integer DEFAULT 0, p_recibido integer DEFAULT NULL::integer, p_autorizado_por text DEFAULT NULL::text, p_cotizacion_id integer DEFAULT NULL::integer) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_caja        int;
    v_venta       int;
    v_folio       text;
    v_atencion    text;
    it            jsonb;
    v_n           int := 0;
    v_prod        record;
    v_part        record;
    v_cant        int;
    v_precio      int;
    v_pendiente   int;
    v_toma        int;
    v_bruto       int := 0;
    v_promo       int := 0;
    v_manual      int := 0;
    v_canje       int := 0;
    v_total_desc  int;
    v_iva_tasa    numeric;
    v_neto        int;
    v_iva         int;
    v_total       int;
    v_umbral      int;
    v_club        boolean;
    v_valor_punto int;
    v_canje_min   int;
    v_por_peso    int;
    v_puntos_gana int := 0;
    v_saldo       int;
    v_items_calc  jsonb := '[]'::jsonb;
    v_cot         record;
    v_abono       int := 0;
BEGIN
    -- ═══ 1. Caja abierta ═══
    -- Es la primera pregunta del punto de venta: sin turno no hay dónde
    -- registrar la plata, y una boleta sin caja no aparecería en ningún
    -- arqueo.
    SELECT id INTO v_caja FROM cajas WHERE estado = 'abierta';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay caja abierta. Abre el turno antes de vender.';
    END IF;

    -- ═══ 1b. Cobro final de una cotización ═══
    -- La boleta que cierra un evento. La flor viaja desde el POS, porque se
    -- ajusta a lo que realmente salió; los servicios (lo hecho a medida, el
    -- traslado, el montaje) los pone esta función desde la cotización, con
    -- su precio cotizado: nadie los reescribe en el mesón. Lo ya abonado se
    -- descuenta del total.
    IF p_cotizacion_id IS NOT NULL THEN
        SELECT * INTO v_cot FROM cotizaciones WHERE id = p_cotizacion_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'La cotización no existe.';
        END IF;

        IF v_cot.estado <> 'aprobada' THEN
            RAISE EXCEPTION 'Solo se cobra una cotización aprobada: % está %.', v_cot.folio, v_cot.estado;
        END IF;

        SELECT abono INTO v_abono FROM vw_cot_resumen WHERE id = p_cotizacion_id;

        -- Los puntos del evento van a la ficha de la cotización.
        p_cliente_id := COALESCE(p_cliente_id, v_cot.cliente_id);
    END IF;

    -- Anidado a propósito: PL/pgSQL evalúa v_cot aunque la cotización no
    -- venga, y un registro sin asignar hace fallar toda venta normal.
    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        IF p_cotizacion_id IS NULL THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;

        -- Un evento puede ser solo servicios (un arco, un montaje), pero no nada.
        IF v_cot.traslado = 0 AND v_cot.montaje = 0
           AND NOT EXISTS (SELECT 1 FROM cotizacion_items
                            WHERE cotizacion_id = p_cotizacion_id AND a_medida) THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;
    END IF;

    -- ═══ 2. La cabecera, para tener el id ═══
    -- El folio es correlativo por año; el número de atención se reinicia
    -- cada día, porque es lo que se grita en el mesón: "atención 014" tiene
    -- sentido, "boleta 3.847" no. Va con ceros a la izquierda para que
    -- ordene bien al listar.
    SELECT 'B-' || to_char(CURRENT_DATE, 'YYYY') || '-' ||
           lpad((count(*) + 1)::text, 5, '0')
      INTO v_folio
      FROM ventas WHERE extract(year FROM creado_en) = extract(year FROM CURRENT_DATE);

    SELECT lpad((count(*) + 1)::text, 3, '0') INTO v_atencion
      FROM ventas WHERE creado_en::date = CURRENT_DATE;

    v_iva_tasa := fn_ven_config('venta', 'iva', 19);

    INSERT INTO ventas (
        folio, numero_atencion, caja_id, usuario_id, cliente_id,
        promocion_id, cotizacion_id, iva_tasa, medio_pago,
        bruto, descuento_promo, descuento_manual, descuento_canje,
        descuento_total, neto, iva_monto, total
    )
    VALUES (
        v_folio, v_atencion, v_caja, p_usuario_id, p_cliente_id,
        p_promocion_id, p_cotizacion_id, v_iva_tasa, p_medio_pago,
        0, 0, 0, 0, 0, 0, 0, 0
    )
    RETURNING id INTO v_venta;

    -- ═══ 3. Las líneas ═══
    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n    := v_n + 1;
        v_cant := COALESCE((it->>'cantidad')::int, 0);

        IF v_cant < 1 THEN
            RAISE EXCEPTION 'Línea %: la cantidad debe ser al menos 1.', v_n;
        END IF;

        -- ─── Con partida escaneada ───
        IF it ? 'partida' AND nullif(btrim(it->>'partida'), '') IS NOT NULL THEN

            SELECT pm.*, p.nombre AS producto, p.activo
              INTO v_part
              FROM partidas_mostrador pm
              JOIN productos p ON p.id = pm.producto_id
             WHERE pm.codigo = upper(btrim(it->>'partida'))
               FOR UPDATE OF pm;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: no existe la partida %.', v_n, it->>'partida';
            END IF;

            IF NOT v_part.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_part.producto;
            END IF;

            IF v_part.fecha_vencimiento IS NOT NULL
               AND v_part.fecha_vencimiento < CURRENT_DATE THEN
                RAISE EXCEPTION 'Línea %: la partida % venció el %.',
                    v_n, v_part.codigo, to_char(v_part.fecha_vencimiento, 'DD-MM-YYYY');
            END IF;

            -- Acá es donde la venta se cae si otra caja se llevó las últimas
            -- varas entre el escaneo y el cobro. El escaneo era informativo;
            -- esta validación es la que manda.
            IF v_part.cantidad_disponible < v_cant THEN
                RAISE EXCEPTION 'Línea %: la partida % tiene % y estás vendiendo %.',
                    v_n, v_part.codigo, v_part.cantidad_disponible, v_cant;
            END IF;

            v_precio := v_part.precio_unitario;

            UPDATE partidas_mostrador
               SET cantidad_disponible = cantidad_disponible - v_cant,
                   agotada_en = CASE WHEN cantidad_disponible - v_cant = 0
                                     THEN now() ELSE agotada_en END
             WHERE id = v_part.id;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.producto,
                v_precio, v_cant, v_precio * v_cant, false
            );

            -- El consumo guarda el LOTE, no la partida: es lo que permite
            -- devolver las varas al balde exacto si se anula la boleta.
            INSERT INTO venta_consumos (
                venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.lote_id, 'simple',
                v_cant, COALESCE(v_part.costo_por_vara, 0)
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_part.producto_id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);

        -- ─── Sin escanear: por producto ───
        ELSE
            SELECT * INTO v_prod
              FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
            END IF;

            IF NOT v_prod.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_prod.nombre;
            END IF;

            v_precio := v_prod.precio;

            -- Un armado consume unidades ya montadas, no varas: sus insumos
            -- se descontaron al armarlo.
            IF v_prod.tipo = 'armado' THEN
                IF COALESCE(v_prod.stock_listo, 0) < v_cant THEN
                    RAISE EXCEPTION 'Línea %: solo hay % de % armados.',
                        v_n, COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
                END IF;

                UPDATE productos
                   SET stock_listo = stock_listo - v_cant, actualizado_en = now()
                 WHERE id = v_prod.id;

                INSERT INTO venta_consumos (
                    venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                )
                VALUES (
                    v_venta, v_prod.id, NULL, 'listo',
                    v_cant, COALESCE(v_prod.costo_armado, 0)
                );

            ELSE
                -- Reparto por FIFO entre las partidas del mostrador: lo que
                -- vence antes sale primero.
                v_pendiente := v_cant;

                FOR v_part IN
                    SELECT pm.id, pm.codigo, pm.lote_id, pm.cantidad_disponible,
                           pm.costo_por_vara
                    FROM partidas_mostrador pm
                    WHERE pm.producto_id = v_prod.id
                      AND pm.cantidad_disponible > 0
                      AND COALESCE(pm.fecha_vencimiento >= CURRENT_DATE, true)
                    ORDER BY pm.fecha_vencimiento NULLS LAST, pm.traspasado_en, pm.id
                    FOR UPDATE
                LOOP
                    EXIT WHEN v_pendiente = 0;

                    v_toma := LEAST(v_pendiente, v_part.cantidad_disponible);

                    UPDATE partidas_mostrador
                       SET cantidad_disponible = cantidad_disponible - v_toma,
                           agotada_en = CASE WHEN cantidad_disponible - v_toma = 0
                                             THEN now() ELSE agotada_en END
                     WHERE id = v_part.id;

                    INSERT INTO venta_consumos (
                        venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                    )
                    VALUES (
                        v_venta, v_prod.id, v_part.lote_id, 'simple',
                        v_toma, COALESCE(v_part.costo_por_vara, 0)
                    );

                    v_pendiente := v_pendiente - v_toma;
                END LOOP;

                IF v_pendiente > 0 THEN
                    RAISE EXCEPTION 'Línea %: faltan % de % en el mostrador. Baja más de bodega.',
                        v_n, v_pendiente, v_prod.nombre;
                END IF;
            END IF;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_prod.id, v_prod.nombre,
                v_precio, v_cant, v_precio * v_cant, false
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_prod.id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);
        END IF;
    END LOOP;

    -- ═══ 3b. Los servicios de la cotización ═══
    IF p_cotizacion_id IS NOT NULL THEN
        INSERT INTO venta_items (
            venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
        )
        SELECT v_venta, NULL, s.nombre, s.precio, s.cantidad, s.precio * s.cantidad, true
        FROM (
            SELECT i.nombre, i.precio, i.cantidad, 1 AS orden, i.id
              FROM cotizacion_items i
             WHERE i.cotizacion_id = p_cotizacion_id AND i.a_medida
            UNION ALL
            SELECT 'Traslado', v_cot.traslado, 1, 2, 0 WHERE v_cot.traslado > 0
            UNION ALL
            SELECT 'Montaje', v_cot.montaje, 1, 3, 0 WHERE v_cot.montaje > 0
        ) s
        ORDER BY s.orden, s.id;

        v_bruto := v_bruto + COALESCE((
            SELECT sum(subtotal) FROM venta_items
             WHERE venta_id = v_venta AND es_servicio), 0);
    END IF;

    -- ═══ 4. Descuentos ═══
    -- La promoción se RECALCULA con los precios que acaban de leerse. El
    -- descuento que mostró el carrito no se usa: era una estimación sobre
    -- datos que pudieron cambiar.
    IF p_promocion_id IS NOT NULL THEN
        v_promo := fn_ven_descuento_promo(p_promocion_id, v_items_calc);

        IF v_promo = 0 THEN
            RAISE EXCEPTION 'La promoción ya no aplica a este carrito.';
        END IF;

        UPDATE promociones SET usos = usos + 1, actualizado_en = now()
         WHERE id = p_promocion_id;
    END IF;

    -- El descuento manual sobre el umbral necesita que alguien lo autorice
    -- con su clave. Quién puede autorizar y si la clave es correcta se
    -- verifica en la API, que es donde vive BCrypt; acá solo se exige que
    -- venga firmado.
    v_manual := GREATEST(COALESCE(p_descuento_manual, 0), 0);
    v_umbral := fn_ven_config('venta', 'descuentoSinAutorizacion', 2000)::int;

    IF v_manual > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION 'Un descuento sobre $% necesita autorización.', v_umbral;
    END IF;

    -- ═══ 5. Canje de puntos ═══
    v_club        := fn_ven_config_bool('club', 'activo', false);
    v_valor_punto := fn_ven_config('club', 'valorPunto', 10)::int;
    v_canje_min   := fn_ven_config('club', 'canjeMinimo', 50)::int;
    v_por_peso    := fn_ven_config('club', 'puntosPorPeso', 1000)::int;

    IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
        IF NOT v_club THEN
            RAISE EXCEPTION 'El club de clientes está desactivado.';
        END IF;

        IF p_cliente_id IS NULL THEN
            RAISE EXCEPTION 'Para canjear puntos hay que identificar al cliente.';
        END IF;

        IF p_puntos_canjeados < v_canje_min THEN
            RAISE EXCEPTION 'El canje mínimo es de % puntos.', v_canje_min;
        END IF;

        SELECT puntos INTO v_saldo FROM clientes WHERE id = p_cliente_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'El cliente no existe.';
        END IF;

        IF v_saldo < p_puntos_canjeados THEN
            RAISE EXCEPTION 'El cliente tiene % puntos y quieres canjear %.',
                v_saldo, p_puntos_canjeados;
        END IF;

        v_canje := p_puntos_canjeados * v_valor_punto;
    END IF;

    -- Los descuentos nunca pueden dejar la boleta bajo cero.
    v_total_desc := LEAST(v_promo + v_manual + v_canje, v_bruto);
    v_total      := v_bruto - v_total_desc;

    -- Lo abonado ya entró a caja en sus propias boletas, con su IVA: acá
    -- solo se cobra la diferencia. Si la flor que salió fue menos que lo
    -- abonado, sobra plata, y eso no se resuelve con una boleta negativa.
    IF v_abono > v_total THEN
        RAISE EXCEPTION 'Ya se abonaron $% y esta boleta suma $%. Revisa las líneas o anula un abono.',
            v_abono, v_total;
    END IF;

    v_total := v_total - v_abono;

    -- El IVA va incluido en el precio de venta: el neto se DESARMA del
    -- total, no se suma encima. Cobrar $2.490 y agregarle 19% daría $2.963,
    -- que no es lo que dice la etiqueta de la vitrina.
    v_neto := ROUND(v_total / (1 + v_iva_tasa / 100.0));
    v_iva  := v_total - v_neto;

    IF p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL AND p_recibido < v_total THEN
        RAISE EXCEPTION 'Recibiste $% y el total es $%.', p_recibido, v_total;
    END IF;

    -- ═══ 6. Puntos ganados ═══
    IF v_club AND p_cliente_id IS NOT NULL AND v_por_peso > 0 THEN
        -- Los abonos no dieron puntos: el evento completo los da al cerrarse.
        v_puntos_gana := floor((v_total + v_abono) / v_por_peso)::int;
    END IF;

    -- ═══ 7. Cerrar la boleta ═══
    UPDATE ventas SET
        bruto            = v_bruto,
        descuento_promo  = v_promo,
        descuento_manual = v_manual,
        descuento_canje  = v_canje,
        descuento_total  = v_total_desc,
        neto             = v_neto,
        iva_monto        = v_iva,
        total            = v_total,
        recibido         = p_recibido,
        vuelto           = CASE WHEN p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL
                                THEN p_recibido - v_total END,
        autorizado_por   = nullif(btrim(coalesce(p_autorizado_por, '')), ''),
        puntos_ganados   = v_puntos_gana,
        puntos_canjeados = COALESCE(p_puntos_canjeados, 0),
        abono_previo     = v_abono
    WHERE id = v_venta;

    -- ═══ 8. Puntos del cliente ═══
    IF p_cliente_id IS NOT NULL
       AND (v_puntos_gana > 0 OR COALESCE(p_puntos_canjeados, 0) > 0) THEN

        UPDATE clientes
           SET puntos = puntos + v_puntos_gana - COALESCE(p_puntos_canjeados, 0),
               actualizado_en = now()
         WHERE id = p_cliente_id
        RETURNING puntos INTO v_saldo;

        IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, -p_puntos_canjeados, v_saldo,
                    'Canje en boleta ' || v_folio, v_venta, p_usuario_id);
        END IF;

        IF v_puntos_gana > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, v_puntos_gana, v_saldo,
                    'Compra ' || v_folio, v_venta, p_usuario_id);
        END IF;
    END IF;

    -- ═══ 9. Los movimientos de inventario ═══
    -- Uno por producto, no por consumo: doce líneas de la misma rosa
    -- llenarían el libro sin agregar información.
    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, motivo, detalle,
        usuario_id, referencia_tipo, referencia_id
    )
    SELECT vc.producto_id, NULL, 'venta', -sum(vc.cantidad),
           'Venta ' || v_folio, NULL,
           p_usuario_id, 'venta', v_venta
    FROM venta_consumos vc
    WHERE vc.venta_id = v_venta
    GROUP BY vc.producto_id;

    -- ═══ 10. La cotización queda cobrada ═══
    IF p_cotizacion_id IS NOT NULL THEN
        UPDATE cotizaciones SET
            estado = 'cobrada', venta_id = v_venta,
            cobrada_en = now(), actualizado_en = now()
        WHERE id = p_cotizacion_id;
    END IF;

    RETURN v_venta;
END;
$_$;


--
-- Name: sp_ven_i_venta(jsonb, public.medio_pago, integer, integer, integer, integer, integer, integer, text, integer, boolean, text, text, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_i_venta(p_items jsonb, p_medio_pago public.medio_pago, p_usuario_id integer, p_cliente_id integer DEFAULT NULL::integer, p_promocion_id integer DEFAULT NULL::integer, p_descuento_manual integer DEFAULT 0, p_puntos_canjeados integer DEFAULT 0, p_recibido integer DEFAULT NULL::integer, p_autorizado_por text DEFAULT NULL::text, p_cotizacion_id integer DEFAULT NULL::integer, p_es_despacho boolean DEFAULT false, p_dir_despacho text DEFAULT NULL::text, p_despacho_recibe text DEFAULT NULL::text, p_despacho_telefono text DEFAULT NULL::text, p_despacho_valor integer DEFAULT 0) RETURNS integer
    LANGUAGE plpgsql
    AS $_$
DECLARE
    v_caja        int;
    v_venta       int;
    v_folio       text;
    v_atencion    text;
    it            jsonb;
    v_n           int := 0;
    v_prod        record;
    v_part        record;
    v_cant        int;
    v_precio      int;
    v_pendiente   int;
    v_toma        int;
    v_bruto       int := 0;
    v_promo       int := 0;
    v_manual      int := 0;
    v_canje       int := 0;
    v_total_desc  int;
    v_iva_tasa    numeric;
    v_neto        int;
    v_iva         int;
    v_total       int;
    v_umbral      int;
    v_club        boolean;
    v_valor_punto int;
    v_canje_min   int;
    v_por_peso    int;
    v_puntos_gana int := 0;
    v_saldo       int;
    v_items_calc  jsonb := '[]'::jsonb;
    v_cot         record;
    v_abono       int := 0;
BEGIN
    -- ═══ 1. Caja abierta ═══
    SELECT id INTO v_caja FROM cajas WHERE estado = 'abierta';

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay caja abierta. Abre el turno antes de vender.';
    END IF;

    -- ═══ 1b. Cobro final de una cotización ═══
    IF p_cotizacion_id IS NOT NULL THEN
        SELECT * INTO v_cot FROM cotizaciones WHERE id = p_cotizacion_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'La cotización no existe.';
        END IF;

        IF v_cot.estado <> 'aprobada' THEN
            RAISE EXCEPTION 'Solo se cobra una cotización aprobada: % está %.', v_cot.folio, v_cot.estado;
        END IF;

        SELECT abono INTO v_abono FROM vw_cot_resumen WHERE id = p_cotizacion_id;

        p_cliente_id := COALESCE(p_cliente_id, v_cot.cliente_id);
    END IF;

    IF p_items IS NULL OR jsonb_array_length(p_items) = 0 THEN
        IF p_cotizacion_id IS NULL THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;

        IF v_cot.traslado = 0 AND v_cot.montaje = 0
           AND NOT EXISTS (SELECT 1 FROM cotizacion_items
                            WHERE cotizacion_id = p_cotizacion_id AND a_medida) THEN
            RAISE EXCEPTION 'El carrito está vacío.';
        END IF;
    END IF;

    -- ═══ 1c. Despacho ═══
    -- Sin despacho los datos de entrega no aplican: se descartan en vez de
    -- guardar una dirección de una venta que se retiró en tienda.
    IF NOT COALESCE(p_es_despacho, FALSE) THEN
        p_dir_despacho       := NULL;
        p_despacho_recibe    := NULL;
        p_despacho_telefono  := NULL;
        p_despacho_valor     := 0;
    END IF;

    IF COALESCE(p_despacho_valor, 0) < 0 THEN
        RAISE EXCEPTION 'El valor del despacho no puede ser negativo.';
    END IF;

    -- ═══ 2. La cabecera, para tener el id ═══
    SELECT 'B-' || to_char(CURRENT_DATE, 'YYYY') || '-' ||
           lpad((count(*) + 1)::text, 5, '0')
      INTO v_folio
      FROM ventas WHERE extract(year FROM creado_en) = extract(year FROM CURRENT_DATE);

    SELECT lpad((count(*) + 1)::text, 3, '0') INTO v_atencion
      FROM ventas WHERE creado_en::date = CURRENT_DATE;

    v_iva_tasa := fn_ven_config('venta', 'iva', 19);

    INSERT INTO ventas (
        folio, numero_atencion, caja_id, usuario_id, cliente_id,
        promocion_id, cotizacion_id, iva_tasa, medio_pago,
        bruto, descuento_promo, descuento_manual, descuento_canje,
        descuento_total, neto, iva_monto, total,
        es_despacho, direccion_despacho,
        despacho_recibe, despacho_telefono, despacho_valor
    )
    VALUES (
        v_folio, v_atencion, v_caja, p_usuario_id, p_cliente_id,
        p_promocion_id, p_cotizacion_id, v_iva_tasa, p_medio_pago,
        0, 0, 0, 0, 0, 0, 0, 0,
        COALESCE(p_es_despacho, FALSE),
        NULLIF(BTRIM(COALESCE(p_dir_despacho, '')), ''),
        NULLIF(BTRIM(COALESCE(p_despacho_recibe, '')), ''),
        NULLIF(BTRIM(COALESCE(p_despacho_telefono, '')), ''),
        COALESCE(p_despacho_valor, 0)
    )
    RETURNING id INTO v_venta;

    -- ═══ 3. Las líneas ═══
    FOR it IN SELECT * FROM jsonb_array_elements(p_items)
    LOOP
        v_n    := v_n + 1;
        v_cant := COALESCE((it->>'cantidad')::int, 0);

        IF v_cant < 1 THEN
            RAISE EXCEPTION 'Línea %: la cantidad debe ser al menos 1.', v_n;
        END IF;

        -- ─── Con partida escaneada ───
        IF it ? 'partida' AND nullif(btrim(it->>'partida'), '') IS NOT NULL THEN

            SELECT pm.*, p.nombre AS producto, p.activo
              INTO v_part
              FROM partidas_mostrador pm
              JOIN productos p ON p.id = pm.producto_id
             WHERE pm.codigo = upper(btrim(it->>'partida'))
               FOR UPDATE OF pm;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: no existe la partida %.', v_n, it->>'partida';
            END IF;

            IF NOT v_part.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_part.producto;
            END IF;

            IF v_part.fecha_vencimiento IS NOT NULL
               AND v_part.fecha_vencimiento < CURRENT_DATE THEN
                RAISE EXCEPTION 'Línea %: la partida % venció el %.',
                    v_n, v_part.codigo, to_char(v_part.fecha_vencimiento, 'DD-MM-YYYY');
            END IF;

            IF v_part.cantidad_disponible < v_cant THEN
                RAISE EXCEPTION 'Línea %: la partida % tiene % y estás vendiendo %.',
                    v_n, v_part.codigo, v_part.cantidad_disponible, v_cant;
            END IF;

            v_precio := v_part.precio_unitario;

            UPDATE partidas_mostrador
               SET cantidad_disponible = cantidad_disponible - v_cant,
                   agotada_en = CASE WHEN cantidad_disponible - v_cant = 0
                                     THEN now() ELSE agotada_en END
             WHERE id = v_part.id;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.producto,
                v_precio, v_cant, v_precio * v_cant, false
            );

            INSERT INTO venta_consumos (
                venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
            )
            VALUES (
                v_venta, v_part.producto_id, v_part.lote_id, 'simple',
                v_cant, COALESCE(v_part.costo_por_vara, 0)
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_part.producto_id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);

        -- ─── Sin escanear: por producto ───
        ELSE
            SELECT * INTO v_prod
              FROM productos WHERE id = (it->>'productoId')::int FOR UPDATE;

            IF NOT FOUND THEN
                RAISE EXCEPTION 'Línea %: el producto no existe.', v_n;
            END IF;

            IF NOT v_prod.activo THEN
                RAISE EXCEPTION 'Línea %: % está desactivado.', v_n, v_prod.nombre;
            END IF;

            v_precio := v_prod.precio;

            IF v_prod.tipo = 'armado' THEN
                IF COALESCE(v_prod.stock_listo, 0) < v_cant THEN
                    RAISE EXCEPTION 'Línea %: solo hay % de % armados.',
                        v_n, COALESCE(v_prod.stock_listo, 0), v_prod.nombre;
                END IF;

                UPDATE productos
                   SET stock_listo = stock_listo - v_cant, actualizado_en = now()
                 WHERE id = v_prod.id;

                INSERT INTO venta_consumos (
                    venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                )
                VALUES (
                    v_venta, v_prod.id, NULL, 'listo',
                    v_cant, COALESCE(v_prod.costo_armado, 0)
                );

            ELSE
                v_pendiente := v_cant;

                FOR v_part IN
                    SELECT pm.id, pm.codigo, pm.lote_id, pm.cantidad_disponible,
                           pm.costo_por_vara
                    FROM partidas_mostrador pm
                    WHERE pm.producto_id = v_prod.id
                      AND pm.cantidad_disponible > 0
                      AND COALESCE(pm.fecha_vencimiento >= CURRENT_DATE, true)
                    ORDER BY pm.fecha_vencimiento NULLS LAST, pm.traspasado_en, pm.id
                    FOR UPDATE
                LOOP
                    EXIT WHEN v_pendiente = 0;

                    v_toma := LEAST(v_pendiente, v_part.cantidad_disponible);

                    UPDATE partidas_mostrador
                       SET cantidad_disponible = cantidad_disponible - v_toma,
                           agotada_en = CASE WHEN cantidad_disponible - v_toma = 0
                                             THEN now() ELSE agotada_en END
                     WHERE id = v_part.id;

                    INSERT INTO venta_consumos (
                        venta_id, producto_id, lote_id, tipo, cantidad, costo_unitario
                    )
                    VALUES (
                        v_venta, v_prod.id, v_part.lote_id, 'simple',
                        v_toma, COALESCE(v_part.costo_por_vara, 0)
                    );

                    v_pendiente := v_pendiente - v_toma;
                END LOOP;

                IF v_pendiente > 0 THEN
                    RAISE EXCEPTION 'Línea %: faltan % de % en el mostrador. Baja más de bodega.',
                        v_n, v_pendiente, v_prod.nombre;
                END IF;
            END IF;

            INSERT INTO venta_items (
                venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
            )
            VALUES (
                v_venta, v_prod.id, v_prod.nombre,
                v_precio, v_cant, v_precio * v_cant, false
            );

            v_bruto := v_bruto + (v_precio * v_cant);

            v_items_calc := v_items_calc || jsonb_build_object(
                'productoId', v_prod.id,
                'cantidad',   v_cant,
                'subtotal',   v_precio * v_cant);
        END IF;
    END LOOP;

    -- ═══ 3b. Los servicios de la cotización ═══
    IF p_cotizacion_id IS NOT NULL THEN
        INSERT INTO venta_items (
            venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
        )
        SELECT v_venta, NULL, s.nombre, s.precio, s.cantidad, s.precio * s.cantidad, true
        FROM (
            SELECT i.nombre, i.precio, i.cantidad, 1 AS orden, i.id
              FROM cotizacion_items i
             WHERE i.cotizacion_id = p_cotizacion_id AND i.a_medida
            UNION ALL
            SELECT 'Traslado', v_cot.traslado, 1, 2, 0 WHERE v_cot.traslado > 0
            UNION ALL
            SELECT 'Montaje', v_cot.montaje, 1, 3, 0 WHERE v_cot.montaje > 0
        ) s
        ORDER BY s.orden, s.id;

        v_bruto := v_bruto + COALESCE((
            SELECT sum(subtotal) FROM venta_items
             WHERE venta_id = v_venta AND es_servicio), 0);
    END IF;

    -- ═══ 3c. El despacho, como servicio ═══
    -- Igual que el traslado de una cotización: una línea más de la boleta,
    -- con IVA incluido. Gratis ($0) no deja línea.
    IF COALESCE(p_despacho_valor, 0) > 0 THEN
        INSERT INTO venta_items (
            venta_id, producto_id, nombre, precio_unitario, cantidad, subtotal, es_servicio
        )
        VALUES (v_venta, NULL, 'Despacho', p_despacho_valor, 1, p_despacho_valor, true);

        v_bruto := v_bruto + p_despacho_valor;
    END IF;

    -- ═══ 4. Descuentos ═══
    IF p_promocion_id IS NOT NULL THEN
        v_promo := fn_ven_descuento_promo(p_promocion_id, v_items_calc);

        IF v_promo = 0 THEN
            RAISE EXCEPTION 'La promoción ya no aplica a este carrito.';
        END IF;

        UPDATE promociones SET usos = usos + 1, actualizado_en = now()
         WHERE id = p_promocion_id;
    END IF;

    v_manual := GREATEST(COALESCE(p_descuento_manual, 0), 0);
    v_umbral := fn_ven_config('venta', 'descuentoSinAutorizacion', 2000)::int;

    IF v_manual > v_umbral
       AND nullif(btrim(coalesce(p_autorizado_por, '')), '') IS NULL THEN
        RAISE EXCEPTION 'Un descuento sobre $% necesita autorización.', v_umbral;
    END IF;

    -- ═══ 5. Canje de puntos ═══
    v_club        := fn_ven_config_bool('club', 'activo', false);
    v_valor_punto := fn_ven_config('club', 'valorPunto', 10)::int;
    v_canje_min   := fn_ven_config('club', 'canjeMinimo', 50)::int;
    v_por_peso    := fn_ven_config('club', 'puntosPorPeso', 1000)::int;

    IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
        IF NOT v_club THEN
            RAISE EXCEPTION 'El club de clientes está desactivado.';
        END IF;

        IF p_cliente_id IS NULL THEN
            RAISE EXCEPTION 'Para canjear puntos hay que identificar al cliente.';
        END IF;

        IF p_puntos_canjeados < v_canje_min THEN
            RAISE EXCEPTION 'El canje mínimo es de % puntos.', v_canje_min;
        END IF;

        SELECT puntos INTO v_saldo FROM clientes WHERE id = p_cliente_id FOR UPDATE;

        IF NOT FOUND THEN
            RAISE EXCEPTION 'El cliente no existe.';
        END IF;

        IF v_saldo < p_puntos_canjeados THEN
            RAISE EXCEPTION 'El cliente tiene % puntos y quieres canjear %.',
                v_saldo, p_puntos_canjeados;
        END IF;

        v_canje := p_puntos_canjeados * v_valor_punto;
    END IF;

    v_total_desc := LEAST(v_promo + v_manual + v_canje, v_bruto);
    v_total      := v_bruto - v_total_desc;

    IF v_abono > v_total THEN
        RAISE EXCEPTION 'Ya se abonaron $% y esta boleta suma $%. Revisa las líneas o anula un abono.',
            v_abono, v_total;
    END IF;

    v_total := v_total - v_abono;

    v_neto := ROUND(v_total / (1 + v_iva_tasa / 100.0));
    v_iva  := v_total - v_neto;

    IF p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL AND p_recibido < v_total THEN
        RAISE EXCEPTION 'Recibiste $% y el total es $%.', p_recibido, v_total;
    END IF;

    -- ═══ 6. Puntos ganados ═══
    IF v_club AND p_cliente_id IS NOT NULL AND v_por_peso > 0 THEN
        v_puntos_gana := floor((v_total + v_abono) / v_por_peso)::int;
    END IF;

    -- ═══ 7. Cerrar la boleta ═══
    UPDATE ventas SET
        bruto            = v_bruto,
        descuento_promo  = v_promo,
        descuento_manual = v_manual,
        descuento_canje  = v_canje,
        descuento_total  = v_total_desc,
        neto             = v_neto,
        iva_monto        = v_iva,
        total            = v_total,
        recibido         = p_recibido,
        vuelto           = CASE WHEN p_medio_pago = 'efectivo' AND p_recibido IS NOT NULL
                                THEN p_recibido - v_total END,
        autorizado_por   = nullif(btrim(coalesce(p_autorizado_por, '')), ''),
        puntos_ganados   = v_puntos_gana,
        puntos_canjeados = COALESCE(p_puntos_canjeados, 0),
        abono_previo     = v_abono
    WHERE id = v_venta;

    -- ═══ 8. Puntos del cliente ═══
    IF p_cliente_id IS NOT NULL
       AND (v_puntos_gana > 0 OR COALESCE(p_puntos_canjeados, 0) > 0) THEN

        UPDATE clientes
           SET puntos = puntos + v_puntos_gana - COALESCE(p_puntos_canjeados, 0),
               actualizado_en = now()
         WHERE id = p_cliente_id
        RETURNING puntos INTO v_saldo;

        IF COALESCE(p_puntos_canjeados, 0) > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, -p_puntos_canjeados, v_saldo,
                    'Canje en boleta ' || v_folio, v_venta, p_usuario_id);
        END IF;

        IF v_puntos_gana > 0 THEN
            INSERT INTO puntos_movimientos (
                cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
            )
            VALUES (p_cliente_id, v_puntos_gana, v_saldo,
                    'Compra ' || v_folio, v_venta, p_usuario_id);
        END IF;
    END IF;

    -- ═══ 9. Los movimientos de inventario ═══
    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, motivo, detalle,
        usuario_id, referencia_tipo, referencia_id
    )
    SELECT vc.producto_id, NULL, 'venta', -sum(vc.cantidad),
           'Venta ' || v_folio, NULL,
           p_usuario_id, 'venta', v_venta
    FROM venta_consumos vc
    WHERE vc.venta_id = v_venta
    GROUP BY vc.producto_id;

    -- ═══ 10. La cotización queda cobrada ═══
    IF p_cotizacion_id IS NOT NULL THEN
        UPDATE cotizaciones SET
            estado = 'cobrada', venta_id = v_venta,
            cobrada_en = now(), actualizado_en = now()
        WHERE id = p_cotizacion_id;
    END IF;

    RETURN v_venta;
END;
$_$;


--
-- Name: sp_ven_u_caja_cerrar(integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_u_caja_cerrar(p_efectivo_contado integer, p_usuario_id integer, p_nota text DEFAULT NULL::text) RETURNS integer
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_id        int;
    v_esperado  int;
BEGIN
    IF p_efectivo_contado IS NULL OR p_efectivo_contado < 0 THEN
        RAISE EXCEPTION 'Indica cuánto efectivo contaste en el cajón.';
    END IF;

    -- FOR UPDATE: dos personas cerrando a la vez guardarían dos arqueos
    -- distintos sobre la misma fila.
    SELECT c.id INTO v_id
      FROM cajas c WHERE c.estado = 'abierta' FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay ninguna caja abierta.';
    END IF;

    SELECT r.en_cajon::int INTO v_esperado
      FROM vw_cajas_resumen r WHERE r.id = v_id;

    UPDATE cajas SET
        estado            = 'cerrada',
        cerrada_en        = now(),
        cerrada_por       = p_usuario_id,
        efectivo_esperado = v_esperado,
        efectivo_contado  = p_efectivo_contado,
        diferencia        = p_efectivo_contado - v_esperado,
        nota_cierre       = nullif(btrim(coalesce(p_nota, '')), '')
    WHERE id = v_id;

    RETURN v_id;
END;
$$;


--
-- Name: sp_ven_u_partida_retorno(text, integer, integer, text); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_u_partida_retorno(p_partida text, p_cantidad integer, p_usuario_id integer DEFAULT NULL::integer, p_notas text DEFAULT NULL::text) RETURNS TABLE(o_partida_id integer, o_codigo text, o_producto text, o_devuelto integer, o_en_partida integer, o_en_bodega integer)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_cod      text;
    v_p        record;
    v_queda    int;
    v_bodega   int;
BEGIN
    IF p_cantidad IS NULL OR p_cantidad < 1 THEN
        RAISE EXCEPTION 'Indica cuántas varas devolver.';
    END IF;

    v_cod := CASE
               WHEN btrim(p_partida) LIKE 'CLB1*%' THEN split_part(btrim(p_partida), '*', 5)
               ELSE upper(btrim(p_partida))
             END;

    SELECT pm.*, p.nombre AS producto
      INTO v_p
      FROM partidas_mostrador pm JOIN productos p ON p.id = pm.producto_id
     WHERE pm.codigo = v_cod
       FOR UPDATE OF pm;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'No hay ninguna partida con el código %.', v_cod;
    END IF;

    IF v_p.cantidad_disponible < p_cantidad THEN
        RAISE EXCEPTION 'La partida % tiene % y estás devolviendo %.',
            v_cod, v_p.cantidad_disponible, p_cantidad;
    END IF;

    UPDATE partidas_mostrador
       SET cantidad_disponible = cantidad_disponible - p_cantidad,
           agotada_en = CASE WHEN cantidad_disponible - p_cantidad = 0
                             THEN now() ELSE agotada_en END
     WHERE id = v_p.id
    RETURNING cantidad_disponible INTO v_queda;

    -- Vuelve al lote de origen si lo tiene, y reabre el lote si estaba
    -- agotado justamente porque bajó todo al mesón.
    IF v_p.lote_id IS NOT NULL THEN
        UPDATE lotes
           SET varas_disponibles = varas_disponibles + p_cantidad,
               estado = CASE WHEN estado = 'agotado' THEN 'activo'::estado_lote ELSE estado END,
               actualizado_en = now()
         WHERE id = v_p.lote_id;
    END IF;

    UPDATE productos
       SET stock = coalesce(stock, 0) + p_cantidad, actualizado_en = now()
     WHERE id = v_p.producto_id
    RETURNING stock INTO v_bodega;

    INSERT INTO movimientos_inventario (
        producto_id, lote_id, tipo, cantidad, stock_resultante,
        motivo, detalle, usuario_id, referencia_tipo, referencia_id
    )
    VALUES (
        v_p.producto_id, v_p.lote_id, 'traspaso', p_cantidad, v_bodega,
        'Retorno a bodega', 'Partida ' || v_cod,
        p_usuario_id, 'retorno', v_p.id
    );

    RETURN QUERY
    SELECT v_p.id, v_cod, v_p.producto, p_cantidad, v_queda, v_bodega;
END;
$$;


--
-- Name: sp_ven_u_venta_anular(integer, text, integer); Type: FUNCTION; Schema: public; Owner: -
--

CREATE FUNCTION public.sp_ven_u_venta_anular(p_venta_id integer, p_motivo text, p_usuario_id integer) RETURNS TABLE(o_venta_id integer, o_folio text, o_devuelto integer, o_puntos integer)
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_v        record;
    c          record;
    v_devuelto int := 0;
    v_saldo    int;
BEGIN
    IF btrim(coalesce(p_motivo, '')) = '' THEN
        RAISE EXCEPTION 'Indica por qué se anula la boleta.';
    END IF;

    SELECT * INTO v_v FROM ventas WHERE id = p_venta_id FOR UPDATE;

    IF NOT FOUND THEN
        RAISE EXCEPTION 'La venta no existe.';
    END IF;

    IF v_v.anulada THEN
        RAISE EXCEPTION 'La boleta % ya estaba anulada.', v_v.folio;
    END IF;

    -- Anular una boleta de un turno cerrado descuadraría un arqueo que ya se
    -- firmó. Si el error se detectó tarde, corresponde una nota de crédito,
    -- no reescribir el pasado.
    IF EXISTS (SELECT 1 FROM cajas WHERE id = v_v.caja_id AND estado = 'cerrada') THEN
        RAISE EXCEPTION 'La caja de esta boleta ya se cerró. Anularla descuadraría ese arqueo.';
    END IF;

    -- Devolver al inventario
    FOR c IN
        SELECT vc.producto_id, vc.lote_id, vc.tipo, sum(vc.cantidad) AS cantidad
        FROM venta_consumos vc
        WHERE vc.venta_id = p_venta_id
        GROUP BY vc.producto_id, vc.lote_id, vc.tipo
    LOOP
        IF c.tipo = 'listo' THEN
            UPDATE productos
               SET stock_listo = COALESCE(stock_listo, 0) + c.cantidad,
                   actualizado_en = now()
             WHERE id = c.producto_id;
        ELSE
            IF c.lote_id IS NOT NULL THEN
                UPDATE lotes
                   SET varas_disponibles = varas_disponibles + c.cantidad,
                       estado = CASE WHEN estado = 'agotado'
                                     THEN 'activo'::estado_lote ELSE estado END,
                       actualizado_en = now()
                 WHERE id = c.lote_id;
            END IF;

            UPDATE productos
               SET stock = COALESCE(stock, 0) + c.cantidad, actualizado_en = now()
             WHERE id = c.producto_id;
        END IF;

        v_devuelto := v_devuelto + c.cantidad;

        INSERT INTO movimientos_inventario (
            producto_id, lote_id, tipo, cantidad, motivo, detalle,
            usuario_id, referencia_tipo, referencia_id
        )
        VALUES (
            c.producto_id, c.lote_id, 'ajuste', c.cantidad,
            'Anulación de boleta ' || v_v.folio, p_motivo,
            p_usuario_id, 'anulacion', p_venta_id
        );
    END LOOP;

    -- Revertir los puntos
    IF v_v.cliente_id IS NOT NULL
       AND (v_v.puntos_ganados > 0 OR v_v.puntos_canjeados > 0) THEN

        UPDATE clientes
           SET puntos = puntos - v_v.puntos_ganados + v_v.puntos_canjeados,
               actualizado_en = now()
         WHERE id = v_v.cliente_id
        RETURNING puntos INTO v_saldo;

        INSERT INTO puntos_movimientos (
            cliente_id, cantidad, saldo_resultante, motivo, venta_id, usuario_id
        )
        VALUES (
            v_v.cliente_id,
            v_v.puntos_canjeados - v_v.puntos_ganados, v_saldo,
            'Anulación de boleta ' || v_v.folio, p_venta_id, p_usuario_id
        );
    END IF;

    IF v_v.promocion_id IS NOT NULL THEN
        UPDATE promociones SET usos = GREATEST(usos - 1, 0) WHERE id = v_v.promocion_id;
    END IF;

    -- ═══ Cotizaciones ═══
    -- Si la boleta era el abono de una cotización, el abono deja de contar.
    -- Esto vale igual si se anula desde Ventas o desde la cotización: la
    -- boleta es la que manda.
    WITH g AS (
        UPDATE cotizacion_pagos SET
            anulado = true, anulado_en = now(), motivo_anulacion = btrim(p_motivo)
        WHERE venta_id = p_venta_id AND NOT anulado
        RETURNING cotizacion_id, monto
    )
    -- Alias `cot` y no `c`: la función ya usa `c` como variable del bucle
    -- de devoluciones, y el alias chocaba con ella.
    UPDATE cotizaciones cot
       SET abono = GREATEST(cot.abono - g.monto, 0), actualizado_en = now()
      FROM g
     WHERE cot.id = g.cotizacion_id;

    -- Si era el cobro final, la cotización vuelve a estar por cobrar.
    UPDATE cotizaciones SET
        estado = 'aprobada', venta_id = NULL, cobrada_en = NULL, actualizado_en = now()
    WHERE venta_id = p_venta_id AND estado = 'cobrada';

    UPDATE ventas SET
        anulada          = true,
        motivo_anulacion = btrim(p_motivo),
        anulada_por      = p_usuario_id,
        anulada_en       = now()
    WHERE id = p_venta_id;

    RETURN QUERY
    SELECT p_venta_id, v_v.folio, v_devuelto,
           (v_v.puntos_canjeados - v_v.puntos_ganados);
END;
$$;


--
-- Name: cajas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.cajas (
    id integer NOT NULL,
    estado public.estado_caja DEFAULT 'abierta'::public.estado_caja NOT NULL,
    fondo_inicial integer NOT NULL,
    abierta_en timestamp with time zone DEFAULT now() NOT NULL,
    abierta_por integer NOT NULL,
    cerrada_en timestamp with time zone,
    cerrada_por integer,
    efectivo_esperado integer,
    efectivo_contado integer,
    diferencia integer,
    nota_cierre text,
    CONSTRAINT cajas_cierre_completo CHECK (((estado = 'abierta'::public.estado_caja) OR ((cerrada_en IS NOT NULL) AND (efectivo_contado IS NOT NULL) AND (efectivo_esperado IS NOT NULL)))),
    CONSTRAINT cajas_fondo_no_negativo CHECK ((fondo_inicial >= 0))
);


--
-- Name: cajas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cajas_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cajas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cajas_id_seq OWNED BY public.cajas.id;


--
-- Name: categorias; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.categorias (
    id integer NOT NULL,
    nombre text NOT NULL,
    orden integer DEFAULT 0 NOT NULL,
    CONSTRAINT categorias_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0))
);


--
-- Name: categorias_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.categorias_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: categorias_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.categorias_id_seq OWNED BY public.categorias.id;


--
-- Name: clientes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.clientes (
    id integer NOT NULL,
    rut text NOT NULL,
    nombre text NOT NULL,
    telefono text,
    correo text,
    direccion text,
    cumple_mes smallint,
    cumple_dia smallint,
    notas text,
    puntos integer DEFAULT 0 NOT NULL,
    activo boolean DEFAULT true NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT clientes_cumple_completo CHECK ((((cumple_mes IS NULL) AND (cumple_dia IS NULL)) OR ((cumple_mes IS NOT NULL) AND (cumple_dia IS NOT NULL)))),
    CONSTRAINT clientes_cumple_dia_valido CHECK (((cumple_dia IS NULL) OR ((cumple_dia >= 1) AND (cumple_dia <= 31)))),
    CONSTRAINT clientes_cumple_mes_valido CHECK (((cumple_mes IS NULL) OR ((cumple_mes >= 1) AND (cumple_mes <= 12)))),
    CONSTRAINT clientes_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0)),
    CONSTRAINT clientes_puntos_no_negativos CHECK ((puntos >= 0))
);


--
-- Name: clientes_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.clientes_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: clientes_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.clientes_id_seq OWNED BY public.clientes.id;


--
-- Name: codigos_autorizacion_descuento; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.codigos_autorizacion_descuento (
    id integer NOT NULL,
    codigo character(6) NOT NULL,
    descuento integer NOT NULL,
    vendedor_id integer NOT NULL,
    usado boolean DEFAULT false NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    expira_en timestamp with time zone DEFAULT (now() + '00:10:00'::interval) NOT NULL,
    CONSTRAINT codigos_autorizacion_descuento_descuento_check CHECK ((descuento > 0))
);


--
-- Name: codigos_autorizacion_descuento_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.codigos_autorizacion_descuento_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: codigos_autorizacion_descuento_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.codigos_autorizacion_descuento_id_seq OWNED BY public.codigos_autorizacion_descuento.id;


--
-- Name: codigos_autorizacion_merma; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.codigos_autorizacion_merma (
    id integer NOT NULL,
    codigo character(6) NOT NULL,
    valor integer NOT NULL,
    usuario_id integer NOT NULL,
    usado boolean DEFAULT false NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    expira_en timestamp with time zone DEFAULT (now() + '00:10:00'::interval) NOT NULL,
    CONSTRAINT codigos_autorizacion_merma_valor_check CHECK ((valor >= 0))
);


--
-- Name: codigos_autorizacion_merma_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.codigos_autorizacion_merma_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: codigos_autorizacion_merma_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.codigos_autorizacion_merma_id_seq OWNED BY public.codigos_autorizacion_merma.id;


--
-- Name: compra_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.compra_items (
    id integer NOT NULL,
    compra_id integer NOT NULL,
    producto_id integer NOT NULL,
    presentacion_id integer NOT NULL,
    cantidad integer NOT NULL,
    costo_unitario integer NOT NULL,
    varas_totales integer NOT NULL,
    costo_por_vara numeric(12,4) NOT NULL,
    CONSTRAINT compra_items_cantidad_positiva CHECK ((cantidad > 0)),
    CONSTRAINT compra_items_costo_no_negativo CHECK ((costo_unitario >= 0)),
    CONSTRAINT compra_items_varas_positivas CHECK ((varas_totales > 0))
);


--
-- Name: compra_items_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.compra_items_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: compra_items_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.compra_items_id_seq OWNED BY public.compra_items.id;


--
-- Name: compras; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.compras (
    id integer NOT NULL,
    folio text NOT NULL,
    proveedor_id integer NOT NULL,
    fecha date DEFAULT CURRENT_DATE NOT NULL,
    documento text,
    estado public.estado_compra DEFAULT 'borrador'::public.estado_compra NOT NULL,
    neto integer DEFAULT 0 NOT NULL,
    iva integer DEFAULT 0 NOT NULL,
    total integer DEFAULT 0 NOT NULL,
    notas text,
    usuario_id integer,
    recibida_en timestamp with time zone,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT compras_montos_no_negativos CHECK (((neto >= 0) AND (iva >= 0) AND (total >= 0))),
    CONSTRAINT compras_recibida_con_fecha CHECK (((estado <> 'recibida'::public.estado_compra) OR (recibida_en IS NOT NULL)))
);


--
-- Name: compras_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.compras_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: compras_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.compras_id_seq OWNED BY public.compras.id;


--
-- Name: configuracion; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.configuracion (
    clave text NOT NULL,
    valor jsonb NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_por integer
);


--
-- Name: conteo_lineas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.conteo_lineas (
    id integer NOT NULL,
    conteo_id integer NOT NULL,
    producto_id integer NOT NULL,
    lote_id integer,
    partida_id integer,
    origen text,
    sistema integer NOT NULL,
    contado integer NOT NULL,
    diferencia integer NOT NULL,
    costo_unitario integer DEFAULT 0 NOT NULL,
    merma_id integer,
    CONSTRAINT conteo_lineas_contado_check CHECK ((contado >= 0))
);


--
-- Name: conteo_lineas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.conteo_lineas_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: conteo_lineas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.conteo_lineas_id_seq OWNED BY public.conteo_lineas.id;


--
-- Name: conteos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.conteos (
    id integer NOT NULL,
    ubicacion text NOT NULL,
    detalle text,
    usuario_id integer,
    autorizado_por text,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    lineas integer DEFAULT 0 NOT NULL,
    con_diferencia integer DEFAULT 0 NOT NULL,
    sobrantes integer DEFAULT 0 NOT NULL,
    faltantes integer DEFAULT 0 NOT NULL,
    valor_sobrante integer DEFAULT 0 NOT NULL,
    valor_faltante integer DEFAULT 0 NOT NULL,
    CONSTRAINT conteos_ubicacion_check CHECK ((ubicacion = ANY (ARRAY['bodega'::text, 'vitrina'::text])))
);


--
-- Name: conteos_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.conteos_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: conteos_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.conteos_id_seq OWNED BY public.conteos.id;


--
-- Name: cotizacion_cuotas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cotizacion_cuotas_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cotizacion_cuotas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cotizacion_cuotas_id_seq OWNED BY public.cotizacion_cuotas.id;


--
-- Name: cotizacion_items_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cotizacion_items_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cotizacion_items_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cotizacion_items_id_seq OWNED BY public.cotizacion_items.id;


--
-- Name: cotizacion_pagos_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cotizacion_pagos_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cotizacion_pagos_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cotizacion_pagos_id_seq OWNED BY public.cotizacion_pagos.id;


--
-- Name: cotizaciones_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.cotizaciones_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: cotizaciones_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.cotizaciones_id_seq OWNED BY public.cotizaciones.id;


--
-- Name: inventario_venta; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.inventario_venta (
    producto_id integer NOT NULL,
    stock integer DEFAULT 0 NOT NULL,
    bajado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT inventario_venta_stock_check CHECK ((stock >= 0))
);


--
-- Name: TABLE inventario_venta; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.inventario_venta IS 'Lo que está adelante, listo para vender. Entra por traspaso desde bodega.';


--
-- Name: COLUMN inventario_venta.bajado_en; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.inventario_venta.bajado_en IS 'Primera vez que este producto llegó al mesón. Sirve para ver qué lleva días ahí.';


--
-- Name: lotes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.lotes (
    id integer NOT NULL,
    codigo text NOT NULL,
    producto_id integer NOT NULL,
    compra_id integer,
    compra_item_id integer,
    proveedor_id integer,
    presentacion_id integer,
    fecha_ingreso date DEFAULT CURRENT_DATE NOT NULL,
    fecha_vencimiento date,
    varas_iniciales integer NOT NULL,
    varas_disponibles integer NOT NULL,
    costo_por_vara numeric(12,4) NOT NULL,
    estado public.estado_lote DEFAULT 'activo'::public.estado_lote NOT NULL,
    ubicacion text,
    notas text,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    origen_lote_id integer,
    calidad public.calidad_reingreso,
    precio_unitario integer,
    CONSTRAINT lotes_costo_no_negativo CHECK ((costo_por_vara >= (0)::numeric)),
    CONSTRAINT lotes_disponibles_en_rango CHECK (((varas_disponibles >= 0) AND (varas_disponibles <= varas_iniciales))),
    CONSTRAINT lotes_estado_coherente CHECK ((((estado = 'activo'::public.estado_lote) AND (varas_disponibles > 0)) OR ((estado = 'agotado'::public.estado_lote) AND (varas_disponibles = 0)) OR (estado = 'descartado'::public.estado_lote))),
    CONSTRAINT lotes_iniciales_positivas CHECK ((varas_iniciales > 0)),
    CONSTRAINT lotes_origen_distinto CHECK (((origen_lote_id IS NULL) OR (origen_lote_id <> id))),
    CONSTRAINT lotes_precio_positivo CHECK (((precio_unitario IS NULL) OR (precio_unitario > 0))),
    CONSTRAINT lotes_vencimiento_posterior CHECK (((fecha_vencimiento IS NULL) OR (fecha_vencimiento >= fecha_ingreso)))
);


--
-- Name: lotes_codigo_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.lotes_codigo_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: lotes_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.lotes_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: lotes_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.lotes_id_seq OWNED BY public.lotes.id;


--
-- Name: merma_evidencias; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merma_evidencias (
    id integer NOT NULL,
    reporte_id integer NOT NULL,
    foto bytea NOT NULL,
    tipo_mime text DEFAULT 'image/jpeg'::text NOT NULL,
    bytes integer GENERATED ALWAYS AS (octet_length(foto)) STORED,
    sha256 text NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT merma_evidencias_tamano CHECK (((octet_length(foto) >= 1) AND (octet_length(foto) <= 5242880))),
    CONSTRAINT merma_evidencias_tipo_mime_check CHECK ((tipo_mime = ANY (ARRAY['image/jpeg'::text, 'image/png'::text, 'image/webp'::text])))
);


--
-- Name: merma_evidencias_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.merma_evidencias_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: merma_evidencias_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.merma_evidencias_id_seq OWNED BY public.merma_evidencias.id;


--
-- Name: merma_reportes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.merma_reportes (
    id integer NOT NULL,
    tipo text NOT NULL,
    motivo_id integer,
    detalle text,
    usuario_id integer,
    autorizado_por text,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    deshecho_en timestamp with time zone,
    deshecho_por integer,
    revisado_en timestamp with time zone,
    revisado_por integer,
    nota_revision text,
    CONSTRAINT merma_reportes_tipo_check CHECK ((tipo = ANY (ARRAY['puntual'::text, 'incidente'::text, 'desarme'::text])))
);


--
-- Name: merma_reportes_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.merma_reportes_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: merma_reportes_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.merma_reportes_id_seq OWNED BY public.merma_reportes.id;


--
-- Name: mermas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.mermas (
    id integer NOT NULL,
    producto_id integer NOT NULL,
    lote_id integer,
    cantidad integer NOT NULL,
    motivo text NOT NULL,
    detalle text,
    costo_unitario integer NOT NULL,
    costo_total integer NOT NULL,
    usuario_id integer,
    revertida boolean DEFAULT false NOT NULL,
    revertida_por integer,
    revertida_en timestamp with time zone,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    destino public.destino_merma DEFAULT 'perdida'::public.destino_merma NOT NULL,
    cantidad_recuperada integer DEFAULT 0 NOT NULL,
    calidad_reingreso public.calidad_reingreso,
    lote_recuperacion_id integer,
    costo_recuperado_unitario integer,
    costo_perdido integer GENERATED ALWAYS AS (
CASE
    WHEN (destino = 'devolucion_proveedor'::public.destino_merma) THEN 0
    ELSE ((costo_unitario * (cantidad - cantidad_recuperada)) + ((costo_unitario - COALESCE(costo_recuperado_unitario, costo_unitario)) * cantidad_recuperada))
END) STORED,
    partida_id integer,
    codigo_escaneado text,
    escaneado boolean DEFAULT false NOT NULL,
    autorizado_por text,
    motivo_reversion text,
    desarme_grupo integer,
    desarme_producto_id integer,
    desarme_cantidad integer,
    motivo_id integer,
    reporte_id integer,
    valor_autorizacion integer,
    CONSTRAINT mermas_cantidad_positiva CHECK ((cantidad > 0)),
    CONSTRAINT mermas_costo_cuadra CHECK ((costo_total = (costo_unitario * cantidad))),
    CONSTRAINT mermas_costo_no_negativo CHECK ((costo_unitario >= 0)),
    CONSTRAINT mermas_costo_recuperado_con_reingreso CHECK (((costo_recuperado_unitario IS NULL) OR (cantidad_recuperada > 0))),
    CONSTRAINT mermas_costo_recuperado_no_supera CHECK (((costo_recuperado_unitario IS NULL) OR ((costo_recuperado_unitario >= 0) AND (costo_recuperado_unitario <= costo_unitario)))),
    CONSTRAINT mermas_destino_coherente CHECK ((((destino = 'perdida'::public.destino_merma) AND (cantidad_recuperada = 0)) OR ((destino = 'reingreso'::public.destino_merma) AND (cantidad_recuperada > 0)) OR ((destino = 'devolucion_proveedor'::public.destino_merma) AND (cantidad_recuperada = 0)))),
    CONSTRAINT mermas_motivo_no_vacio CHECK ((length(TRIM(BOTH FROM motivo)) > 0)),
    CONSTRAINT mermas_recuperada_en_rango CHECK (((cantidad_recuperada >= 0) AND (cantidad_recuperada <= cantidad))),
    CONSTRAINT mermas_reingreso_con_calidad CHECK (((cantidad_recuperada = 0) OR (calidad_reingreso IS NOT NULL))),
    CONSTRAINT mermas_reversion_completa CHECK (((NOT revertida) OR (revertida_en IS NOT NULL)))
);


--
-- Name: COLUMN mermas.escaneado; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.mermas.escaneado IS 'true si el código se leyó con la cámara; false si se tipeó a mano.';


--
-- Name: mermas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.mermas_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: mermas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.mermas_id_seq OWNED BY public.mermas.id;


--
-- Name: motivos_merma_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.motivos_merma_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: motivos_merma_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.motivos_merma_id_seq OWNED BY public.motivos_merma.id;


--
-- Name: movimientos_inventario; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.movimientos_inventario (
    id integer NOT NULL,
    producto_id integer NOT NULL,
    lote_id integer,
    tipo public.tipo_movimiento NOT NULL,
    cantidad integer NOT NULL,
    stock_resultante integer,
    motivo text NOT NULL,
    detalle text,
    usuario_id integer,
    referencia_tipo text,
    referencia_id integer,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT movimientos_cantidad_no_cero CHECK ((cantidad <> 0)),
    CONSTRAINT movimientos_motivo_no_vacio CHECK ((length(TRIM(BOTH FROM motivo)) > 0))
);


--
-- Name: movimientos_inventario_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.movimientos_inventario_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: movimientos_inventario_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.movimientos_inventario_id_seq OWNED BY public.movimientos_inventario.id;


--
-- Name: partidas_codigo_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.partidas_codigo_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: partidas_mostrador; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.partidas_mostrador (
    id integer NOT NULL,
    codigo text NOT NULL,
    producto_id integer NOT NULL,
    lote_id integer,
    cantidad_inicial integer NOT NULL,
    cantidad_disponible integer NOT NULL,
    precio_unitario integer NOT NULL,
    costo_por_vara numeric(12,4),
    fecha_vencimiento date,
    traspasado_en timestamp with time zone DEFAULT now() NOT NULL,
    traspasado_por integer,
    agotada_en timestamp with time zone,
    notas text,
    CONSTRAINT partidas_disponible_valido CHECK ((cantidad_disponible <= cantidad_inicial)),
    CONSTRAINT partidas_mostrador_cantidad_disponible_check CHECK ((cantidad_disponible >= 0)),
    CONSTRAINT partidas_mostrador_cantidad_inicial_check CHECK ((cantidad_inicial > 0))
);


--
-- Name: TABLE partidas_mostrador; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON TABLE public.partidas_mostrador IS 'Lo que bajó de un balde concreto al mesón, con su propio QR.';


--
-- Name: partidas_mostrador_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.partidas_mostrador_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: partidas_mostrador_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.partidas_mostrador_id_seq OWNED BY public.partidas_mostrador.id;


--
-- Name: presentaciones; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.presentaciones (
    id integer NOT NULL,
    producto_id integer NOT NULL,
    nombre text NOT NULL,
    tipo public.tipo_presentacion NOT NULL,
    paquetes integer DEFAULT 1 NOT NULL,
    varas_por_paquete integer NOT NULL,
    varas_totales integer GENERATED ALWAYS AS ((paquetes * varas_por_paquete)) STORED,
    predeterminada boolean DEFAULT false NOT NULL,
    activa boolean DEFAULT true NOT NULL,
    CONSTRAINT presentaciones_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0)),
    CONSTRAINT presentaciones_paquetes_positivo CHECK ((paquetes > 0)),
    CONSTRAINT presentaciones_vara_coherente CHECK (((tipo <> 'vara'::public.tipo_presentacion) OR ((paquetes = 1) AND (varas_por_paquete = 1)))),
    CONSTRAINT presentaciones_varas_positivo CHECK ((varas_por_paquete > 0))
);


--
-- Name: presentaciones_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.presentaciones_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: presentaciones_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.presentaciones_id_seq OWNED BY public.presentaciones.id;


--
-- Name: producto_imagenes; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.producto_imagenes (
    producto_id integer NOT NULL,
    archivo text NOT NULL,
    version bigint NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT producto_imagenes_archivo_no_vacio CHECK ((length(btrim(archivo)) > 0))
);


--
-- Name: productos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.productos (
    id integer NOT NULL,
    codigo text NOT NULL,
    nombre text NOT NULL,
    categoria_id integer NOT NULL,
    tipo public.tipo_producto NOT NULL,
    emoji text DEFAULT '🌿'::text NOT NULL,
    precio integer NOT NULL,
    minimo integer DEFAULT 0 NOT NULL,
    costo integer,
    stock integer,
    stock_listo integer,
    costo_armado integer,
    controla_lotes boolean DEFAULT false NOT NULL,
    dias_vida integer,
    activo boolean DEFAULT true NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    precio_ramo numeric(12,2),
    precio_liquidacion numeric(12,2),
    CONSTRAINT productos_dias_vida_positivo CHECK (((dias_vida IS NULL) OR (dias_vida > 0))),
    CONSTRAINT productos_forma_armado CHECK (((tipo <> 'armado'::public.tipo_producto) OR ((stock_listo IS NOT NULL) AND (stock_listo >= 0) AND (costo_armado IS NOT NULL) AND (costo_armado >= 0) AND (costo IS NULL) AND (stock IS NULL)))),
    CONSTRAINT productos_forma_simple CHECK (((tipo <> 'simple'::public.tipo_producto) OR ((costo IS NOT NULL) AND (costo >= 0) AND (stock IS NOT NULL) AND (stock >= 0) AND (stock_listo IS NULL) AND (costo_armado IS NULL)))),
    CONSTRAINT productos_lotes_solo_simples CHECK (((NOT controla_lotes) OR (tipo = 'simple'::public.tipo_producto))),
    CONSTRAINT productos_minimo_no_negativo CHECK ((minimo >= 0)),
    CONSTRAINT productos_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0)),
    CONSTRAINT productos_precio_no_negativo CHECK ((precio >= 0)),
    CONSTRAINT productos_precios_coherentes CHECK ((((precio_ramo IS NULL) OR (precio_ramo <= (precio)::numeric)) AND ((precio_liquidacion IS NULL) OR (precio_liquidacion <= (precio)::numeric))))
);


--
-- Name: COLUMN productos.precio_ramo; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.productos.precio_ramo IS 'Valor de la vara dentro de un arreglo. NULL = no aplica, usar precio.';


--
-- Name: COLUMN productos.precio_liquidacion; Type: COMMENT; Schema: public; Owner: -
--

COMMENT ON COLUMN public.productos.precio_liquidacion IS 'Valor cuando la vara ya no da para vitrina. Qué varas están así lo decide el lote.';


--
-- Name: productos_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.productos_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: productos_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.productos_id_seq OWNED BY public.productos.id;


--
-- Name: promociones; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.promociones (
    id integer NOT NULL,
    nombre text NOT NULL,
    descripcion text,
    tipo public.tipo_promocion NOT NULL,
    valor integer NOT NULL,
    alcance public.alcance_promocion DEFAULT 'boleta'::public.alcance_promocion NOT NULL,
    categoria_id integer,
    producto_id integer,
    minimo integer DEFAULT 0 NOT NULL,
    desde date,
    hasta date,
    dias smallint[] DEFAULT '{}'::smallint[] NOT NULL,
    activa boolean DEFAULT true NOT NULL,
    usos integer DEFAULT 0 NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT promociones_alcance_coherente CHECK ((((alcance = 'boleta'::public.alcance_promocion) AND (categoria_id IS NULL) AND (producto_id IS NULL)) OR ((alcance = 'categoria'::public.alcance_promocion) AND (categoria_id IS NOT NULL) AND (producto_id IS NULL)) OR ((alcance = 'producto'::public.alcance_promocion) AND (producto_id IS NOT NULL) AND (categoria_id IS NULL)))),
    CONSTRAINT promociones_minimo_no_negativo CHECK ((minimo >= 0)),
    CONSTRAINT promociones_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0)),
    CONSTRAINT promociones_porcentaje_valido CHECK (((tipo <> 'porcentaje'::public.tipo_promocion) OR (valor <= 100))),
    CONSTRAINT promociones_usos_no_negativos CHECK ((usos >= 0)),
    CONSTRAINT promociones_valor_positivo CHECK ((valor > 0)),
    CONSTRAINT promociones_vigencia_coherente CHECK (((desde IS NULL) OR (hasta IS NULL) OR (desde <= hasta)))
);


--
-- Name: promociones_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.promociones_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: promociones_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.promociones_id_seq OWNED BY public.promociones.id;


--
-- Name: proveedores; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.proveedores (
    id integer NOT NULL,
    nombre text NOT NULL,
    rut text,
    contacto text,
    telefono text,
    correo text,
    direccion text,
    notas text,
    activo boolean DEFAULT true NOT NULL,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    actualizado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT proveedores_nombre_no_vacio CHECK ((length(TRIM(BOTH FROM nombre)) > 0))
);


--
-- Name: proveedores_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.proveedores_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: proveedores_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.proveedores_id_seq OWNED BY public.proveedores.id;


--
-- Name: puntos_movimientos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.puntos_movimientos (
    id integer NOT NULL,
    cliente_id integer NOT NULL,
    cantidad integer NOT NULL,
    saldo_resultante integer,
    motivo text NOT NULL,
    venta_id integer,
    usuario_id integer,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    CONSTRAINT puntos_cantidad_no_cero CHECK ((cantidad <> 0))
);


--
-- Name: puntos_movimientos_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.puntos_movimientos_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: puntos_movimientos_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.puntos_movimientos_id_seq OWNED BY public.puntos_movimientos.id;


--
-- Name: recetas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.recetas (
    producto_id integer NOT NULL,
    componente_id integer NOT NULL,
    cantidad integer NOT NULL,
    CONSTRAINT recetas_cantidad_positiva CHECK ((cantidad > 0)),
    CONSTRAINT recetas_sin_autoreferencia CHECK ((producto_id <> componente_id))
);


--
-- Name: seq_atencion; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_atencion
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_folio_compra; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_folio_compra
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_folio_cotizacion; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_folio_cotizacion
    START WITH 101
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_folio_venta; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_folio_venta
    START WITH 1001
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_lote; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_lote
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_lote_recuperado; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_lote_recuperado
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: seq_mer_desarme_grupo; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.seq_mer_desarme_grupo
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: usuarios_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.usuarios_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: usuarios_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.usuarios_id_seq OWNED BY public.usuarios.id;


--
-- Name: venta_consumos; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.venta_consumos (
    id integer NOT NULL,
    venta_id integer NOT NULL,
    producto_id integer NOT NULL,
    lote_id integer,
    tipo public.tipo_consumo NOT NULL,
    cantidad integer NOT NULL,
    costo_unitario numeric(12,4),
    CONSTRAINT venta_consumos_cantidad_positiva CHECK ((cantidad > 0))
);


--
-- Name: venta_consumos_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.venta_consumos_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: venta_consumos_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.venta_consumos_id_seq OWNED BY public.venta_consumos.id;


--
-- Name: venta_items; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.venta_items (
    id integer NOT NULL,
    venta_id integer NOT NULL,
    producto_id integer,
    nombre text NOT NULL,
    precio_unitario integer NOT NULL,
    cantidad integer NOT NULL,
    subtotal integer NOT NULL,
    es_servicio boolean DEFAULT false NOT NULL,
    CONSTRAINT venta_items_cantidad_positiva CHECK ((cantidad > 0)),
    CONSTRAINT venta_items_precio_no_negativo CHECK ((precio_unitario >= 0)),
    CONSTRAINT venta_items_servicio_sin_producto CHECK (((NOT es_servicio) OR (producto_id IS NULL))),
    CONSTRAINT venta_items_subtotal_cuadra CHECK ((subtotal = (precio_unitario * cantidad)))
);


--
-- Name: venta_items_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.venta_items_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: venta_items_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.venta_items_id_seq OWNED BY public.venta_items.id;


--
-- Name: ventas; Type: TABLE; Schema: public; Owner: -
--

CREATE TABLE public.ventas (
    id integer NOT NULL,
    folio text NOT NULL,
    numero_atencion text NOT NULL,
    caja_id integer NOT NULL,
    usuario_id integer NOT NULL,
    cliente_id integer,
    promocion_id integer,
    cotizacion_id integer,
    bruto integer NOT NULL,
    descuento_promo integer DEFAULT 0 NOT NULL,
    descuento_manual integer DEFAULT 0 NOT NULL,
    descuento_canje integer DEFAULT 0 NOT NULL,
    descuento_total integer DEFAULT 0 NOT NULL,
    iva_tasa numeric(5,2) DEFAULT 19.00 NOT NULL,
    neto integer NOT NULL,
    iva_monto integer NOT NULL,
    total integer NOT NULL,
    medio_pago public.medio_pago NOT NULL,
    recibido integer,
    vuelto integer,
    autorizado_por text,
    puntos_ganados integer DEFAULT 0 NOT NULL,
    puntos_canjeados integer DEFAULT 0 NOT NULL,
    anulada boolean DEFAULT false NOT NULL,
    motivo_anulacion text,
    anulada_por integer,
    anulada_en timestamp with time zone,
    creado_en timestamp with time zone DEFAULT now() NOT NULL,
    abono_previo integer DEFAULT 0 NOT NULL,
    es_despacho boolean DEFAULT false NOT NULL,
    direccion_despacho text,
    despacho_recibe text,
    despacho_telefono text,
    despacho_valor integer DEFAULT 0 NOT NULL,
    CONSTRAINT ventas_abono_no_negativo CHECK ((abono_previo >= 0)),
    CONSTRAINT ventas_abono_no_supera CHECK ((abono_previo <= (bruto - descuento_total))),
    CONSTRAINT ventas_anulacion_completa CHECK (((NOT anulada) OR ((motivo_anulacion IS NOT NULL) AND (length(TRIM(BOTH FROM motivo_anulacion)) > 0) AND (anulada_en IS NOT NULL)))),
    CONSTRAINT ventas_descuento_cuadra CHECK ((descuento_total = ((descuento_promo + descuento_manual) + descuento_canje))),
    CONSTRAINT ventas_descuento_no_supera_bruto CHECK ((descuento_total <= bruto)),
    CONSTRAINT ventas_desglose_cuadra CHECK ((total = (neto + iva_monto))),
    CONSTRAINT ventas_despacho_valor_no_negativo CHECK ((despacho_valor >= 0)),
    CONSTRAINT ventas_efectivo_alcanza CHECK (((medio_pago <> 'efectivo'::public.medio_pago) OR (recibido IS NULL) OR (recibido >= total))),
    CONSTRAINT ventas_montos_no_negativos CHECK (((bruto >= 0) AND (total >= 0) AND (neto >= 0) AND (iva_monto >= 0) AND (descuento_promo >= 0) AND (descuento_manual >= 0) AND (descuento_canje >= 0))),
    CONSTRAINT ventas_total_cuadra CHECK ((total = ((bruto - descuento_total) - abono_previo)))
);


--
-- Name: ventas_id_seq; Type: SEQUENCE; Schema: public; Owner: -
--

CREATE SEQUENCE public.ventas_id_seq
    AS integer
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


--
-- Name: ventas_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: -
--

ALTER SEQUENCE public.ventas_id_seq OWNED BY public.ventas.id;


--
-- Name: vw_producto_costos; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_producto_costos AS
 SELECT id AS producto_id,
    (
        CASE
            WHEN (tipo = 'simple'::public.tipo_producto) THEN (costo)::bigint
            ELSE (COALESCE(( SELECT sum((c.costo * r.cantidad)) AS sum
               FROM (public.recetas r
                 JOIN public.productos c ON ((c.id = r.componente_id)))
              WHERE (r.producto_id = p.id)), (0)::bigint) + COALESCE(costo_armado, 0))
        END)::integer AS costo_unitario
   FROM public.productos p;


--
-- Name: vw_productos_disponibles; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_productos_disponibles AS
 SELECT p.id,
    p.codigo,
    p.nombre,
    p.tipo,
    p.categoria_id,
    cat.nombre AS categoria,
    p.precio,
    p.minimo,
    p.activo,
    p.stock,
    p.stock_listo,
    co.costo_unitario,
        CASE
            WHEN (p.tipo = 'simple'::public.tipo_producto) THEN p.stock
            ELSE (COALESCE(p.stock_listo, 0) + COALESCE(( SELECT (min(floor(((c.stock)::numeric / (r.cantidad)::numeric))))::integer AS min
               FROM (public.recetas r
                 JOIN public.productos c ON ((c.id = r.componente_id)))
              WHERE (r.producto_id = p.id)), 0))
        END AS disponible,
        CASE
            WHEN (p.tipo = 'armado'::public.tipo_producto) THEN COALESCE(( SELECT (min(floor(((c.stock)::numeric / (r.cantidad)::numeric))))::integer AS min
               FROM (public.recetas r
                 JOIN public.productos c ON ((c.id = r.componente_id)))
              WHERE (r.producto_id = p.id)), 0)
            ELSE NULL::integer
        END AS posibles_de_armar,
        CASE
            WHEN (p.precio > 0) THEN round(((((p.precio - co.costo_unitario))::numeric / (p.precio)::numeric) * (100)::numeric), 1)
            ELSE (0)::numeric
        END AS margen_porcentaje
   FROM ((public.productos p
     JOIN public.categorias cat ON ((cat.id = p.categoria_id)))
     JOIN public.vw_producto_costos co ON ((co.producto_id = p.id)));


--
-- Name: vw_bajo_minimo; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_bajo_minimo AS
 SELECT id,
    codigo,
    nombre,
    tipo,
    categoria_id,
    categoria,
    precio,
    minimo,
    activo,
    stock,
    stock_listo,
    costo_unitario,
    disponible,
    posibles_de_armar,
    margen_porcentaje
   FROM public.vw_productos_disponibles
  WHERE (activo AND (disponible <= minimo))
  ORDER BY disponible, nombre;


--
-- Name: vw_cajas_resumen; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_cajas_resumen AS
 SELECT c.id,
    c.estado,
    c.fondo_inicial,
    c.abierta_en,
    ua.nombre AS abierta_por,
    c.abierta_por AS abierta_por_id,
    c.cerrada_en,
    uc.nombre AS cerrada_por,
    c.nota_cierre,
    c.efectivo_contado,
    COALESCE(v.efectivo, (0)::bigint) AS efectivo,
    COALESCE(v.debito, (0)::bigint) AS debito,
    COALESCE(v.credito, (0)::bigint) AS credito,
    COALESCE(v.transferencia, (0)::bigint) AS transferencia,
    COALESCE(v.total_vendido, (0)::bigint) AS total_vendido,
    COALESCE(v.boletas, (0)::bigint) AS boletas,
    COALESCE(v.anuladas, (0)::bigint) AS anuladas,
    COALESCE(v.descuentos, (0)::bigint) AS total_descuentos,
    COALESCE(v.puntos_otorgados, (0)::bigint) AS puntos_otorgados,
    COALESCE(v.puntos_canjeados, (0)::bigint) AS puntos_canjeados,
    (c.fondo_inicial + COALESCE(v.efectivo, (0)::bigint)) AS en_cajon,
        CASE
            WHEN (c.estado = 'cerrada'::public.estado_caja) THEN (c.efectivo_esperado)::bigint
            ELSE (c.fondo_inicial + COALESCE(v.efectivo, (0)::bigint))
        END AS efectivo_esperado,
    c.diferencia
   FROM (((public.cajas c
     LEFT JOIN public.usuarios ua ON ((ua.id = c.abierta_por)))
     LEFT JOIN public.usuarios uc ON ((uc.id = c.cerrada_por)))
     LEFT JOIN LATERAL ( SELECT sum(ve.total) FILTER (WHERE ((ve.medio_pago = 'efectivo'::public.medio_pago) AND (NOT ve.anulada))) AS efectivo,
            sum(ve.total) FILTER (WHERE ((ve.medio_pago = 'debito'::public.medio_pago) AND (NOT ve.anulada))) AS debito,
            sum(ve.total) FILTER (WHERE ((ve.medio_pago = 'credito'::public.medio_pago) AND (NOT ve.anulada))) AS credito,
            sum(ve.total) FILTER (WHERE ((ve.medio_pago = 'transferencia'::public.medio_pago) AND (NOT ve.anulada))) AS transferencia,
            sum(ve.total) FILTER (WHERE (NOT ve.anulada)) AS total_vendido,
            count(*) FILTER (WHERE (NOT ve.anulada)) AS boletas,
            count(*) FILTER (WHERE ve.anulada) AS anuladas,
            sum(ve.descuento_total) FILTER (WHERE (NOT ve.anulada)) AS descuentos,
            sum(ve.puntos_ganados) FILTER (WHERE (NOT ve.anulada)) AS puntos_otorgados,
            sum(ve.puntos_canjeados) FILTER (WHERE (NOT ve.anulada)) AS puntos_canjeados
           FROM public.ventas ve
          WHERE (ve.caja_id = c.id)) v ON (true));


--
-- Name: vw_clientes; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_clientes AS
 SELECT c.id,
    c.rut,
    c.nombre,
    c.telefono,
    c.correo,
    c.direccion,
    c.cumple_mes,
    c.cumple_dia,
    c.notas,
    c.puntos,
    c.activo,
    c.creado_en,
    c.actualizado_en,
    COALESCE(v.compras, (0)::bigint) AS compras,
    COALESCE(v.total_gastado, (0)::bigint) AS total_gastado,
    v.ultima_compra,
        CASE
            WHEN (v.compras > 0) THEN round(((v.total_gastado)::numeric / (v.compras)::numeric))
            ELSE NULL::numeric
        END AS ticket_promedio,
        CASE
            WHEN (v.ultima_compra IS NOT NULL) THEN (CURRENT_DATE - (v.ultima_compra)::date)
            ELSE NULL::integer
        END AS dias_sin_comprar,
    (c.puntos * COALESCE((public.fn_ven_config('club'::text, 'valorPunto'::text, (10)::numeric))::integer, 10)) AS valor_puntos,
        CASE
            WHEN ((c.cumple_mes IS NOT NULL) AND (c.cumple_dia IS NOT NULL)) THEN (make_date((EXTRACT(year FROM CURRENT_DATE))::integer, (c.cumple_mes)::integer, (c.cumple_dia)::integer) - CURRENT_DATE)
            ELSE NULL::integer
        END AS dias_para_cumple
   FROM (public.clientes c
     LEFT JOIN LATERAL ( SELECT count(*) AS compras,
            sum(ve.total) AS total_gastado,
            max(ve.creado_en) AS ultima_compra
           FROM public.ventas ve
          WHERE ((ve.cliente_id = c.id) AND (NOT ve.anulada))) v ON (true));


--
-- Name: vw_compras_proveedor; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_compras_proveedor AS
 SELECT pr.id AS proveedor_id,
    pr.nombre AS proveedor,
    count(DISTINCT c.id) AS compras,
    sum(c.total) AS total_comprado,
    max(c.fecha) AS ultima_compra,
    count(DISTINCT ci.producto_id) AS productos_distintos
   FROM ((public.proveedores pr
     JOIN public.compras c ON (((c.proveedor_id = pr.id) AND (c.estado = 'recibida'::public.estado_compra))))
     LEFT JOIN public.compra_items ci ON ((ci.compra_id = c.id)))
  GROUP BY pr.id, pr.nombre;


--
-- Name: vw_compromiso_eventos; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_compromiso_eventos AS
 SELECT ci.producto_id,
    p.nombre,
    sum(ci.cantidad) AS comprometido,
    vd.disponible,
    (sum(ci.cantidad) - vd.disponible) AS faltante
   FROM (((public.cotizacion_items ci
     JOIN public.cotizaciones c ON ((c.id = ci.cotizacion_id)))
     JOIN public.productos p ON ((p.id = ci.producto_id)))
     JOIN public.vw_productos_disponibles vd ON ((vd.id = ci.producto_id)))
  WHERE ((c.estado = 'aprobada'::public.estado_cotizacion) AND (ci.producto_id IS NOT NULL))
  GROUP BY ci.producto_id, p.nombre, vd.disponible
 HAVING (sum(ci.cantidad) > vd.disponible);


--
-- Name: vw_costo_promedio; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_costo_promedio AS
 SELECT producto_id,
    sum(varas_disponibles) AS varas,
    (round(sum((costo_por_vara * (varas_disponibles)::numeric))))::integer AS valor_total,
    round((sum((costo_por_vara * (varas_disponibles)::numeric)) / (NULLIF(sum(varas_disponibles), 0))::numeric), 2) AS costo_promedio
   FROM public.lotes l
  WHERE (estado = 'activo'::public.estado_lote)
  GROUP BY producto_id;


--
-- Name: vw_cotizaciones_saldo; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_cotizaciones_saldo AS
 SELECT id,
    folio,
    cliente_nombre,
    tipo_evento,
    fecha_evento,
    estado,
    total,
    abono,
    (total - abono) AS saldo,
        CASE
            WHEN (total > 0) THEN round(((100.0 * (abono)::numeric) / (total)::numeric), 1)
            ELSE (0)::numeric
        END AS porcentaje_pagado,
    (fecha_evento - CURRENT_DATE) AS dias_para_evento,
    ( SELECT count(*) AS count
           FROM public.cotizacion_pagos p
          WHERE ((p.cotizacion_id = c.id) AND (NOT p.anulado))) AS pagos,
    ( SELECT count(*) AS count
           FROM public.cotizacion_cuotas q
          WHERE (q.cotizacion_id = c.id)) AS cuotas,
    COALESCE(( SELECT sum(q.monto) AS sum
           FROM public.cotizacion_cuotas q
          WHERE ((q.cotizacion_id = c.id) AND (q.vence <= CURRENT_DATE))), (
        CASE
            WHEN ((fecha_evento IS NOT NULL) AND (fecha_evento <= CURRENT_DATE)) THEN total
            ELSE 0
        END)::bigint) AS exigible_hoy,
    GREATEST((0)::bigint, (COALESCE(( SELECT sum(q.monto) AS sum
           FROM public.cotizacion_cuotas q
          WHERE ((q.cotizacion_id = c.id) AND (q.vence <= CURRENT_DATE))), (
        CASE
            WHEN ((fecha_evento IS NOT NULL) AND (fecha_evento <= CURRENT_DATE)) THEN total
            ELSE 0
        END)::bigint) - abono)) AS vencido,
    ( SELECT min(q.vence) AS min
           FROM public.cotizacion_cuotas q
          WHERE ((q.cotizacion_id = c.id) AND (q.vence > CURRENT_DATE))) AS proximo_vencimiento
   FROM public.cotizaciones c
  WHERE (estado = ANY (ARRAY['borrador'::public.estado_cotizacion, 'aprobada'::public.estado_cotizacion]));


--
-- Name: vw_evolucion_costo; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_evolucion_costo AS
 SELECT ci.producto_id,
    p.nombre AS producto,
    c.fecha,
    pr.nombre AS proveedor,
    pres.nombre AS presentacion,
    ci.cantidad,
    ci.costo_unitario,
    ci.costo_por_vara,
    lag(ci.costo_por_vara) OVER (PARTITION BY ci.producto_id ORDER BY c.fecha, ci.id) AS costo_anterior,
    round((ci.costo_por_vara - lag(ci.costo_por_vara) OVER (PARTITION BY ci.producto_id ORDER BY c.fecha, ci.id)), 2) AS variacion
   FROM ((((public.compra_items ci
     JOIN public.compras c ON (((c.id = ci.compra_id) AND (c.estado = 'recibida'::public.estado_compra))))
     JOIN public.productos p ON ((p.id = ci.producto_id)))
     JOIN public.proveedores pr ON ((pr.id = c.proveedor_id)))
     LEFT JOIN public.presentaciones pres ON ((pres.id = ci.presentacion_id)));


--
-- Name: vw_lotes_activos; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_lotes_activos AS
 SELECT l.id,
    l.codigo,
    l.producto_id,
    p.nombre AS producto,
    p.emoji,
    pr.nombre AS proveedor,
    pres.nombre AS presentacion,
    l.fecha_ingreso,
    l.fecha_vencimiento,
    (CURRENT_DATE - l.fecha_ingreso) AS dias_en_camara,
    (l.fecha_vencimiento - CURRENT_DATE) AS dias_para_vencer,
    l.varas_iniciales,
    l.varas_disponibles,
    (l.varas_iniciales - l.varas_disponibles) AS varas_consumidas,
    round(((100.0 * ((l.varas_iniciales - l.varas_disponibles))::numeric) / (l.varas_iniciales)::numeric), 1) AS porcentaje_vendido,
    l.costo_por_vara,
    (round((l.costo_por_vara * (l.varas_disponibles)::numeric)))::integer AS valor_restante,
    l.ubicacion,
    l.calidad,
    l.origen_lote_id,
    o.codigo AS lote_origen,
    l.precio_unitario,
    COALESCE(l.precio_unitario, p.precio) AS precio_venta,
    ((l.origen_lote_id IS NOT NULL) OR (l.calidad IS NOT NULL)) AS es_recuperado,
    (l.precio_unitario IS NOT NULL) AS requiere_escaneo,
        CASE
            WHEN (l.precio_unitario IS NULL) THEN row_number() OVER (PARTITION BY l.producto_id, (l.precio_unitario IS NULL) ORDER BY l.fecha_ingreso, l.id)
            ELSE NULL::bigint
        END AS orden_fifo,
        CASE
            WHEN ((l.fecha_vencimiento IS NOT NULL) AND (l.fecha_vencimiento < CURRENT_DATE)) THEN 'vencido'::text
            WHEN ((l.fecha_vencimiento IS NOT NULL) AND (l.fecha_vencimiento <= (CURRENT_DATE + 2))) THEN 'por vencer'::text
            WHEN ((l.varas_disponibles <= 10) AND ((l.varas_disponibles)::numeric < ((l.varas_iniciales)::numeric * 0.3))) THEN 'resto por liquidar'::text
            ELSE 'normal'::text
        END AS alerta
   FROM ((((public.lotes l
     JOIN public.productos p ON ((p.id = l.producto_id)))
     LEFT JOIN public.proveedores pr ON ((pr.id = l.proveedor_id)))
     LEFT JOIN public.presentaciones pres ON ((pres.id = l.presentacion_id)))
     LEFT JOIN public.lotes o ON ((o.id = l.origen_lote_id)))
  WHERE (l.estado = 'activo'::public.estado_lote);


--
-- Name: vw_lotes_calculado; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_lotes_calculado AS
 SELECT l.id,
    l.codigo,
    l.producto_id,
    p.nombre AS producto,
    p.emoji,
    p.precio AS precio_producto,
    pr.nombre AS proveedor,
    pe.nombre AS presentacion,
    c.folio AS compra_folio,
    c.documento,
    l.fecha_ingreso,
    l.fecha_vencimiento,
    l.varas_iniciales,
    l.varas_disponibles,
    l.costo_por_vara,
    l.precio_unitario,
    l.ubicacion,
    l.notas,
    l.estado,
    l.calidad,
    lo.codigo AS lote_origen,
    (CURRENT_DATE - l.fecha_ingreso) AS dias_en_camara,
        CASE
            WHEN (l.fecha_vencimiento IS NOT NULL) THEN (l.fecha_vencimiento - CURRENT_DATE)
            ELSE NULL::integer
        END AS dias_para_vencer,
    round(((((l.varas_iniciales - l.varas_disponibles))::numeric / (NULLIF(l.varas_iniciales, 0))::numeric) * (100)::numeric), 1) AS porcentaje_vendido,
    round(((l.varas_disponibles)::numeric * l.costo_por_vara)) AS valor_restante,
    COALESCE(l.precio_unitario, p.precio) AS precio_venta,
    (p.precio - COALESCE(l.precio_unitario, p.precio)) AS rebaja,
    round(((((p.precio - COALESCE(l.precio_unitario, p.precio)))::numeric / (NULLIF(p.precio, 0))::numeric) * (100)::numeric), 1) AS rebaja_porcentaje,
    (l.origen_lote_id IS NOT NULL) AS es_recuperado,
    ((l.origen_lote_id IS NOT NULL) OR ((l.precio_unitario IS NOT NULL) AND (l.precio_unitario <> p.precio))) AS requiere_escaneo,
    (
        CASE
            WHEN ((l.estado = 'activo'::public.estado_lote) AND (l.varas_disponibles > 0) AND (l.origen_lote_id IS NULL) AND ((l.precio_unitario IS NULL) OR (l.precio_unitario = p.precio))) THEN row_number() OVER (PARTITION BY l.producto_id, ((l.estado = 'activo'::public.estado_lote) AND (l.varas_disponibles > 0) AND (l.origen_lote_id IS NULL) AND ((l.precio_unitario IS NULL) OR (l.precio_unitario = p.precio))) ORDER BY l.fecha_ingreso, l.id)
            ELSE NULL::bigint
        END)::integer AS orden_fifo,
    (EXISTS ( SELECT 1
           FROM public.lotes l2
          WHERE ((l2.producto_id = l.producto_id) AND (l2.id <> l.id) AND (l2.fecha_ingreso > l.fecha_ingreso) AND (l2.varas_disponibles < l2.varas_iniciales)))) AS rezagado
   FROM (((((public.lotes l
     JOIN public.productos p ON ((p.id = l.producto_id)))
     LEFT JOIN public.proveedores pr ON ((pr.id = l.proveedor_id)))
     LEFT JOIN public.presentaciones pe ON ((pe.id = l.presentacion_id)))
     LEFT JOIN public.compras c ON ((c.id = l.compra_id)))
     LEFT JOIN public.lotes lo ON ((lo.id = l.origen_lote_id)));


--
-- Name: vw_lotes_recuperados; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_lotes_recuperados AS
 SELECT la.id,
    la.codigo,
    la.producto_id,
    la.producto,
    la.emoji,
    la.proveedor,
    la.presentacion,
    la.fecha_ingreso,
    la.fecha_vencimiento,
    la.dias_en_camara,
    la.dias_para_vencer,
    la.varas_iniciales,
    la.varas_disponibles,
    la.varas_consumidas,
    la.porcentaje_vendido,
    la.costo_por_vara,
    la.valor_restante,
    la.ubicacion,
    la.calidad,
    la.origen_lote_id,
    la.lote_origen,
    la.precio_unitario,
    la.precio_venta,
    la.es_recuperado,
    la.requiere_escaneo,
    la.orden_fifo,
    la.alerta,
    (p.precio - la.precio_venta) AS rebaja,
        CASE
            WHEN (p.precio > 0) THEN round(((100.0 * ((p.precio - la.precio_venta))::numeric) / (p.precio)::numeric), 1)
            ELSE (0)::numeric
        END AS rebaja_porcentaje
   FROM (public.vw_lotes_activos la
     JOIN public.productos p ON ((p.id = la.producto_id)))
  WHERE la.es_recuperado;


--
-- Name: vw_lotes_rezagados; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_lotes_rezagados AS
 SELECT id,
    codigo,
    producto_id,
    producto,
    emoji,
    proveedor,
    presentacion,
    fecha_ingreso,
    fecha_vencimiento,
    dias_en_camara,
    dias_para_vencer,
    varas_iniciales,
    varas_disponibles,
    varas_consumidas,
    porcentaje_vendido,
    costo_por_vara,
    valor_restante,
    ubicacion,
    calidad,
    origen_lote_id,
    lote_origen,
    precio_unitario,
    precio_venta,
    es_recuperado,
    requiere_escaneo,
    orden_fifo,
    alerta
   FROM public.vw_lotes_activos la
  WHERE ((orden_fifo = 1) AND (NOT es_recuperado) AND (EXISTS ( SELECT 1
           FROM public.lotes l2
          WHERE ((l2.producto_id = la.producto_id) AND (l2.estado = 'activo'::public.estado_lote) AND (l2.precio_unitario IS NULL) AND (l2.id <> la.id)))) AND (varas_disponibles < varas_iniciales))
  ORDER BY dias_en_camara DESC;


--
-- Name: vw_mermas_resultado; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_mermas_resultado AS
 SELECT (date_trunc('day'::text, creado_en))::date AS dia,
    destino,
    count(*) AS registros,
    sum(cantidad) AS unidades_movidas,
    sum(cantidad_recuperada) AS unidades_recuperadas,
    sum((cantidad - cantidad_recuperada)) AS unidades_perdidas,
    sum(costo_total) AS costo_movido,
    sum(costo_perdido) AS costo_perdido,
    sum((costo_total - costo_perdido)) AS costo_recuperado,
    sum((costo_unitario * (cantidad - cantidad_recuperada))) AS costo_botado,
    sum(((costo_unitario - COALESCE(costo_recuperado_unitario, costo_unitario)) * cantidad_recuperada)) AS costo_desvalorizado
   FROM public.mermas m
  WHERE (NOT revertida)
  GROUP BY ((date_trunc('day'::text, creado_en))::date), destino;


--
-- Name: vw_mostrador; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_mostrador AS
 SELECT producto_id,
    (sum(cantidad_disponible))::integer AS stock,
    (count(*))::integer AS partidas,
    min(fecha_vencimiento) AS vence_primero,
    min(traspasado_en) AS bajado_en
   FROM public.partidas_mostrador pm
  WHERE (cantidad_disponible > 0)
  GROUP BY producto_id;


--
-- Name: vw_partidas; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_partidas AS
 SELECT pm.id,
    pm.codigo,
    pm.producto_id,
    p.nombre AS producto,
    p.emoji,
    p.precio AS precio_producto,
    p.precio_ramo,
    p.precio_liquidacion,
    pm.lote_id,
    l.codigo AS lote_codigo,
    pm.cantidad_inicial,
    pm.cantidad_disponible,
    pm.precio_unitario,
    pm.costo_por_vara,
    pm.fecha_vencimiento,
    pm.traspasado_en,
    u.nombre AS traspasado_por,
    pm.agotada_en,
    pm.notas,
    (CURRENT_DATE - (pm.traspasado_en)::date) AS "dias_en_mesón",
        CASE
            WHEN (pm.fecha_vencimiento IS NOT NULL) THEN (pm.fecha_vencimiento - CURRENT_DATE)
            ELSE NULL::integer
        END AS dias_para_vencer,
    ((((((('CLB1*'::text || pm.id) || '*'::text) || pm.producto_id) || '*'::text) || to_char(pm.traspasado_en, 'YYYYMMDD'::text)) || '*'::text) || pm.codigo) AS qr,
    ((pm.cantidad_disponible > 0) AND p.activo AND COALESCE((pm.fecha_vencimiento >= CURRENT_DATE), true)) AS vendible
   FROM (((public.partidas_mostrador pm
     JOIN public.productos p ON ((p.id = pm.producto_id)))
     LEFT JOIN public.lotes l ON ((l.id = pm.lote_id)))
     LEFT JOIN public.usuarios u ON ((u.id = pm.traspasado_por)));


--
-- Name: vw_resultado_diario; Type: VIEW; Schema: public; Owner: -
--

CREATE VIEW public.vw_resultado_diario AS
 WITH ventas_dia AS (
         SELECT (date_trunc('day'::text, ventas.creado_en))::date AS dia,
            count(*) AS boletas,
            sum(ventas.bruto) AS bruto,
            sum(ventas.descuento_total) AS descuentos,
            sum(ventas.total) AS ingresos,
            sum(ventas.neto) AS neto,
            sum(ventas.iva_monto) AS iva
           FROM public.ventas
          WHERE (NOT ventas.anulada)
          GROUP BY ((date_trunc('day'::text, ventas.creado_en))::date)
        ), costos_dia AS (
         SELECT (date_trunc('day'::text, v_1.creado_en))::date AS dia,
            sum((vi.cantidad * co.costo_unitario)) AS costo_vendido
           FROM ((public.venta_items vi
             JOIN public.ventas v_1 ON ((v_1.id = vi.venta_id)))
             JOIN public.vw_producto_costos co ON ((co.producto_id = vi.producto_id)))
          WHERE (NOT v_1.anulada)
          GROUP BY ((date_trunc('day'::text, v_1.creado_en))::date)
        ), mermas_dia AS (
         SELECT (date_trunc('day'::text, mermas.creado_en))::date AS dia,
            sum(mermas.costo_total) AS mermas
           FROM public.mermas
          WHERE (NOT mermas.revertida)
          GROUP BY ((date_trunc('day'::text, mermas.creado_en))::date)
        ), dias AS (
         SELECT ventas_dia.dia
           FROM ventas_dia
        UNION
         SELECT mermas_dia.dia
           FROM mermas_dia
        )
 SELECT d.dia,
    COALESCE(v.boletas, (0)::bigint) AS boletas,
    COALESCE(v.bruto, (0)::bigint) AS bruto,
    COALESCE(v.descuentos, (0)::bigint) AS descuentos,
    COALESCE(v.ingresos, (0)::bigint) AS ingresos,
    COALESCE(v.neto, (0)::bigint) AS neto,
    COALESCE(v.iva, (0)::bigint) AS iva,
    COALESCE(c.costo_vendido, (0)::bigint) AS costo_vendido,
    COALESCE(m.mermas, (0)::bigint) AS mermas,
    (COALESCE(v.ingresos, (0)::bigint) - COALESCE(c.costo_vendido, (0)::bigint)) AS utilidad_bruta,
    ((COALESCE(v.ingresos, (0)::bigint) - COALESCE(c.costo_vendido, (0)::bigint)) - COALESCE(m.mermas, (0)::bigint)) AS resultado
   FROM (((dias d
     LEFT JOIN ventas_dia v ON ((v.dia = d.dia)))
     LEFT JOIN costos_dia c ON ((c.dia = d.dia)))
     LEFT JOIN mermas_dia m ON ((m.dia = d.dia)))
  ORDER BY d.dia DESC;


--
-- Name: cajas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cajas ALTER COLUMN id SET DEFAULT nextval('public.cajas_id_seq'::regclass);


--
-- Name: categorias id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categorias ALTER COLUMN id SET DEFAULT nextval('public.categorias_id_seq'::regclass);


--
-- Name: clientes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.clientes ALTER COLUMN id SET DEFAULT nextval('public.clientes_id_seq'::regclass);


--
-- Name: codigos_autorizacion_descuento id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_descuento ALTER COLUMN id SET DEFAULT nextval('public.codigos_autorizacion_descuento_id_seq'::regclass);


--
-- Name: codigos_autorizacion_merma id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_merma ALTER COLUMN id SET DEFAULT nextval('public.codigos_autorizacion_merma_id_seq'::regclass);


--
-- Name: compra_items id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compra_items ALTER COLUMN id SET DEFAULT nextval('public.compra_items_id_seq'::regclass);


--
-- Name: compras id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compras ALTER COLUMN id SET DEFAULT nextval('public.compras_id_seq'::regclass);


--
-- Name: conteo_lineas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas ALTER COLUMN id SET DEFAULT nextval('public.conteo_lineas_id_seq'::regclass);


--
-- Name: conteos id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteos ALTER COLUMN id SET DEFAULT nextval('public.conteos_id_seq'::regclass);


--
-- Name: cotizacion_cuotas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_cuotas ALTER COLUMN id SET DEFAULT nextval('public.cotizacion_cuotas_id_seq'::regclass);


--
-- Name: cotizacion_items id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_items ALTER COLUMN id SET DEFAULT nextval('public.cotizacion_items_id_seq'::regclass);


--
-- Name: cotizacion_pagos id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_pagos ALTER COLUMN id SET DEFAULT nextval('public.cotizacion_pagos_id_seq'::regclass);


--
-- Name: cotizaciones id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones ALTER COLUMN id SET DEFAULT nextval('public.cotizaciones_id_seq'::regclass);


--
-- Name: lotes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes ALTER COLUMN id SET DEFAULT nextval('public.lotes_id_seq'::regclass);


--
-- Name: merma_evidencias id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_evidencias ALTER COLUMN id SET DEFAULT nextval('public.merma_evidencias_id_seq'::regclass);


--
-- Name: merma_reportes id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes ALTER COLUMN id SET DEFAULT nextval('public.merma_reportes_id_seq'::regclass);


--
-- Name: mermas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas ALTER COLUMN id SET DEFAULT nextval('public.mermas_id_seq'::regclass);


--
-- Name: motivos_merma id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.motivos_merma ALTER COLUMN id SET DEFAULT nextval('public.motivos_merma_id_seq'::regclass);


--
-- Name: movimientos_inventario id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.movimientos_inventario ALTER COLUMN id SET DEFAULT nextval('public.movimientos_inventario_id_seq'::regclass);


--
-- Name: partidas_mostrador id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador ALTER COLUMN id SET DEFAULT nextval('public.partidas_mostrador_id_seq'::regclass);


--
-- Name: presentaciones id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.presentaciones ALTER COLUMN id SET DEFAULT nextval('public.presentaciones_id_seq'::regclass);


--
-- Name: productos id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.productos ALTER COLUMN id SET DEFAULT nextval('public.productos_id_seq'::regclass);


--
-- Name: promociones id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promociones ALTER COLUMN id SET DEFAULT nextval('public.promociones_id_seq'::regclass);


--
-- Name: proveedores id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.proveedores ALTER COLUMN id SET DEFAULT nextval('public.proveedores_id_seq'::regclass);


--
-- Name: puntos_movimientos id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.puntos_movimientos ALTER COLUMN id SET DEFAULT nextval('public.puntos_movimientos_id_seq'::regclass);


--
-- Name: usuarios id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuarios ALTER COLUMN id SET DEFAULT nextval('public.usuarios_id_seq'::regclass);


--
-- Name: venta_consumos id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos ALTER COLUMN id SET DEFAULT nextval('public.venta_consumos_id_seq'::regclass);


--
-- Name: venta_items id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_items ALTER COLUMN id SET DEFAULT nextval('public.venta_items_id_seq'::regclass);


--
-- Name: ventas id; Type: DEFAULT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas ALTER COLUMN id SET DEFAULT nextval('public.ventas_id_seq'::regclass);


--
-- Name: cajas cajas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cajas
    ADD CONSTRAINT cajas_pkey PRIMARY KEY (id);


--
-- Name: categorias categorias_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categorias
    ADD CONSTRAINT categorias_nombre_key UNIQUE (nombre);


--
-- Name: categorias categorias_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.categorias
    ADD CONSTRAINT categorias_pkey PRIMARY KEY (id);


--
-- Name: clientes clientes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.clientes
    ADD CONSTRAINT clientes_pkey PRIMARY KEY (id);


--
-- Name: codigos_autorizacion_descuento codigos_autorizacion_descuento_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_descuento
    ADD CONSTRAINT codigos_autorizacion_descuento_pkey PRIMARY KEY (id);


--
-- Name: codigos_autorizacion_merma codigos_autorizacion_merma_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_merma
    ADD CONSTRAINT codigos_autorizacion_merma_pkey PRIMARY KEY (id);


--
-- Name: compra_items compra_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compra_items
    ADD CONSTRAINT compra_items_pkey PRIMARY KEY (id);


--
-- Name: compras compras_folio_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compras
    ADD CONSTRAINT compras_folio_key UNIQUE (folio);


--
-- Name: compras compras_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compras
    ADD CONSTRAINT compras_pkey PRIMARY KEY (id);


--
-- Name: configuracion configuracion_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.configuracion
    ADD CONSTRAINT configuracion_pkey PRIMARY KEY (clave);


--
-- Name: conteo_lineas conteo_lineas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_pkey PRIMARY KEY (id);


--
-- Name: conteos conteos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteos
    ADD CONSTRAINT conteos_pkey PRIMARY KEY (id);


--
-- Name: cotizacion_cuotas cotizacion_cuotas_cotizacion_id_numero_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_cuotas
    ADD CONSTRAINT cotizacion_cuotas_cotizacion_id_numero_key UNIQUE (cotizacion_id, numero);


--
-- Name: cotizacion_cuotas cotizacion_cuotas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_cuotas
    ADD CONSTRAINT cotizacion_cuotas_pkey PRIMARY KEY (id);


--
-- Name: cotizacion_items cotizacion_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_items
    ADD CONSTRAINT cotizacion_items_pkey PRIMARY KEY (id);


--
-- Name: cotizacion_pagos cotizacion_pagos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_pagos
    ADD CONSTRAINT cotizacion_pagos_pkey PRIMARY KEY (id);


--
-- Name: cotizaciones cotizaciones_folio_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_folio_key UNIQUE (folio);


--
-- Name: cotizaciones cotizaciones_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_pkey PRIMARY KEY (id);


--
-- Name: inventario_venta inventario_venta_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventario_venta
    ADD CONSTRAINT inventario_venta_pkey PRIMARY KEY (producto_id);


--
-- Name: lotes lotes_codigo_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_codigo_key UNIQUE (codigo);


--
-- Name: lotes lotes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_pkey PRIMARY KEY (id);


--
-- Name: merma_evidencias merma_evidencias_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_evidencias
    ADD CONSTRAINT merma_evidencias_pkey PRIMARY KEY (id);


--
-- Name: merma_reportes merma_reportes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes
    ADD CONSTRAINT merma_reportes_pkey PRIMARY KEY (id);


--
-- Name: mermas mermas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_pkey PRIMARY KEY (id);


--
-- Name: motivos_merma motivos_merma_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.motivos_merma
    ADD CONSTRAINT motivos_merma_pkey PRIMARY KEY (id);


--
-- Name: movimientos_inventario movimientos_inventario_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.movimientos_inventario
    ADD CONSTRAINT movimientos_inventario_pkey PRIMARY KEY (id);


--
-- Name: partidas_mostrador partidas_mostrador_codigo_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador
    ADD CONSTRAINT partidas_mostrador_codigo_key UNIQUE (codigo);


--
-- Name: partidas_mostrador partidas_mostrador_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador
    ADD CONSTRAINT partidas_mostrador_pkey PRIMARY KEY (id);


--
-- Name: presentaciones presentaciones_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.presentaciones
    ADD CONSTRAINT presentaciones_pkey PRIMARY KEY (id);


--
-- Name: presentaciones presentaciones_producto_id_nombre_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.presentaciones
    ADD CONSTRAINT presentaciones_producto_id_nombre_key UNIQUE (producto_id, nombre);


--
-- Name: producto_imagenes producto_imagenes_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.producto_imagenes
    ADD CONSTRAINT producto_imagenes_pkey PRIMARY KEY (producto_id);


--
-- Name: productos productos_codigo_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.productos
    ADD CONSTRAINT productos_codigo_key UNIQUE (codigo);


--
-- Name: productos productos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.productos
    ADD CONSTRAINT productos_pkey PRIMARY KEY (id);


--
-- Name: promociones promociones_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promociones
    ADD CONSTRAINT promociones_pkey PRIMARY KEY (id);


--
-- Name: proveedores proveedores_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.proveedores
    ADD CONSTRAINT proveedores_pkey PRIMARY KEY (id);


--
-- Name: puntos_movimientos puntos_movimientos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.puntos_movimientos
    ADD CONSTRAINT puntos_movimientos_pkey PRIMARY KEY (id);


--
-- Name: recetas recetas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recetas
    ADD CONSTRAINT recetas_pkey PRIMARY KEY (producto_id, componente_id);


--
-- Name: usuarios usuarios_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.usuarios
    ADD CONSTRAINT usuarios_pkey PRIMARY KEY (id);


--
-- Name: venta_consumos venta_consumos_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos
    ADD CONSTRAINT venta_consumos_pkey PRIMARY KEY (id);


--
-- Name: venta_consumos venta_consumos_unico; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos
    ADD CONSTRAINT venta_consumos_unico UNIQUE (venta_id, producto_id, tipo, lote_id);


--
-- Name: venta_items venta_items_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_items
    ADD CONSTRAINT venta_items_pkey PRIMARY KEY (id);


--
-- Name: ventas ventas_folio_key; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_folio_key UNIQUE (folio);


--
-- Name: ventas ventas_pkey; Type: CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_pkey PRIMARY KEY (id);


--
-- Name: cajas_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX cajas_fecha_idx ON public.cajas USING btree (abierta_en DESC);


--
-- Name: cajas_una_abierta; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX cajas_una_abierta ON public.cajas USING btree (estado) WHERE (estado = 'abierta'::public.estado_caja);


--
-- Name: cajas_una_sola_abierta; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX cajas_una_sola_abierta ON public.cajas USING btree (estado) WHERE (estado = 'abierta'::public.estado_caja);


--
-- Name: clientes_cumple_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX clientes_cumple_idx ON public.clientes USING btree (cumple_mes, cumple_dia) WHERE activo;


--
-- Name: clientes_nombre_busqueda_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX clientes_nombre_busqueda_idx ON public.clientes USING gin (nombre public.gin_trgm_ops);


--
-- Name: clientes_rut_norm_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX clientes_rut_norm_idx ON public.clientes USING btree (public.fn_cli_normalizar_rut(rut));


--
-- Name: clientes_rut_unico; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX clientes_rut_unico ON public.clientes USING btree (upper(replace(replace(rut, '.'::text, ''::text), '-'::text, ''::text)));


--
-- Name: compra_items_compra_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX compra_items_compra_idx ON public.compra_items USING btree (compra_id);


--
-- Name: compra_items_producto_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX compra_items_producto_idx ON public.compra_items USING btree (producto_id);


--
-- Name: compras_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX compras_fecha_idx ON public.compras USING btree (fecha DESC);


--
-- Name: compras_proveedor_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX compras_proveedor_idx ON public.compras USING btree (proveedor_id, fecha DESC);


--
-- Name: cotizacion_items_cotizacion_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX cotizacion_items_cotizacion_idx ON public.cotizacion_items USING btree (cotizacion_id);


--
-- Name: cotizaciones_estado_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX cotizaciones_estado_idx ON public.cotizaciones USING btree (estado);


--
-- Name: cotizaciones_fecha_evento_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX cotizaciones_fecha_evento_idx ON public.cotizaciones USING btree (fecha_evento) WHERE (estado = 'aprobada'::public.estado_cotizacion);


--
-- Name: cuotas_vence_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX cuotas_vence_idx ON public.cotizacion_cuotas USING btree (vence);


--
-- Name: ix_cod_aut_codigo; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cod_aut_codigo ON public.codigos_autorizacion_descuento USING btree (codigo) WHERE (NOT usado);


--
-- Name: ix_cod_aut_merma_usuario; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_cod_aut_merma_usuario ON public.codigos_autorizacion_merma USING btree (usuario_id, codigo) WHERE (NOT usado);


--
-- Name: ix_conteo_lineas_conteo; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_conteo_lineas_conteo ON public.conteo_lineas USING btree (conteo_id);


--
-- Name: ix_conteos_fecha; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_conteos_fecha ON public.conteos USING btree (creado_en DESC);


--
-- Name: ix_merma_evidencias_reporte; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_merma_evidencias_reporte ON public.merma_evidencias USING btree (reporte_id);


--
-- Name: ix_merma_evidencias_sha; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_merma_evidencias_sha ON public.merma_evidencias USING btree (sha256);


--
-- Name: ix_merma_reportes_pendientes; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_merma_reportes_pendientes ON public.merma_reportes USING btree (creado_en DESC) WHERE ((revisado_en IS NULL) AND (deshecho_en IS NULL));


--
-- Name: ix_mermas_desarme_grupo; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_mermas_desarme_grupo ON public.mermas USING btree (desarme_grupo) WHERE (desarme_grupo IS NOT NULL);


--
-- Name: ix_mermas_reporte; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ix_mermas_reporte ON public.mermas USING btree (reporte_id);


--
-- Name: lotes_compra_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX lotes_compra_idx ON public.lotes USING btree (compra_id);


--
-- Name: lotes_fifo_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX lotes_fifo_idx ON public.lotes USING btree (producto_id, fecha_ingreso, id) WHERE (estado = 'activo'::public.estado_lote);


--
-- Name: lotes_origen_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX lotes_origen_idx ON public.lotes USING btree (origen_lote_id) WHERE (origen_lote_id IS NOT NULL);


--
-- Name: lotes_vencimiento_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX lotes_vencimiento_idx ON public.lotes USING btree (fecha_vencimiento) WHERE ((estado = 'activo'::public.estado_lote) AND (fecha_vencimiento IS NOT NULL));


--
-- Name: mermas_destino_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX mermas_destino_idx ON public.mermas USING btree (destino) WHERE (NOT revertida);


--
-- Name: mermas_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX mermas_fecha_idx ON public.mermas USING btree (creado_en DESC);


--
-- Name: mermas_lote_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX mermas_lote_idx ON public.mermas USING btree (lote_id);


--
-- Name: mermas_producto_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX mermas_producto_idx ON public.mermas USING btree (producto_id) WHERE (NOT revertida);


--
-- Name: mermas_usuario_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX mermas_usuario_fecha_idx ON public.mermas USING btree (usuario_id, creado_en) WHERE (NOT revertida);


--
-- Name: movimientos_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX movimientos_fecha_idx ON public.movimientos_inventario USING btree (creado_en DESC);


--
-- Name: movimientos_lote_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX movimientos_lote_idx ON public.movimientos_inventario USING btree (lote_id);


--
-- Name: movimientos_producto_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX movimientos_producto_fecha_idx ON public.movimientos_inventario USING btree (producto_id, creado_en DESC);


--
-- Name: movimientos_referencia_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX movimientos_referencia_idx ON public.movimientos_inventario USING btree (referencia_tipo, referencia_id);


--
-- Name: pagos_cotizacion_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pagos_cotizacion_idx ON public.cotizacion_pagos USING btree (cotizacion_id, fecha DESC);


--
-- Name: pagos_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pagos_fecha_idx ON public.cotizacion_pagos USING btree (fecha DESC) WHERE (NOT anulado);


--
-- Name: pagos_venta_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX pagos_venta_idx ON public.cotizacion_pagos USING btree (venta_id);


--
-- Name: partidas_lote_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX partidas_lote_idx ON public.partidas_mostrador USING btree (lote_id);


--
-- Name: partidas_producto_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX partidas_producto_idx ON public.partidas_mostrador USING btree (producto_id, traspasado_en) WHERE (cantidad_disponible > 0);


--
-- Name: presentaciones_producto_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX presentaciones_producto_idx ON public.presentaciones USING btree (producto_id) WHERE activa;


--
-- Name: presentaciones_una_predeterminada; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX presentaciones_una_predeterminada ON public.presentaciones USING btree (producto_id) WHERE predeterminada;


--
-- Name: productos_categoria_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX productos_categoria_idx ON public.productos USING btree (categoria_id);


--
-- Name: productos_nombre_busqueda_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX productos_nombre_busqueda_idx ON public.productos USING gin (nombre public.gin_trgm_ops);


--
-- Name: productos_tipo_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX productos_tipo_idx ON public.productos USING btree (tipo) WHERE activo;


--
-- Name: promociones_activas_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX promociones_activas_idx ON public.promociones USING btree (activa) WHERE activa;


--
-- Name: proveedores_rut_unico; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX proveedores_rut_unico ON public.proveedores USING btree (upper(replace(replace(rut, '.'::text, ''::text), '-'::text, ''::text))) WHERE (rut IS NOT NULL);


--
-- Name: puntos_cliente_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX puntos_cliente_idx ON public.puntos_movimientos USING btree (cliente_id, creado_en DESC);


--
-- Name: recetas_componente_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX recetas_componente_idx ON public.recetas USING btree (componente_id);


--
-- Name: recetas_producto_componente_uq; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX recetas_producto_componente_uq ON public.recetas USING btree (producto_id, componente_id);


--
-- Name: usuarios_email_unico; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX usuarios_email_unico ON public.usuarios USING btree (lower(email));


--
-- Name: usuarios_rol_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX usuarios_rol_idx ON public.usuarios USING btree (rol) WHERE activo;


--
-- Name: ux_motivos_merma_nombre; Type: INDEX; Schema: public; Owner: -
--

CREATE UNIQUE INDEX ux_motivos_merma_nombre ON public.motivos_merma USING btree (lower(btrim(nombre)));


--
-- Name: venta_consumos_lote_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX venta_consumos_lote_idx ON public.venta_consumos USING btree (lote_id);


--
-- Name: venta_consumos_venta_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX venta_consumos_venta_idx ON public.venta_consumos USING btree (venta_id);


--
-- Name: venta_items_producto_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX venta_items_producto_idx ON public.venta_items USING btree (producto_id);


--
-- Name: venta_items_venta_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX venta_items_venta_idx ON public.venta_items USING btree (venta_id);


--
-- Name: ventas_caja_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ventas_caja_idx ON public.ventas USING btree (caja_id) WHERE (NOT anulada);


--
-- Name: ventas_cliente_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ventas_cliente_idx ON public.ventas USING btree (cliente_id) WHERE (cliente_id IS NOT NULL);


--
-- Name: ventas_fecha_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ventas_fecha_idx ON public.ventas USING btree (creado_en DESC);


--
-- Name: ventas_usuario_idx; Type: INDEX; Schema: public; Owner: -
--

CREATE INDEX ventas_usuario_idx ON public.ventas USING btree (usuario_id, creado_en DESC);


--
-- Name: lotes tg_lot_precio_por_definir; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER tg_lot_precio_por_definir BEFORE INSERT OR UPDATE OF precio_unitario ON public.lotes FOR EACH ROW EXECUTE FUNCTION public.fn_lot_precio_por_definir();


--
-- Name: venta_items tg_ven_item_con_precio; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER tg_ven_item_con_precio BEFORE INSERT ON public.venta_items FOR EACH ROW EXECUTE FUNCTION public.fn_ven_item_con_precio();


--
-- Name: partidas_mostrador tg_ven_partida_con_precio; Type: TRIGGER; Schema: public; Owner: -
--

CREATE TRIGGER tg_ven_partida_con_precio BEFORE INSERT ON public.partidas_mostrador FOR EACH ROW EXECUTE FUNCTION public.fn_ven_partida_con_precio();


--
-- Name: cajas cajas_abierta_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cajas
    ADD CONSTRAINT cajas_abierta_por_fkey FOREIGN KEY (abierta_por) REFERENCES public.usuarios(id) ON DELETE RESTRICT;


--
-- Name: cajas cajas_cerrada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cajas
    ADD CONSTRAINT cajas_cerrada_por_fkey FOREIGN KEY (cerrada_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: codigos_autorizacion_descuento codigos_autorizacion_descuento_vendedor_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_descuento
    ADD CONSTRAINT codigos_autorizacion_descuento_vendedor_id_fkey FOREIGN KEY (vendedor_id) REFERENCES public.usuarios(id);


--
-- Name: codigos_autorizacion_merma codigos_autorizacion_merma_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.codigos_autorizacion_merma
    ADD CONSTRAINT codigos_autorizacion_merma_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id);


--
-- Name: compra_items compra_items_compra_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compra_items
    ADD CONSTRAINT compra_items_compra_id_fkey FOREIGN KEY (compra_id) REFERENCES public.compras(id) ON DELETE CASCADE;


--
-- Name: compra_items compra_items_presentacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compra_items
    ADD CONSTRAINT compra_items_presentacion_id_fkey FOREIGN KEY (presentacion_id) REFERENCES public.presentaciones(id) ON DELETE RESTRICT;


--
-- Name: compra_items compra_items_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compra_items
    ADD CONSTRAINT compra_items_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: compras compras_proveedor_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compras
    ADD CONSTRAINT compras_proveedor_id_fkey FOREIGN KEY (proveedor_id) REFERENCES public.proveedores(id) ON DELETE RESTRICT;


--
-- Name: compras compras_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.compras
    ADD CONSTRAINT compras_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: configuracion configuracion_actualizado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.configuracion
    ADD CONSTRAINT configuracion_actualizado_por_fkey FOREIGN KEY (actualizado_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: conteo_lineas conteo_lineas_conteo_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_conteo_id_fkey FOREIGN KEY (conteo_id) REFERENCES public.conteos(id);


--
-- Name: conteo_lineas conteo_lineas_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_lote_id_fkey FOREIGN KEY (lote_id) REFERENCES public.lotes(id);


--
-- Name: conteo_lineas conteo_lineas_merma_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_merma_id_fkey FOREIGN KEY (merma_id) REFERENCES public.mermas(id);


--
-- Name: conteo_lineas conteo_lineas_partida_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_partida_id_fkey FOREIGN KEY (partida_id) REFERENCES public.partidas_mostrador(id);


--
-- Name: conteo_lineas conteo_lineas_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteo_lineas
    ADD CONSTRAINT conteo_lineas_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id);


--
-- Name: conteos conteos_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.conteos
    ADD CONSTRAINT conteos_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id);


--
-- Name: cotizacion_cuotas cotizacion_cuotas_cotizacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_cuotas
    ADD CONSTRAINT cotizacion_cuotas_cotizacion_id_fkey FOREIGN KEY (cotizacion_id) REFERENCES public.cotizaciones(id) ON DELETE CASCADE;


--
-- Name: cotizacion_items cotizacion_items_cotizacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_items
    ADD CONSTRAINT cotizacion_items_cotizacion_id_fkey FOREIGN KEY (cotizacion_id) REFERENCES public.cotizaciones(id) ON DELETE CASCADE;


--
-- Name: cotizacion_items cotizacion_items_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_items
    ADD CONSTRAINT cotizacion_items_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE SET NULL;


--
-- Name: cotizacion_pagos cotizacion_pagos_cotizacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_pagos
    ADD CONSTRAINT cotizacion_pagos_cotizacion_id_fkey FOREIGN KEY (cotizacion_id) REFERENCES public.cotizaciones(id) ON DELETE CASCADE;


--
-- Name: cotizacion_pagos cotizacion_pagos_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_pagos
    ADD CONSTRAINT cotizacion_pagos_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: cotizacion_pagos cotizacion_pagos_venta_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizacion_pagos
    ADD CONSTRAINT cotizacion_pagos_venta_id_fkey FOREIGN KEY (venta_id) REFERENCES public.ventas(id) ON DELETE SET NULL;


--
-- Name: cotizaciones cotizaciones_anulada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_anulada_por_fkey FOREIGN KEY (anulada_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: cotizaciones cotizaciones_cliente_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_cliente_id_fkey FOREIGN KEY (cliente_id) REFERENCES public.clientes(id) ON DELETE SET NULL;


--
-- Name: cotizaciones cotizaciones_creada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_creada_por_fkey FOREIGN KEY (creada_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: cotizaciones cotizaciones_venta_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.cotizaciones
    ADD CONSTRAINT cotizaciones_venta_id_fkey FOREIGN KEY (venta_id) REFERENCES public.ventas(id) ON DELETE SET NULL;


--
-- Name: inventario_venta inventario_venta_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.inventario_venta
    ADD CONSTRAINT inventario_venta_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE CASCADE;


--
-- Name: lotes lotes_compra_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_compra_id_fkey FOREIGN KEY (compra_id) REFERENCES public.compras(id) ON DELETE SET NULL;


--
-- Name: lotes lotes_compra_item_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_compra_item_id_fkey FOREIGN KEY (compra_item_id) REFERENCES public.compra_items(id) ON DELETE SET NULL;


--
-- Name: lotes lotes_origen_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_origen_lote_id_fkey FOREIGN KEY (origen_lote_id) REFERENCES public.lotes(id) ON DELETE SET NULL;


--
-- Name: lotes lotes_presentacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_presentacion_id_fkey FOREIGN KEY (presentacion_id) REFERENCES public.presentaciones(id) ON DELETE SET NULL;


--
-- Name: lotes lotes_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: lotes lotes_proveedor_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.lotes
    ADD CONSTRAINT lotes_proveedor_id_fkey FOREIGN KEY (proveedor_id) REFERENCES public.proveedores(id) ON DELETE SET NULL;


--
-- Name: merma_evidencias merma_evidencias_reporte_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_evidencias
    ADD CONSTRAINT merma_evidencias_reporte_id_fkey FOREIGN KEY (reporte_id) REFERENCES public.merma_reportes(id);


--
-- Name: merma_reportes merma_reportes_deshecho_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes
    ADD CONSTRAINT merma_reportes_deshecho_por_fkey FOREIGN KEY (deshecho_por) REFERENCES public.usuarios(id);


--
-- Name: merma_reportes merma_reportes_motivo_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes
    ADD CONSTRAINT merma_reportes_motivo_id_fkey FOREIGN KEY (motivo_id) REFERENCES public.motivos_merma(id);


--
-- Name: merma_reportes merma_reportes_revisado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes
    ADD CONSTRAINT merma_reportes_revisado_por_fkey FOREIGN KEY (revisado_por) REFERENCES public.usuarios(id);


--
-- Name: merma_reportes merma_reportes_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.merma_reportes
    ADD CONSTRAINT merma_reportes_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id);


--
-- Name: mermas mermas_desarme_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_desarme_producto_id_fkey FOREIGN KEY (desarme_producto_id) REFERENCES public.productos(id);


--
-- Name: mermas mermas_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_lote_id_fkey FOREIGN KEY (lote_id) REFERENCES public.lotes(id) ON DELETE SET NULL;


--
-- Name: mermas mermas_lote_recuperacion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_lote_recuperacion_id_fkey FOREIGN KEY (lote_recuperacion_id) REFERENCES public.lotes(id) ON DELETE SET NULL;


--
-- Name: mermas mermas_motivo_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_motivo_id_fkey FOREIGN KEY (motivo_id) REFERENCES public.motivos_merma(id);


--
-- Name: mermas mermas_partida_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_partida_id_fkey FOREIGN KEY (partida_id) REFERENCES public.partidas_mostrador(id);


--
-- Name: mermas mermas_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: mermas mermas_reporte_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_reporte_id_fkey FOREIGN KEY (reporte_id) REFERENCES public.merma_reportes(id);


--
-- Name: mermas mermas_revertida_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_revertida_por_fkey FOREIGN KEY (revertida_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: mermas mermas_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.mermas
    ADD CONSTRAINT mermas_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: movimientos_inventario movimientos_inventario_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.movimientos_inventario
    ADD CONSTRAINT movimientos_inventario_lote_id_fkey FOREIGN KEY (lote_id) REFERENCES public.lotes(id) ON DELETE SET NULL;


--
-- Name: movimientos_inventario movimientos_inventario_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.movimientos_inventario
    ADD CONSTRAINT movimientos_inventario_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: movimientos_inventario movimientos_inventario_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.movimientos_inventario
    ADD CONSTRAINT movimientos_inventario_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: partidas_mostrador partidas_mostrador_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador
    ADD CONSTRAINT partidas_mostrador_lote_id_fkey FOREIGN KEY (lote_id) REFERENCES public.lotes(id) ON DELETE RESTRICT;


--
-- Name: partidas_mostrador partidas_mostrador_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador
    ADD CONSTRAINT partidas_mostrador_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: partidas_mostrador partidas_mostrador_traspasado_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.partidas_mostrador
    ADD CONSTRAINT partidas_mostrador_traspasado_por_fkey FOREIGN KEY (traspasado_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: presentaciones presentaciones_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.presentaciones
    ADD CONSTRAINT presentaciones_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE CASCADE;


--
-- Name: producto_imagenes producto_imagenes_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.producto_imagenes
    ADD CONSTRAINT producto_imagenes_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE CASCADE;


--
-- Name: productos productos_categoria_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.productos
    ADD CONSTRAINT productos_categoria_id_fkey FOREIGN KEY (categoria_id) REFERENCES public.categorias(id) ON DELETE RESTRICT;


--
-- Name: promociones promociones_categoria_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promociones
    ADD CONSTRAINT promociones_categoria_id_fkey FOREIGN KEY (categoria_id) REFERENCES public.categorias(id) ON DELETE CASCADE;


--
-- Name: promociones promociones_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.promociones
    ADD CONSTRAINT promociones_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE CASCADE;


--
-- Name: puntos_movimientos puntos_movimientos_cliente_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.puntos_movimientos
    ADD CONSTRAINT puntos_movimientos_cliente_id_fkey FOREIGN KEY (cliente_id) REFERENCES public.clientes(id) ON DELETE CASCADE;


--
-- Name: puntos_movimientos puntos_movimientos_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.puntos_movimientos
    ADD CONSTRAINT puntos_movimientos_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: puntos_movimientos puntos_venta_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.puntos_movimientos
    ADD CONSTRAINT puntos_venta_fk FOREIGN KEY (venta_id) REFERENCES public.ventas(id) ON DELETE SET NULL;


--
-- Name: recetas recetas_componente_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recetas
    ADD CONSTRAINT recetas_componente_id_fkey FOREIGN KEY (componente_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: recetas recetas_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.recetas
    ADD CONSTRAINT recetas_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE CASCADE;


--
-- Name: venta_consumos venta_consumos_lote_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos
    ADD CONSTRAINT venta_consumos_lote_id_fkey FOREIGN KEY (lote_id) REFERENCES public.lotes(id) ON DELETE SET NULL;


--
-- Name: venta_consumos venta_consumos_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos
    ADD CONSTRAINT venta_consumos_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE RESTRICT;


--
-- Name: venta_consumos venta_consumos_venta_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_consumos
    ADD CONSTRAINT venta_consumos_venta_id_fkey FOREIGN KEY (venta_id) REFERENCES public.ventas(id) ON DELETE CASCADE;


--
-- Name: venta_items venta_items_producto_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_items
    ADD CONSTRAINT venta_items_producto_id_fkey FOREIGN KEY (producto_id) REFERENCES public.productos(id) ON DELETE SET NULL;


--
-- Name: venta_items venta_items_venta_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.venta_items
    ADD CONSTRAINT venta_items_venta_id_fkey FOREIGN KEY (venta_id) REFERENCES public.ventas(id) ON DELETE CASCADE;


--
-- Name: ventas ventas_anulada_por_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_anulada_por_fkey FOREIGN KEY (anulada_por) REFERENCES public.usuarios(id) ON DELETE SET NULL;


--
-- Name: ventas ventas_caja_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_caja_id_fkey FOREIGN KEY (caja_id) REFERENCES public.cajas(id) ON DELETE RESTRICT;


--
-- Name: ventas ventas_cliente_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_cliente_id_fkey FOREIGN KEY (cliente_id) REFERENCES public.clientes(id) ON DELETE SET NULL;


--
-- Name: ventas ventas_cotizacion_fk; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_cotizacion_fk FOREIGN KEY (cotizacion_id) REFERENCES public.cotizaciones(id) ON DELETE SET NULL;


--
-- Name: ventas ventas_promocion_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_promocion_id_fkey FOREIGN KEY (promocion_id) REFERENCES public.promociones(id) ON DELETE SET NULL;


--
-- Name: ventas ventas_usuario_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: -
--

ALTER TABLE ONLY public.ventas
    ADD CONSTRAINT ventas_usuario_id_fkey FOREIGN KEY (usuario_id) REFERENCES public.usuarios(id) ON DELETE RESTRICT;


--
-- PostgreSQL database dump complete
--

\unrestrict WQ8occa8xgm7FDCaCYtfgU76WadLqGaj4C9Kq0utauPtLLsXBJGxzCM2u6Xcbd6

