-- ============================================================================
-- UrabáConecta · generador del paquete de migración  (SÓLO LECTURA sobre el piloto)
--
-- Lee el estado VIVO del piloto y emite, por stdout, los ficheros del paquete
-- separados por marcas "-- @@FILE:". No escribe nada en el piloto.
--
-- Se ejecuta con:  psql -X -A -t -f generate.sql
-- ============================================================================
\set D '266e8c06-dbc8-4f4b-8937-d32f69fb87cf'
\set L '9dc7d8ea-0333-4146-9e50-9cf124ac9f0c'
\set SPA_Y_BELLEZA 'ca7e0003-0000-4000-8000-000000000003'

SET TimeZone = 'UTC';

\echo -- @@FILE:_snapshot.txt
select format('pilot_read_at_utc=%s', now());
select format('%s_version=%s updated=%s status=%s published=%s',
              "Slug", "Version", "UpdatedAtUtc", "Status", "IsPublished")
  from businesses where "Id" in (:'D', :'L') order by "Slug";
select format('%s_%s=%s', b."Slug", x.k, x.v) from businesses b
cross join lateral (values
    ('modules',   (select count(*) from business_modules            t where t."BusinessId"=b."Id")),
    ('hours',     (select count(*) from business_hours              t where t."BusinessId"=b."Id")),
    ('services',  (select count(*) from services                    t where t."BusinessId"=b."Id")),
    ('staff',     (select count(*) from staff_members               t where t."BusinessId"=b."Id")),
    ('staffsvc',  (select count(*) from staff_services              t where t."BusinessId"=b."Id")),
    ('availexc',  (select count(*) from availability_exceptions     t where t."BusinessId"=b."Id")),
    ('prodcats',  (select count(*) from ordering_product_categories t where t."BusinessId"=b."Id")),
    ('products',  (select count(*) from ordering_products           t where t."BusinessId"=b."Id")),
    ('pickupcfg', (select count(*) from ordering_pickup_settings    t where t."BusinessId"=b."Id")),
    ('imglive',   (select count(*) from business_images             t where t."BusinessId"=b."Id" and not t."IsDeleted")),
    ('memactive', (select count(*) from business_memberships        t where t."BusinessId"=b."Id" and t."IsActive")),
    ('promoactive',(select count(*) from business_promotions        t where t."BusinessId"=b."Id" and t."IsActive"
                                                                       and now() between t."StartsAtUtc" and t."EndsAtUtc"))
) as x(k, v)
where b."Id" in (:'D', :'L') order by b."Slug", x.k;

-- ---------------------------------------------------------------- taxonomía
\echo -- @@FILE:10_taxonomy.sql
\echo -- Municipios referenciados. FK RESTRICT: deben existir antes que los negocios.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'municipalities', to_jsonb(m), 'Id')
  from municipalities m
 where m."Id" in (select "MunicipalityId" from businesses where "Id" in (:'D', :'L'))
 order by m."Slug";
\echo -- La categoría se resuelve contra PilotCategorySeeder: no se inserta ninguna.

-- ---------------------------------------------------------------- identidad
\echo -- @@FILE:20_identity.sql
\echo -- CONTIENE MATERIAL SENSIBLE (PasswordHash, SecurityStamp). No imprimir.
\echo -- Los hashes son PBKDF2 autocontenidos: no dependen del anillo de Data Protection.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'AspNetUsers', to_jsonb(u), 'Id')
  from "AspNetUsers" u
 where u."Id" in (select ms."UserId" from business_memberships ms
                   where ms."BusinessId" in (:'D', :'L') and ms."IsActive")
 order by u."Email";
\echo
\echo -- Rol resuelto POR NOMBRE contra prod-real. Nunca se reutiliza el RoleId del piloto.
select format($q$INSERT INTO "AspNetUserRoles" ("UserId","RoleId") SELECT %L::uuid, r."Id" FROM "AspNetRoles" r WHERE r."Name" = 'BusinessOwner' ON CONFLICT ("UserId","RoleId") DO NOTHING;$q$, ms."UserId")
  from business_memberships ms
 where ms."BusinessId" in (:'D', :'L') and ms."IsActive"
 order by ms."UserId";

