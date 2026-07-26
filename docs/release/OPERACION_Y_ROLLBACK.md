# Operación y rollback

Antes de publicar: respaldo de PostgreSQL, etiqueta de imagen inmutable y prueba de restauración. Despliegue la imagen, aplique migración controlada y compruebe `/health/ready`.

Rollback: retire tráfico, restaure la imagen anterior y, si la migración no es retrocompatible, restaure el respaldo. Nunca ejecute `down -v` sobre datos reales. Para la demo local solamente, el reinicio limpio puede usar `docker compose down -v` y luego `docker compose up -d --build`.
