-- Huella del estado completo: filas y hash del contenido de cada tabla.
-- Dos ejecuciones del migrador deben producir huellas idénticas. El hash se
-- calcula sobre el jsonb de cada fila, así que detecta también un UPDATE que
-- cambiara un valor sin alterar el número de filas.
SELECT c.relname AS tabla, q.filas, q.huella
  FROM pg_class c
  JOIN pg_namespace n ON n.oid = c.relnamespace
  CROSS JOIN LATERAL (
      SELECT (xpath('/row/c/text()',
                query_to_xml(format('SELECT count(*) AS c FROM public.%I', c.relname),
                             false, true, '')))[1]::text::bigint AS filas,
             (xpath('/row/h/text()',
                query_to_xml(format(
                    'SELECT COALESCE(md5(string_agg(t.j, %L ORDER BY t.j)), %L) AS h '
                    'FROM (SELECT to_jsonb(x)::text AS j FROM public.%I x) t',
                    '|', 'vacia', c.relname), false, true, '')))[1]::text AS huella
  ) q
 WHERE n.nspname = 'public' AND c.relkind = 'r'
   AND c.relname <> '__EFMigrationsHistory'
   AND q.filas > 0
 ORDER BY c.relname;
