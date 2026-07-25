# Configuración privada

**Fecha:** 25 de julio de 2026
**Rama:** `feat/v1-configuracion-negocio`

## Funciones implementadas

- Panel “Configuración del negocio” con identificación del establecimiento.
- Servicios: listado, alta, edición, activación y desactivación lógica; descripción, duración, precio, orden y cantidad de citas futuras.
- Personal: perfiles operativos sin cuenta obligatoria, nombre visible, estado, participación en disponibilidad y servicios asociados.
- Horario habitual: abierto/cerrado y un intervalo por día en `America/Bogota`; copia a lunes–viernes.
- Excepciones: cierre total, cierre parcial y apertura extraordinaria; fecha, intervalo, trabajador y motivo; filtro, edición y eliminación.
- Aviso de citas futuras en conflicto. Ningún cambio de configuración cancela, mueve ni borra citas.
- Concurrencia optimista en servicio, personal, horario y excepción. Un dato desactualizado responde `409 CONCURRENCY_CONFLICT` y la interfaz ofrece recargar.

## Rutas

| Interfaz | Ruta |
|---|---|
| Resumen | `/panel/{businessId}/configuracion` |
| Servicios | `/panel/{businessId}/configuracion/servicios` |
| Personal | `/panel/{businessId}/configuracion/personal` |
| Horarios | `/panel/{businessId}/configuracion/horarios` |
| Excepciones | `/panel/{businessId}/configuracion/excepciones` |

API privada:

- `GET/POST /api/v1/businesses/{businessId}/services`
- `PUT /api/v1/businesses/{businessId}/services/{serviceId}`
- `GET/POST /api/v1/businesses/{businessId}/staff`
- `PUT /api/v1/businesses/{businessId}/staff/{staffId}`
- `GET /api/v1/businesses/{businessId}/hours`
- `PUT /api/v1/businesses/{businessId}/hours/{day}`
- `GET/POST /api/v1/businesses/{businessId}/availability-exceptions`
- `DELETE /api/v1/businesses/{businessId}/availability-exceptions/{exceptionId}?version={version}`

## Permisos y aislamiento

Cada caso de uso vuelve a consultar la membresía activa y el permiso persistido. Propietarios administran por rol; trabajadores requieren `CanManageConfiguration`. El trabajador de operación sembrado no tiene ese permiso y recibe `403`; la cuenta `configuradora@bella.demo` demuestra el caso autorizado.

El `BusinessId` proviene de la ruta, pero nunca se considera autorización. Lecturas, cambios y asociaciones filtran por negocio. Las claves compuestas impiden asociar personal y servicios de negocios distintos. Un visitante recibe `401`; un miembro de otro negocio o sin permiso recibe `403`, conforme a la convención existente.

## Reglas

- Servicio: nombre obligatorio, duración de 5 a 480 minutos, precio y orden no negativos. Desactivar preserva citas e instantáneas históricas.
- Personal: nombre obligatorio y al menos un servicio del mismo negocio. Desactivar o retirar de disponibilidad no borra historia.
- Horario: el modelo actual admite un intervalo por día; apertura debe ser anterior al cierre.
- Excepción: solo una por trabajador y fecha; cierre total no lleva horas; cierre parcial y apertura extraordinaria exigen intervalo válido.
- Disponibilidad pública excluye servicios inactivos, personal inactivo o no participante, citas activas y cierres parciales/totales.

## Comportamiento móvil verificado

La interfaz usa tarjetas en lugar de tablas, acciones táctiles de al menos 44 px, navegación horizontal compacta, formularios de una columna bajo 720 px, etiquetas visibles, estados de carga/vacío/error y mensajes con roles accesibles. Playwright comprobó el resumen a `360 × 800` sin desbordamiento horizontal y ejecutó el CRUD principal desde ese viewport.

Descripción verificable: el resumen muestra cuatro tarjetas numeradas; cada sección conserva una barra de navegación, el nombre cotidiano de cada concepto y mensajes explícitos de que las citas existentes no cambian.
