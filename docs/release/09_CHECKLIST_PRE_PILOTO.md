# Checklist prepiloto

## Plataforma

- [ ] Migración aplicada y sin cambios pendientes.
- [ ] `PlatformAdmin` accede; propietario normal recibe `403`.
- [ ] Negocio creado con identidad, municipio, categoría y función correctos.
- [ ] Administrador global no tiene membresía operativa.
- [ ] Responsable inicial confirmó acceso exclusivo a su negocio.
- [ ] Lista de preparación completa y revisada por una persona.
- [ ] Directorio muestra solo negocios `Active`.

## Cuenta piloto

- [ ] Clave temporal entregada por canal acordado y no registrada.
- [ ] Primer ingreso redirige al cambio obligatorio.
- [ ] Panel y API quedan bloqueados antes del cambio.
- [ ] Clave temporal deja de funcionar después del cambio.
- [ ] Recuperación de acceso y responsable de soporte definidos.

## Operación

- [ ] Citas: horario, servicio y personal revisados.
- [ ] Turnos: duración, cupo, mensaje y jornada revisados.
- [ ] Pedidos: franjas, capacidad, categoría, producto y precio revisados.
- [ ] Suspensión elimina el negocio del directorio y bloquea operaciones nuevas.
- [ ] Seguimientos históricos continúan disponibles.
- [ ] Viewport móvil y escritorio comprobados.

## Release

- [ ] Compilación Release sin advertencias.
- [ ] Suite unitaria, integración PostgreSQL y E2E completa aprobada.
- [ ] Health checks `live` y `ready` aprobados.
- [ ] Reinicio de contenedores conserva datos.
- [ ] Seed ejecutado dos veces sin duplicados.
- [ ] Auditoría de secretos sin hallazgos.
- [ ] Respaldo, rollback, soporte y ventana de piloto acordados.
