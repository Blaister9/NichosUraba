# Backup y restore

## PostgreSQL

Habilitar backups del volumen/base en Railway cuando el plan lo permita. Antes de migrar o hacer
rollback, tomar un backup y registrar proyecto, servicio, hora UTC, SHA y migración más reciente.

Prueba de restauración:

1. Restaurar en un servicio PostgreSQL aislado.
2. Conectar una instancia web temporal con secretos nuevos y un volumen de llaves restaurado.
3. Verificar `ready`, tabla `__EFMigrationsHistory`, conteos y lectura de datos protegidos.
4. Eliminar el entorno temporal al terminar, sin exponerlo públicamente.

## Llaves de Data Protection

El volumen montado en `/app/keys` es parte inseparable del backup. Una copia de PostgreSQL sin el
anillo de llaves no permite descifrar alias, teléfonos y notas. Antes de un restore, conservar el
volumen actual y nunca inicializar uno vacío sobre datos existentes.

## Objetivos del piloto

- RPO operativo: último backup disponible antes de un cambio.
- RTO objetivo: 60 minutos para restauración controlada.
- Verificación: health checks, login, una lectura protegida y un seguimiento histórico ficticio.
