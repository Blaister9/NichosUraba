select 'businesses='||count(*) from businesses
union all select 'users='||count(*) from "AspNetUsers"
union all select 'appointments='||count(*) from appointments
union all select 'orders='||count(*) from ordering_pickup_orders
union all select 'images='||count(*) from business_images
union all select 'memberships='||count(*) from business_memberships
union all select 'municipalities='||count(*) from municipalities
union all select 'laura_version='||"Version" from businesses where "Id"='9dc7d8ea-0333-4146-9e50-9cf124ac9f0c'
union all select 'laura_updated='||"UpdatedAtUtc" from businesses where "Id"='9dc7d8ea-0333-4146-9e50-9cf124ac9f0c'
union all select 'delicadas_version='||"Version" from businesses where "Id"='266e8c06-dbc8-4f4b-8937-d32f69fb87cf';
