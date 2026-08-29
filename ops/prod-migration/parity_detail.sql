select conrelid::regclass::text||' :: '||conname||' :: '||pg_get_constraintdef(oid)
from pg_constraint where connamespace='public'::regnamespace order by 1;
