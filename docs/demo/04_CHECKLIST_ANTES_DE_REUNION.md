# Checklist antes de una reunión

## Plataforma

- [ ] El commit desplegado coincide con la rama de demostración.
- [ ] `/health/live` responde 200.
- [ ] `/health/ready` responde 200.
- [ ] R2 responde y las imágenes se renderizan desde la URL pública.
- [ ] No hay despliegues o migraciones pendientes.

## Accesos

- [ ] PlatformAdmin inicia sesión con su credencial exclusiva.
- [ ] PartnerOperator inicia sesión.
- [ ] Las tres cuentas propietarias inician sesión.
- [ ] Una contraseña incorrecta es rechazada.
- [ ] Ninguna captura muestra campos de contraseña.

## Contenido público

- [ ] Salón Bella Urabá muestra logo, portada y galería.
- [ ] Barbería El Corte muestra logo, portada y galería.
- [ ] Restaurante Sazón Local muestra logo, portada y galería.
- [ ] Hay cupos de citas disponibles.
- [ ] La fila virtual está abierta.
- [ ] Hay franjas de recogida disponibles durante la reunión.
- [ ] Cita, turno y pedido pueden crearse completamente desde la interfaz.
- [ ] Los tres seguimientos cargan el estado esperado.

## Consola de socias

- [ ] El asistente permite crear un negocio.
- [ ] Perfil, municipio y contacto se guardan.
- [ ] Logo, portada y galería quedan persistidos.
- [ ] El módulo y el servicio o producto quedan configurados.
- [ ] La socia puede revisar o configurar los horarios desde la interfaz.
- [ ] La vista previa carga.
- [ ] La invitación se genera y se acepta.
- [ ] El negocio puede enviarse a revisión.
- [ ] La socia no ve una acción para publicar.

## Administración y propietarios

- [ ] PlatformAdmin aprueba y publica.
- [ ] La auditoría registra las acciones sin secretos.
- [ ] Suspensión y reactivación funcionan.
- [ ] Cada propietario ve un único negocio.
- [ ] Cada propietario administra su módulo y cambia una operación.
- [ ] El acceso cruzado responde 403.

## Cierre

- [ ] El negocio piloto queda archivado.
- [ ] Los tres negocios Demo originales siguen activos.
- [ ] Se conservan operaciones ficticias útiles.
- [ ] No se eliminaron usuarios.

Con el estado verificado el 28 de julio de 2026, no deben marcarse todavía los
ítems de imágenes públicas, creación pública confiable, franjas de pedido del
día ni configuración explícita de horarios por PartnerOperator.
