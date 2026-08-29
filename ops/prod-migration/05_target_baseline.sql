-- ============================================================================
-- SÓLO PARA EL ENSAYO. NO se ejecuta en el cutover contra prod-real.
--
-- Reproduce el estado en que queda prod-real DESPUÉS de la Fase 0:
--   · las 5 categorías de PilotCategorySeeder (ya presentes hoy en prod-real);
--   · los 4 roles y el PlatformAdmin que crea ProductionAdminBootstrap al arrancar.
-- El hash es un marcador literal, no una credencial.
-- ============================================================================
INSERT INTO categories ("Id","Slug","Name","IsActive") VALUES
 ('ca7e0001-0000-4000-8000-000000000001','odontologia','Odontología',true),
 ('ca7e0002-0000-4000-8000-000000000002','veterinarias','Veterinarias',true),
 ('ca7e0003-0000-4000-8000-000000000003','spa-y-belleza','Spa y belleza',true),
 ('ca7e0004-0000-4000-8000-000000000004','droguerias','Droguerías',true),
 ('ca7e0005-0000-4000-8000-000000000005','opticas','Ópticas',true)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "AspNetRoles" ("Id","Name","NormalizedName","ConcurrencyStamp") VALUES
 (gen_random_uuid(),'PlatformAdmin','PLATFORMADMIN',gen_random_uuid()::text),
 (gen_random_uuid(),'PartnerOperator','PARTNEROPERATOR',gen_random_uuid()::text),
 (gen_random_uuid(),'BusinessOwner','BUSINESSOWNER',gen_random_uuid()::text),
 (gen_random_uuid(),'BusinessWorker','BUSINESSWORKER',gen_random_uuid()::text)
ON CONFLICT DO NOTHING;

INSERT INTO "AspNetUsers" ("Id","UserName","NormalizedUserName","Email","NormalizedEmail",
    "EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp","PhoneNumberConfirmed",
    "TwoFactorEnabled","LockoutEnabled","AccessFailedCount","DisplayName","MustChangePassword")
VALUES ('00000000-0000-4000-8000-0000000000ad','admin@rehearsal.invalid','ADMIN@REHEARSAL.INVALID',
    'admin@rehearsal.invalid','ADMIN@REHEARSAL.INVALID', true,
    'REHEARSAL-NOT-A-REAL-HASH','REHEARSAL-STAMP',gen_random_uuid()::text,false,false,true,0,
    'Administración UrabáConecta', true)
ON CONFLICT ("Id") DO NOTHING;

INSERT INTO "AspNetUserRoles" ("UserId","RoleId")
SELECT '00000000-0000-4000-8000-0000000000ad'::uuid, r."Id"
  FROM "AspNetRoles" r WHERE r."Name" = 'PlatformAdmin'
ON CONFLICT DO NOTHING;
