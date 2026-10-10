-- =====================================================================
-- Configuración: la base trabaja en hora de Chile
-- =====================================================================
-- El contenedor de Postgres viene en UTC. Los timestamptz se guardan igual
-- (son un instante, no una hora de reloj), pero todo lo que se calcula con
-- la fecha u hora "local" salía corrido 3 a 4 horas:
--   · CURRENT_DATE y los ::date: lo registrado después de las 21:00 caía
--     en el día siguiente (reportes, folios, número de atención,
--     vencimientos de lotes y partidas);
--   · extract(hour …): el patrón horario de mermas mostraba horas UTC.
--
-- Se fija en la base y no en la conexión: así vale para la API, para psql
-- y para cualquier script, sin depender de que cada uno lo pida.
--
-- Rige para las conexiones NUEVAS: después de correrlo hay que reiniciar
-- el backend para que el pool abra conexiones con la zona nueva.
--
-- Idempotente.
-- =====================================================================

DO $$
BEGIN
    EXECUTE format('ALTER DATABASE %I SET timezone TO %L',
                   current_database(), 'America/Santiago');
END $$;
