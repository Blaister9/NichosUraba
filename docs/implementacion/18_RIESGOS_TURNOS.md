# Riesgos y pendientes — turnos virtuales

No hay bloqueos técnicos conocidos para la demostración en Development.

## Pendientes productivos

1. SignalR funciona en una sola instancia; escalar horizontalmente exige backplane o servicio administrado.
2. Deben medirse carga, duración de conexiones, capacidad y límites por IP con tráfico representativo.
3. La duración promedio produce una estimación simple, no una promesa de atención.
4. Llaves HMAC y Data Protection requieren almacenamiento persistente, rotación y respaldo.
5. El aviso de privacidad, retención del alias y atención de derechos requieren revisión jurídica.
6. Observabilidad debe excluir códigos, alias y contenido individual; falta una política productiva de logs.
7. El modelo permite futuras definiciones, pero la interfaz administra únicamente la activa.
8. No hay modo offline, backplane, notificaciones push, WhatsApp, pagos ni reservas de hora dentro de esta vertical.

## Riesgos controlados

- numeración duplicada: bloqueo de jornada, transacción e índice único;
- doble llamado: versión observada más bloqueo PostgreSQL;
- cruce entre negocios: permiso persistido, filtros `BusinessId` y FKs compuestas;
- exposición pública: grupos separados, señales sin payload sensible y HMAC;
- cierre accidental: se rechaza si existen turnos activos;
- pérdida de turnos al pausar: la pausa conserva estado y numeración;
- permisos obsoletos: no se usan claims como fuente interna y cada operación consulta membresía.
