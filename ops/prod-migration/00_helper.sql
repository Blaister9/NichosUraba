-- Ayudante de sesión. Vive en pg_temp: desaparece al cerrar la conexión y no deja
-- ningún objeto en el esquema de destino.
--
-- Convierte un jsonb de fila en un upsert convergente. Reejecutar con la misma carga
-- deja exactamente el mismo estado, y reejecutar con una carga más nueva actualiza en
-- sitio sin cambiar el Id.
CREATE FUNCTION pg_temp.uc_upsert(p_table text, p_payload jsonb, p_conflict text[])
RETURNS void LANGUAGE plpgsql AS $fn$
DECLARE
    v_update   text;
    v_conflict text;
    v_sql      text;
BEGIN
    SELECT string_agg(format('%I = EXCLUDED.%I', column_name, column_name), ', '
                      ORDER BY ordinal_position)
      INTO v_update
      FROM information_schema.columns
     WHERE table_schema = 'public'
       AND table_name   = p_table
       AND NOT (column_name = ANY (p_conflict));

    SELECT string_agg(quote_ident(c), ', ') INTO v_conflict FROM unnest(p_conflict) AS c;

    IF v_conflict IS NULL THEN
        RAISE EXCEPTION 'uc_upsert: falta la clave de conflicto para %', p_table;
    END IF;

    IF v_update IS NULL THEN
        v_sql := format(
            'INSERT INTO public.%I SELECT (jsonb_populate_record(NULL::public.%I, $1)).* '
            'ON CONFLICT (%s) DO NOTHING', p_table, p_table, v_conflict);
    ELSE
        v_sql := format(
            'INSERT INTO public.%I SELECT (jsonb_populate_record(NULL::public.%I, $1)).* '
            'ON CONFLICT (%s) DO UPDATE SET %s', p_table, p_table, v_conflict, v_update);
    END IF;

    EXECUTE v_sql USING p_payload;
END
$fn$;
