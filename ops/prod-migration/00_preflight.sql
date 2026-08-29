-- ============================================================================
-- Comprobaciones sobre el DESTINO antes de escribir nada. Aborta la transacción
-- si el terreno no es el esperado; ninguna de estas consultas modifica datos.
-- ============================================================================
DO $$
DECLARE
    v_head    text;
    v_missing text;
BEGIN
    -- 1. Cabeza de migraciones: el paquete se generó contra este esquema.
    SELECT max("MigrationId") INTO v_head FROM "__EFMigrationsHistory";
    IF v_head IS DISTINCT FROM '20260825014007_AddBusinessLocationAndFulfillment' THEN
        RAISE EXCEPTION 'Cabeza de migraciones inesperada: % (se esperaba 20260825014007_AddBusinessLocationAndFulfillment)', v_head;
    END IF;

    -- 2. Roles: ProductionAdminBootstrap debe haber corrido ya. El paquete resuelve
    --    BusinessOwner por nombre y sin la fila insertaría cero filas en silencio.
    SELECT string_agg(r, ', ') INTO v_missing
      FROM unnest(ARRAY['PlatformAdmin','BusinessOwner']) AS r
     WHERE NOT EXISTS (SELECT 1 FROM "AspNetRoles" x WHERE x."Name" = r);
    IF v_missing IS NOT NULL THEN
        RAISE EXCEPTION 'Faltan roles en el destino: %. Ejecute antes ProductionBootstrap.', v_missing;
    END IF;

    -- 3. Un único PlatformAdmin al que apuntar CreatedByUserId.
    IF (SELECT count(*) FROM "AspNetUsers" u
         JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
         JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
        WHERE r."Name" = 'PlatformAdmin') <> 1 THEN
        RAISE EXCEPTION 'Se esperaba exactamente una cuenta PlatformAdmin en el destino.';
    END IF;

    -- 4. La categoría productiva de destino existe y no se va a duplicar.
    IF NOT EXISTS (SELECT 1 FROM categories
                    WHERE "Id" = 'ca7e0003-0000-4000-8000-000000000003' AND "Slug" = 'spa-y-belleza') THEN
        RAISE EXCEPTION 'Falta la categoría spa-y-belleza de PilotCategorySeeder.';
    END IF;

    -- 5. La categoría heredada NO debe entrar por esta puerta.
    IF EXISTS (SELECT 1 FROM categories WHERE "Slug" = 'belleza-cuidado-personal') THEN
        RAISE WARNING 'El destino ya contiene belleza-cuidado-personal; el paquete no la usa.';
    END IF;

    RAISE NOTICE 'Preflight correcto: esquema, roles, administrador y categorías en orden.';
END
$$;
