-- ═══════════════════════════════════════════════════════════════
-- ACCESO · Usuarios y autenticación
--
-- La verificación de la contraseña NO se hace acá: BCrypt vive en C#
-- porque Postgres necesitaría la extensión pgcrypto y el hash tendría
-- que viajar igual. Lo que sí hace la base es entregar el hash solo a
-- través de sp_acc_c_usuario_login, que es la única función que lo
-- expone.
-- ═══════════════════════════════════════════════════════════════

-- ───────────────────────────────────────────────────────────────
-- sp_acc_c_usuario_login — la ÚNICA que devuelve password_hash
--
-- Devuelve la fila aunque el usuario esté inactivo: quién decide qué
-- mensaje mostrar es el BLL, después de verificar la contraseña. Si
-- filtráramos por activo acá, un usuario desactivado recibiría
-- "credenciales incorrectas" y llamaría por teléfono a preguntar por
-- qué su clave dejó de funcionar.
--
-- El índice es sobre lower(email), así que la comparación va igual.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_c_usuario_login(p_email text)
RETURNS TABLE (
    id             int,
    nombre         text,
    email          text,
    password_hash  text,
    rol            rol_usuario,
    activo         boolean
)
LANGUAGE sql
STABLE
AS $$
    SELECT u.id, u.nombre, u.email, u.password_hash, u.rol, u.activo
    FROM usuarios u
    WHERE lower(u.email) = lower(btrim(p_email));
$$;

COMMENT ON FUNCTION sp_acc_c_usuario_login IS
    'Único punto que expone password_hash. No usar para nada más.';


-- ───────────────────────────────────────────────────────────────
-- sp_acc_u_ultimo_acceso — sello de entrada
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_u_ultimo_acceso(p_id int)
RETURNS int
LANGUAGE sql
AS $$
    UPDATE usuarios
       SET ultimo_acceso = now(), actualizado_en = now()
     WHERE id = p_id
    RETURNING id;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_acc_c_usuarios — listado de administración. Sin hash.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_c_usuarios(
    p_busqueda  text        DEFAULT NULL,
    p_rol       rol_usuario DEFAULT NULL,
    p_activo    boolean     DEFAULT NULL
)
RETURNS TABLE (
    id             int,
    nombre         text,
    email          text,
    rol            rol_usuario,
    activo         boolean,
    ultimo_acceso  timestamptz,
    creado_en      timestamptz,
    actualizado_en timestamptz
)
LANGUAGE sql
STABLE
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


-- ───────────────────────────────────────────────────────────────
-- sp_acc_c_usuario — uno por id. Sin hash.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_c_usuario(p_id int)
RETURNS TABLE (
    id int, nombre text, email text, rol rol_usuario, activo boolean,
    ultimo_acceso timestamptz, creado_en timestamptz, actualizado_en timestamptz
)
LANGUAGE sql
STABLE
AS $$
    SELECT u.id, u.nombre, u.email, u.rol, u.activo,
           u.ultimo_acceso, u.creado_en, u.actualizado_en
    FROM usuarios u
    WHERE u.id = p_id;
$$;


-- ───────────────────────────────────────────────────────────────
-- sp_acc_i_usuario — el hash llega ya calculado desde C#
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_i_usuario(
    p_nombre         text,
    p_email          text,
    p_password_hash  text,
    p_rol            rol_usuario
)
RETURNS int
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


-- ───────────────────────────────────────────────────────────────
-- sp_acc_u_usuario — ficha. La contraseña y el estado van aparte.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_u_usuario(
    p_id      int,
    p_nombre  text,
    p_email   text,
    p_rol     rol_usuario
)
RETURNS int
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


-- ───────────────────────────────────────────────────────────────
-- sp_acc_u_usuario_password
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_u_usuario_password(
    p_id             int,
    p_password_hash  text
)
RETURNS int
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


-- ───────────────────────────────────────────────────────────────
-- sp_acc_u_usuario_estado — activar / desactivar
--
-- Desactivar en vez de borrar: el usuario firma movimientos, ventas y
-- mermas, y esas filas tienen que seguir apuntando a alguien.
-- ───────────────────────────────────────────────────────────────
CREATE OR REPLACE FUNCTION sp_acc_u_usuario_estado(
    p_id      int,
    p_activo  boolean
)
RETURNS int
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