-- ---------------------------------------------------------------- negocios
\echo -- @@FILE:30_businesses.sql
\echo -- Laura se reasigna a spa-y-belleza; no se importa belleza-cuidado-personal.
\echo -- CreatedByUserId viaja NULL y lo fija 80_fixups.sql con el admin de destino.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'businesses',
              jsonb_set(
                jsonb_set(to_jsonb(b), '{CreatedByUserId}', 'null'::jsonb),
                '{CategoryId}',
                to_jsonb(case when b."Id" = :'L'::uuid then :'SPA_Y_BELLEZA' else b."CategoryId"::text end)),
              'Id')
  from businesses b where b."Id" in (:'D', :'L') order by b."Slug";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L,%L]);', 'business_modules', to_jsonb(t), 'BusinessId', 'Module')
  from business_modules t where t."BusinessId" in (:'D', :'L') order by t."BusinessId", t."Module";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'business_hours', to_jsonb(t), 'Id')
  from business_hours t where t."BusinessId" in (:'D', :'L') order by t."BusinessId", t."Day", t."SortOrder";

-- ---------------------------------------------------------------- catálogo
\echo -- @@FILE:40_catalog.sql
\echo -- Delicadas: categorías -> productos -> ajustes de recogida (NextOrderNumber=1).
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'ordering_product_categories', to_jsonb(t), 'Id')
  from ordering_product_categories t where t."BusinessId" = :'D' order by t."DisplayOrder", t."Name";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'ordering_products', to_jsonb(t), 'Id')
  from ordering_products t where t."BusinessId" = :'D' order by t."DisplayOrder", t."Name";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'ordering_pickup_settings',
              jsonb_set(to_jsonb(t), '{NextOrderNumber}', to_jsonb(1)), 'Id')
  from ordering_pickup_settings t where t."BusinessId" = :'D';
\echo
\echo -- Studio Laura: servicios -> personal -> vínculos -> excepciones de disponibilidad.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'services', to_jsonb(t), 'Id')
  from services t where t."BusinessId" = :'L' order by t."DisplayOrder", t."Name";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'staff_members', to_jsonb(t), 'Id')
  from staff_members t where t."BusinessId" = :'L' order by t."DisplayName";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L,%L,%L]);', 'staff_services', to_jsonb(t),
              'BusinessId', 'StaffMemberId', 'ServiceId')
  from staff_services t where t."BusinessId" = :'L' order by t."StaffMemberId", t."ServiceId";
\echo
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'availability_exceptions', to_jsonb(t), 'Id')
  from availability_exceptions t where t."BusinessId" in (:'D', :'L') order by t."Date";

-- ---------------------------------------------------------------- membresías
\echo -- @@FILE:50_memberships.sql
\echo -- SÓLO membresías activas. La Owner Demo de Laura (inactiva) queda fuera por el WHERE.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'business_memberships', to_jsonb(t), 'Id')
  from business_memberships t
 where t."BusinessId" in (:'D', :'L') and t."IsActive"
 order by t."BusinessId", t."CreatedAtUtc";

-- ---------------------------------------------------------------- media
\echo -- @@FILE:60_images.sql
\echo -- Sólo IsDeleted=false. Mismas StorageKey: la URL pública se compone en ejecución.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'business_images', to_jsonb(t), 'Id')
  from business_images t
 where t."BusinessId" in (:'D', :'L') and not t."IsDeleted"
 order by t."BusinessId", t."Kind", t."DisplayOrder";

-- ---------------------------------------------------------------- promociones
\echo -- @@FILE:70_promotions.sql
\echo -- Sólo promociones activas y dentro de su ventana en el momento del corte.
\echo -- Si Laura la borró o dejó vencer, aquí no aparece y no se restaura.
select format('SELECT pg_temp.uc_upsert(%L, %L::jsonb, ARRAY[%L]);', 'business_promotions', to_jsonb(t), 'Id')
  from business_promotions t
 where t."BusinessId" in (:'D', :'L') and t."IsActive" and now() between t."StartsAtUtc" and t."EndsAtUtc"
 order by t."BusinessId", t."StartsAtUtc";

-- ---------------------------------------------------------------- manifest R2
\echo -- @@FILE:media_manifest.csv
\echo storage_key,byte_size,content_type,business_slug,kind
select format('%s,%s,%s,%s,%s', t."StorageKey", t."ByteSize", t."ContentType", b."Slug", t."Kind")
  from business_images t join businesses b on b."Id" = t."BusinessId"
 where t."BusinessId" in (:'D', :'L') and not t."IsDeleted"
 order by b."Slug", t."Kind", t."StorageKey";
