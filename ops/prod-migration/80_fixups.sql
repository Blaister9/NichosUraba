-- ============================================================================
-- Remapeos que sólo pueden resolverse contra el destino.
--
-- CreatedByUserId no tiene FK, así que una migración literal dejaría dos negocios
-- apuntando a cuentas Demo que en prod-real no existen. Se fija al PlatformAdmin
-- del destino, que es lo que la incorporación asistida significa allí.
-- Idempotente: sólo actúa sobre filas cuyo valor sigue siendo NULL.
-- ============================================================================
UPDATE businesses b
   SET "CreatedByUserId" = (
        SELECT u."Id" FROM "AspNetUsers" u
          JOIN "AspNetUserRoles" ur ON ur."UserId" = u."Id"
          JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
         WHERE r."Name" = 'PlatformAdmin'
         ORDER BY u."Id" LIMIT 1)
 WHERE b."Id" IN ('266e8c06-dbc8-4f4b-8937-d32f69fb87cf','9dc7d8ea-0333-4146-9e50-9cf124ac9f0c')
   AND b."CreatedByUserId" IS NULL;
