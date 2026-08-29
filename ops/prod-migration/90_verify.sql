-- ============================================================================
-- Verificación posterior al apply. Sólo lee.
--
-- La barrera de operabilidad vive en AppDbContext.SaveChangesAsync y el SQL directo
-- no la atraviesa, así que aquí se reproduce en consulta la misma política de
-- BusinessOperationalReadiness.Evaluate. Si un requisito exigible falla, el negocio
-- se degradaría a PendingConfiguration en el primer guardado de la aplicación.
-- ============================================================================
\echo
\echo ===================== READINESS =====================
WITH b AS (
    SELECT * FROM businesses
     WHERE "Id" IN ('266e8c06-dbc8-4f4b-8937-d32f69fb87cf','9dc7d8ea-0333-4146-9e50-9cf124ac9f0c')
), caps AS (
    SELECT b."Id",
           COALESCE(bool_or(m."Module" = 'Appointments'  AND m."IsEnabled"), false) AS appointments,
           COALESCE(bool_or(m."Module" = 'VirtualQueues' AND m."IsEnabled"), false) AS queues,
           COALESCE(bool_or(m."Module" = 'PickupOrders'  AND m."IsEnabled"), false) AS orders
      FROM b LEFT JOIN business_modules m ON m."BusinessId" = b."Id"
     GROUP BY b."Id"
), f AS (
    SELECT b."Id", b."Slug", c.appointments, c.queues, c.orders,
           (b."LocationMode" = 'PublicPhysical')                              AS public_location,
           (b."Name"             IS NOT NULL AND btrim(b."Name") <> '')       AS has_name,
           (b."ShortDescription" IS NOT NULL AND btrim(b."ShortDescription") <> '') AS has_short,
           (b."Description"      IS NOT NULL AND btrim(b."Description") <> '')      AS has_desc,
           (COALESCE(btrim(b."PublicPhone"),'') <> '' OR COALESCE(btrim(b."WhatsAppUrl"),'') <> ''
            OR COALESCE(btrim(b."PublicEmail"),'') <> '')                     AS has_contact,
           (COALESCE(btrim(b."Address"),'') <> '')                            AS has_address,
           (b."OrderFulfillmentMode" <> 'PickupAtPublicLocation')             AS fulfillment_offsite,
           EXISTS (SELECT 1 FROM business_images i WHERE i."BusinessId" = b."Id"
                    AND NOT i."IsDeleted" AND i."Kind" = 'Logo')              AS has_logo,
           EXISTS (SELECT 1 FROM business_images i WHERE i."BusinessId" = b."Id"
                    AND NOT i."IsDeleted" AND i."Kind" = 'Cover')             AS has_cover,
           EXISTS (SELECT 1 FROM business_hours h WHERE h."BusinessId" = b."Id") AS has_hours,
           EXISTS (SELECT 1 FROM services s WHERE s."BusinessId" = b."Id" AND s."IsActive") AS has_active_service,
           EXISTS (SELECT 1 FROM staff_services ss
                     JOIN staff_members st ON st."BusinessId" = ss."BusinessId" AND st."Id" = ss."StaffMemberId"
                     JOIN services sv      ON sv."BusinessId" = ss."BusinessId" AND sv."Id" = ss."ServiceId"
                    WHERE ss."BusinessId" = b."Id" AND st."IsActive"
                      AND st."ParticipatesInAvailability" AND sv."IsActive")  AS has_eligible_staff,
           EXISTS (SELECT 1 FROM staff_services ss
                     JOIN staff_members st ON st."BusinessId" = ss."BusinessId" AND st."Id" = ss."StaffMemberId"
                     JOIN services sv      ON sv."BusinessId" = ss."BusinessId" AND sv."Id" = ss."ServiceId"
                    WHERE ss."BusinessId" = b."Id" AND st."IsActive"
                      AND st."ParticipatesInAvailability" AND sv."IsActive"
                      AND EXISTS (SELECT 1 FROM business_hours h WHERE h."BusinessId" = b."Id"
                                   AND EXTRACT(EPOCH FROM (h."ClosesAt" - h."OpensAt"))/60 >= sv."DurationMinutes"))
                                                                              AS bookable,
           EXISTS (SELECT 1 FROM queue_definitions q WHERE q."BusinessId" = b."Id"
                    AND q."IsActive" AND q."IsEnabled")                       AS has_queue,
           EXISTS (SELECT 1 FROM ordering_pickup_settings p WHERE p."BusinessId" = b."Id"
                    AND p."IsEnabled")                                        AS has_pickup_settings,
           EXISTS (SELECT 1 FROM ordering_product_categories pc WHERE pc."BusinessId" = b."Id"
                    AND pc."IsActive")                                        AS has_product_category,
           EXISTS (SELECT 1 FROM ordering_products pr
                     JOIN ordering_product_categories pc ON pc."Id" = pr."ProductCategoryId"
                    WHERE pr."BusinessId" = b."Id" AND pr."IsActive" AND pr."IsAvailable"
                      AND pc."IsActive")                                      AS has_available_product,
           EXISTS (SELECT 1 FROM ordering_pickup_settings ps
                     JOIN business_hours h ON h."BusinessId" = ps."BusinessId"
                    WHERE ps."BusinessId" = b."Id" AND ps."IsEnabled"
                      AND LEAST(h."ClosesAt", ps."ReceivesUntil") > GREATEST(h."OpensAt", ps."ReceivesFrom")
                      AND EXTRACT(EPOCH FROM (LEAST(h."ClosesAt", ps."ReceivesUntil")
                                            - GREATEST(h."OpensAt", ps."ReceivesFrom")))/60
                          >= ps."SlotIntervalMinutes")                        AS compatible_pickup,
           EXISTS (SELECT 1 FROM business_memberships ms WHERE ms."BusinessId" = b."Id"
                    AND ms."IsActive" AND ms."Role" = 'Owner')                AS has_active_owner
      FROM b JOIN caps c ON c."Id" = b."Id"
), checks AS (
    SELECT f."Slug", v.id, v.required, v.satisfied
      FROM f CROSS JOIN LATERAL (VALUES
        ('business-name',            true,                    f.has_name),
        ('short-description',        true,                    f.has_short),
        ('full-description',         true,                    f.has_desc),
        ('contact',                  true,                    f.has_contact),
        ('location',                 f.public_location,       NOT f.public_location OR f.has_address),
        ('fulfillment',              f.orders,                NOT f.orders OR f.fulfillment_offsite OR f.public_location),
        ('logo',                     true,                    f.has_logo),
        ('cover',                    true,                    f.has_cover),
        ('modules',                  true,                    f.appointments OR f.queues OR f.orders),
        ('hours',                    f.appointments OR f.orders, NOT (f.appointments OR f.orders) OR f.has_hours),
        ('services',                 f.appointments,          NOT f.appointments OR f.has_active_service),
        ('eligible-staff',           f.appointments,          NOT f.appointments OR f.has_eligible_staff),
        ('appointment-availability', f.appointments,          NOT f.appointments OR f.bookable),
        ('queue',                    f.queues,                NOT f.queues OR f.has_queue),
        ('pickup-settings',          f.orders,                NOT f.orders OR f.has_pickup_settings),
        ('catalog-category',         f.orders,                NOT f.orders OR f.has_product_category),
        ('catalog-product',          f.orders,                NOT f.orders OR f.has_available_product),
        ('pickup-availability',      f.orders,                NOT f.orders OR f.compatible_pickup),
        ('active-owner',             true,                    f.has_active_owner),
        ('permissions',              true,                    f.has_active_owner)
      ) AS v(id, required, satisfied)
)
SELECT "Slug",
       count(*) FILTER (WHERE required)                       AS exigibles,
       count(*) FILTER (WHERE required AND satisfied)         AS cumplidos,
       COALESCE(string_agg(id, ', ') FILTER (WHERE required AND NOT satisfied), '—') AS incumplidos,
       CASE WHEN count(*) FILTER (WHERE required AND NOT satisfied) = 0
            THEN 'READY' ELSE 'NOT READY' END                 AS readiness
  FROM checks GROUP BY "Slug" ORDER BY "Slug";

