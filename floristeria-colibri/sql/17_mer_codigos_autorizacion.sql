-- =====================================================================
-- Mermas: autorización con código por correo
-- =====================================================================
-- Sobre el umbral, la merma pedía el correo y la CONTRASEÑA de una
-- administradora, escritos en el teléfono del vendedor. Ahora funciona
-- como el descuento de la venta: el vendedor pide un código, le llega por
-- correo a la administración, y se lo dictan.
--
-- A diferencia del de descuentos, el código queda ligado a quien lo pidió:
-- no sirve en el teléfono de otro. Los intentos fallidos los limita la API
-- (LimiteIntentos): con cinco errores la persona queda bloqueada un rato,
-- y así un código de 6 dígitos no se puede adivinar.
--
-- Idempotente.
-- =====================================================================

CREATE TABLE IF NOT EXISTS codigos_autorizacion_merma (
    id          serial      PRIMARY KEY,
    codigo      char(6)     NOT NULL,
    -- Lo que se estimaba al pedirlo: va en el correo para que quien lo
    -- dicta sepa qué está aprobando.
    valor       integer     NOT NULL CHECK (valor >= 0),
    usuario_id  integer     NOT NULL REFERENCES usuarios(id),
    usado       boolean     NOT NULL DEFAULT false,
    creado_en   timestamptz NOT NULL DEFAULT now(),
    expira_en   timestamptz NOT NULL DEFAULT now() + interval '10 minutes'
);

CREATE INDEX IF NOT EXISTS ix_cod_aut_merma_usuario
    ON codigos_autorizacion_merma (usuario_id, codigo)
    WHERE NOT usado;
