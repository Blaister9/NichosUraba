# Pendientes y riesgos — configuración privada

No hay bloqueos técnicos conocidos para ejecutar esta vertical en Development.

## Pendientes reales

1. El horario habitual admite un solo intervalo por día porque esa es la cardinalidad del modelo existente. Horarios partidos requieren cambiar la unicidad y la interfaz.
2. Existe una excepción por trabajador y fecha. Dos cierres parciales el mismo día exigirían una colección de intervalos y otra migración.
3. El permiso de configuración está persistido y probado, pero no existe todavía una interfaz para otorgarlo o retirarlo; la gestión completa de membresías pertenece a V2-02.
4. Un perfil operativo no puede vincularse a una cuenta desde esta interfaz. No se implementaron invitaciones.
5. El aviso identifica conflictos con citas futuras; la revisión, cancelación o contacto se hace individualmente en Citas. No hay reprogramación masiva.
6. El borrado de una excepción elimina la regla, no una cita. La auditoría transversal de cambios de configuración prevista en arquitectura aún no existe en la línea base.
7. Antes de datos reales siguen pendientes revisión jurídica, llaves persistentes, secretos, respaldo, TLS, observabilidad y prueba de carga.

## Riesgos

- Una configuración incorrecta puede publicar franjas no deseadas; la interfaz avisa, pero el responsable del negocio debe revisarla.
- Desactivar a la única persona que presta un servicio deja el servicio visible sin franjas; no se desactiva automáticamente para evitar efectos silenciosos.
- La copia lunes–viernes ejecuta un comando por día. Si aparece un conflicto intermedio, informa recargar; no usa un lote distribuido.
- `CanManageConfiguration` es amplio para las cuatro secciones. Permisos más granulares requieren evidencia de necesidad.

## Siguiente validación recomendada

Probar con propietarios ficticios el tiempo para crear un servicio, asociarlo a personal, corregir un horario y registrar un cierre. Registrar errores de comprensión antes de ampliar el modelo.
