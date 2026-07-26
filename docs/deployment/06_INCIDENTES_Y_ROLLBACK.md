# Incidentes y rollback

## Señales de incidente

`ready` distinto de `200`, bucle de reinicios, error de migración, login roto, pérdida de cookies tras
reinicio, datos protegidos ilegibles, aislamiento fallido, PII o secretos en logs.

## Respuesta

1. Retirar el dominio público o pausar el servicio web.
2. Preservar logs sin copiarlos a canales públicos; revocar cualquier secreto expuesto.
3. No reiniciar repetidamente si faltan llaves o la migración falló.
4. Tomar snapshot de PostgreSQL y del volumen de llaves.
5. Identificar SHA, migración y primera hora del fallo.

## Rollback

Si la migración es retrocompatible, redeploy del último SHA aprobado y ejecutar smoke. Si no lo es,
restaurar el backup de PostgreSQL y el volumen de llaves tomados juntos, luego desplegar la imagen
correspondiente. Nunca usar `EnsureCreated`, `docker compose down -v` ni borrar un volumen para
recuperar el servicio.

## Cierre

Reabrir tráfico solo con `live` y `ready` en `200`, login, lectura protegida, aislamiento y los tres
flujos públicos verificados. Registrar causa, acciones, tiempo de recuperación y prevención sin
secretos ni datos personales.