\echo
\echo ===================== INVENTARIO =====================
SELECT b."Slug", b."Status", b."IsPublished",
       (SELECT c."Slug" FROM categories c WHERE c."Id" = b."CategoryId")     AS categoria,
       (SELECT m."Slug" FROM municipalities m WHERE m."Id" = b."MunicipalityId") AS municipio,
       (SELECT count(*) FROM business_modules            t WHERE t."BusinessId" = b."Id") AS modulos,
       (SELECT count(*) FROM business_hours              t WHERE t."BusinessId" = b."Id") AS horas,
       (SELECT count(*) FROM services                    t WHERE t."BusinessId" = b."Id") AS servicios,
       (SELECT count(*) FROM staff_members               t WHERE t."BusinessId" = b."Id") AS personal,
       (SELECT count(*) FROM staff_services              t WHERE t."BusinessId" = b."Id") AS vinculos,
       (SELECT count(*) FROM ordering_product_categories t WHERE t."BusinessId" = b."Id") AS cat_prod,
       (SELECT count(*) FROM ordering_products           t WHERE t."BusinessId" = b."Id") AS productos,
       (SELECT count(*) FROM business_images             t WHERE t."BusinessId" = b."Id") AS imagenes,
       (SELECT count(*) FROM business_memberships        t WHERE t."BusinessId" = b."Id") AS membresias,
       (SELECT count(*) FROM business_promotions         t WHERE t."BusinessId" = b."Id") AS promos
  FROM businesses b ORDER BY b."Slug";

