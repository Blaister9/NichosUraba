select 'COLUMNS='||md5(string_agg(sig, '|' order by sig)) from (
  select table_name||'.'||column_name||':'||data_type||':'||is_nullable||':'||coalesce(character_maximum_length::text,'-') as sig
  from information_schema.columns where table_schema='public') t;
select 'CONSTRAINTS='||md5(string_agg(sig,'|' order by sig)) from (
  select conrelid::regclass::text||':'||conname||':'||pg_get_constraintdef(oid) as sig
  from pg_constraint where connamespace='public'::regnamespace) t;
select 'INDEXES='||md5(string_agg(indexdef,'|' order by indexdef)) from pg_indexes where schemaname='public';
select 'FK_UNVALIDATED='||count(*) from pg_constraint where contype='f' and not convalidated and connamespace='public'::regnamespace;
select 'TABLES='||count(*) from information_schema.tables where table_schema='public' and table_type='BASE TABLE';
