# Riesgos y pendientes de acceso

No hay bloqueos técnicos conocidos para demostrar V2-02 en Development.

## Pendientes productivos

1. No existe invitación por correo ni entrega productiva de credenciales.
2. Identity todavía no obliga a cambiar la contraseña temporal en el primer ingreso; por eso la creación directa solo existe en Development y la limitación se muestra al usuario.
3. No existe bloqueo administrativo de cuentas desde este módulo. Solo se desactiva la membresía del establecimiento.
4. El historial es intencionalmente mínimo; no ofrece búsqueda avanzada, exportación ni retención automática.
5. No existe aún el flujo transversal de `PlatformAdmin`; esta vertical no le concede membresía implícita.
6. Antes de datos reales siguen pendientes TLS, llaves persistentes, respaldo, observabilidad, revisión jurídica y procedimiento de recuperación de cuenta.

## Riesgos controlados

- **Último propietario:** transacción, bloqueo de filas, recuento actualizado y prueba concurrente.
- **Permisos en claims:** no se usan como fuente interna; cada operación consulta PostgreSQL.
- **Cruce entre negocios:** ruta, membresía y recurso se validan con `BusinessId`; las pruebas negativas cubren lectura y escritura.
- **Sobrescritura:** `Version` responde `409` y la UI permite recargar.
- **Contraseña temporal:** aleatoria, de una sola visualización, sin logs ni auditoría.
- **Borrado histórico:** no hay borrado de membresía, cuenta, staff o citas desde esta sección.

## Decisión futura

Antes de producción debe definirse un flujo de invitación verificable y de cambio obligatorio de contraseña. No debe ampliarse el conjunto de permisos hasta observar necesidades reales de operación.