\echo
\echo ===================== ASERCIONES =====================
WITH a AS (
  SELECT * FROM (VALUES
    ('negocios migrados = 2',            (SELECT count(*) FROM businesses) = 2),
    ('0 citas historicas',               (SELECT count(*) FROM appointments) = 0),
    ('0 pedidos historicos',             (SELECT count(*) FROM ordering_pickup_orders) = 0),
    ('0 lineas de pedido',               (SELECT count(*) FROM ordering_pickup_order_lines) = 0),
    ('0 consentimientos',                (SELECT count(*) FROM consent_receipts) = 0),
    ('0 invitaciones',                   (SELECT count(*) FROM access_invitations) = 0),
    ('0 suscripciones push',             (SELECT count(*) FROM web_push_subscriptions) = 0),
    ('0 notificaciones',                 (SELECT count(*) FROM notifications) = 0),
    ('0 turnos / colas',                 (SELECT count(*) FROM queue_definitions) = 0),
    ('0 imagenes borradas',              (SELECT count(*) FROM business_images WHERE "IsDeleted") = 0),
    ('Owner Demo ausente',               NOT EXISTS (SELECT 1 FROM "AspNetUsers"
                                            WHERE "Id" = 'b47464f1-5c8d-40cf-8329-91b91de61d8a')),
    ('Membership Demo ausente',          NOT EXISTS (SELECT 1 FROM business_memberships
                                            WHERE "Id" = 'd41ff9ee-1300-4ab0-93d7-34210237adf9')),
    ('sin correos .demo/.test/.local',   NOT EXISTS (SELECT 1 FROM "AspNetUsers"
                                            WHERE "Email" ~* '\.(demo|test|local)$' OR "Email" ~* '@(bella|corte|sazon)\.')),
    ('membresias todas activas',         NOT EXISTS (SELECT 1 FROM business_memberships WHERE NOT "IsActive")),
    ('Laura en spa-y-belleza',           (SELECT c."Slug" FROM businesses b JOIN categories c ON c."Id" = b."CategoryId"
                                            WHERE b."Id" = '9dc7d8ea-0333-4146-9e50-9cf124ac9f0c') = 'spa-y-belleza'),
    ('belleza-cuidado-personal ausente', NOT EXISTS (SELECT 1 FROM categories WHERE "Slug" = 'belleza-cuidado-personal')),
    ('categorias productivas = 5',       (SELECT count(*) FROM categories) = 5),
    ('municipios = 2',                   (SELECT count(*) FROM municipalities) = 2),
    ('NextOrderNumber = 1',              (SELECT "NextOrderNumber" FROM ordering_pickup_settings
                                            WHERE "BusinessId" = '266e8c06-dbc8-4f4b-8937-d32f69fb87cf') = 1),
    ('CreatedByUserId remapeado',        NOT EXISTS (SELECT 1 FROM businesses WHERE "CreatedByUserId" IS NULL)),
    ('CreatedByUserId es PlatformAdmin', NOT EXISTS (
                                            SELECT 1 FROM businesses b WHERE NOT EXISTS (
                                              SELECT 1 FROM "AspNetUserRoles" ur JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                                               WHERE ur."UserId" = b."CreatedByUserId" AND r."Name" = 'PlatformAdmin'))),
    ('ambos Owners con rol BusinessOwner', (SELECT count(DISTINCT ur."UserId") FROM "AspNetUserRoles" ur
                                              JOIN "AspNetRoles" r ON r."Id" = ur."RoleId"
                                             WHERE r."Name" = 'BusinessOwner') = 2),
    ('cada membresia tiene usuario',     NOT EXISTS (SELECT 1 FROM business_memberships ms
                                            WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = ms."UserId"))),
    ('imagenes de servicio bien ligadas',NOT EXISTS (SELECT 1 FROM business_images i WHERE i."ServiceId" IS NOT NULL
                                            AND NOT EXISTS (SELECT 1 FROM services s WHERE s."Id" = i."ServiceId"))),
    ('imagenes de producto bien ligadas',NOT EXISTS (SELECT 1 FROM business_images i WHERE i."ProductId" IS NOT NULL
                                            AND NOT EXISTS (SELECT 1 FROM ordering_products p WHERE p."Id" = i."ProductId")))
  ) AS t(assertion, ok)
)
SELECT CASE WHEN ok THEN 'PASS' ELSE '*** FAIL ***' END AS resultado, assertion FROM a ORDER BY ok, assertion;

