# 09 — Backup y restauración

## Qué hay que respaldar

| Elemento | Qué se pierde si falta |
| --- | --- |
| Base de datos PostgreSQL | Todo: negocios, citas, turnos, pedidos, cuentas y auditoría |
| Volumen `/app/keys` | Las cookies de sesión y, sobre todo, **la posibilidad de descifrar alias, teléfonos y notas de clientes ya guardados** |
| Bucket de imágenes | Logos, portadas y fotografías |

La pérdida del volumen de llaves es la más grave y la menos evidente: la base de datos seguiría
íntegra pero los datos personales cifrados quedarían ilegibles para siempre.

## Frecuencia

| Elemento | Diario | Semanal | Mensual |
| --- | --- | --- | --- |
| PostgreSQL | sí, retención 14 días | sí, retención 8 semanas | sí, retención 12 meses |
| `/app/keys` | sí, retención 14 días | sí, retención 8 semanas | copia inicial bloqueada, sin caducidad |
| Bucket R2 | no aplica (R2 conserva los objetos) | inventario semanal de claves | — |

**Copia inicial bloqueada.** Antes del primer negocio real, guarde una copia del volumen de
llaves en un lugar distinto del proveedor de despliegue y no la borre nunca. Es el último
recurso ante un borrado accidental del volumen.

## Procedimiento probado

Este procedimiento se ejecutó realmente sobre la pila de Docker local el 2026-07-27; los
resultados están en [14_RESULTADOS_PRUEBAS.md](14_RESULTADOS_PRUEBAS.md).

### Respaldo de PostgreSQL

```bash
docker exec -e PGPASSWORD="$POSTGRES_PASSWORD" <contenedor-postgres> \
  pg_dump -U "$POSTGRES_USER" -d "$POSTGRES_DB" -Fc > uraba-$(date +%Y%m%d).dump
```

En Railway, con la cadena de conexión pública del servicio de PostgreSQL:

```bash
pg_dump "$DATABASE_URL" -Fc -f uraba-$(date +%Y%m%d).dump
```

### Restauración en una base temporal

Nunca pruebe una restauración sobre la base en uso.

```bash
psql "$ADMIN_URL" -c "CREATE DATABASE uraba_restore_test;"
pg_restore -d "$RESTORE_URL" --no-owner uraba-YYYYMMDD.dump
psql "$RESTORE_URL" -c "select count(*) from businesses;"
psql "$RESTORE_URL" -c "select count(*) from consent_receipts;"
psql "$RESTORE_URL" -c 'select count(*) from "AspNetUsers";'
```

Compare los conteos con los de origen. Sólo entonces la restauración está probada.

### Respaldo de los volúmenes

```bash
docker run --rm \
  -v <proyecto>_urabaconecta_keys:/keys \
  -v <proyecto>_urabaconecta_media:/media \
  -v "$(pwd)":/backup alpine \
  sh -c "tar czf /backup/keys-$(date +%Y%m%d).tgz -C /keys . && \
         tar czf /backup/media-$(date +%Y%m%d).tgz -C /media ."
```

Verifique siempre el contenido antes de dar el respaldo por bueno:

```bash
tar tzf keys-YYYYMMDD.tgz
```

### Restauración de un volumen

```bash
docker run --rm -v <proyecto>_urabaconecta_keys:/keys -v "$(pwd)":/backup alpine \
  sh -c "rm -rf /keys/* && tar xzf /backup/keys-YYYYMMDD.tgz -C /keys"
```

En Railway, el volumen se restaura montándolo en un servicio temporal y copiando el contenido.

### Imágenes en R2

R2 conserva los objetos y no requiere respaldo periódico para el piloto. Para una copia externa:

```bash
rclone sync r2:urabaconecta-prod ./respaldo-imagenes
```

Las claves están en `business_images.StorageKey`; un objeto huérfano no rompe nada, y una
referencia sin objeto se ve como una imagen rota en la ficha.

## Antes de una migración productiva

1. Respaldo manual de PostgreSQL y del volumen de llaves.
2. Verifique el respaldo: tamaño distinto de cero y `pg_restore --list` legible.
3. Compruebe el estado en `/admin/salud`.
4. Aplique la migración desplegando la versión nueva.
5. Ejecute los *smoke tests* de [11_RUNBOOK_PRODUCCION.md](11_RUNBOOK_PRODUCCION.md).
6. Si algo falla, siga [13_PLAN_DE_ROLLBACK.md](13_PLAN_DE_ROLLBACK.md).

## Exportación de datos

No existe todavía un botón de exportación por negocio. Mientras tanto, la extracción se hace con
consultas sobre la base restaurada. Está registrado como pendiente en
[15_DATOS_MANUALES_PENDIENTES.md](15_DATOS_MANUALES_PENDIENTES.md).

## Automatización

Railway no ofrece respaldo automático de PostgreSQL en todos los planes. Hay dos caminos:

- **Manual documentado:** ejecutar los comandos de arriba con la cadencia de la tabla y guardar
  los archivos en un almacenamiento propio. Es lo que aplica hoy.
- **Programado:** un trabajo externo (por ejemplo una acción programada de GitHub) que ejecute
  `pg_dump` contra la cadena pública y suba el archivo a R2. Requiere decidir dónde guardar la
  credencial y puede implicar costo. Queda como decisión del usuario.
