-- ============================================================================
-- Aplica el paquete completo en una sola transacción y en una sola sesión.
-- El ayudante vive en pg_temp, así que todo debe correr en la misma conexión.
--
--   psql -v ON_ERROR_STOP=1 -f apply.sql
--
-- Reejecutable: cada sentencia converge al estado del paquete sin duplicar filas
-- ni cambiar identificadores.
-- ============================================================================
\set ON_ERROR_STOP on
SET TimeZone = 'UTC';

\ir 00_helper.sql
\ir 00_preflight.sql

BEGIN;

\ir pkg/10_taxonomy.sql
\ir pkg/20_identity.sql
\ir pkg/30_businesses.sql
\ir pkg/40_catalog.sql
\ir pkg/50_memberships.sql
\ir pkg/60_images.sql
\ir pkg/70_promotions.sql
\ir 80_fixups.sql

COMMIT;

\echo Paquete aplicado.