\echo
\echo ===================== TOTAL =====================
SELECT CASE WHEN bool_and(ok) THEN 'TODAS LAS ASERCIONES PASAN' ELSE '*** HAY ASERCIONES FALLIDAS ***' END AS veredicto
FROM (VALUES
    ((SELECT count(*) FROM businesses) = 2),
    ((SELECT count(*) FROM appointments) = 0),
    ((SELECT count(*) FROM ordering_pickup_orders) = 0),
    ((SELECT count(*) FROM consent_receipts) = 0),
    ((SELECT count(*) FROM access_invitations) = 0),
    ((SELECT count(*) FROM web_push_subscriptions) = 0),
    ((SELECT count(*) FROM business_images WHERE "IsDeleted") = 0),
    (NOT EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Id" = 'b47464f1-5c8d-40cf-8329-91b91de61d8a')),
    (NOT EXISTS (SELECT 1 FROM business_memberships WHERE "Id" = 'd41ff9ee-1300-4ab0-93d7-34210237adf9')),
    (NOT EXISTS (SELECT 1 FROM categories WHERE "Slug" = 'belleza-cuidado-personal')),
    ((SELECT "NextOrderNumber" FROM ordering_pickup_settings
       WHERE "BusinessId" = '266e8c06-dbc8-4f4b-8937-d32f69fb87cf') = 1),
    (NOT EXISTS (SELECT 1 FROM businesses WHERE "CreatedByUserId" IS NULL))
) AS t(ok);
