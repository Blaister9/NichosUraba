# Métricas de la cohorte

Las metas son hipótesis internas para esta cohorte. No representan benchmarks del sector. Medir por código de establecimiento y semana; conservar datos personales y comprobantes fuera del repositorio.

## Diccionario

| Grupo | Métrica | Definición | Método |
|---|---|---|---|
| citas | solicitudes | citas creadas, excluyendo pruebas | conteo semanal agregado |
| citas | completadas | citas con estado completado | conteo semanal |
| citas | no asistencia | citas marcadas “no asistió” | conteo; no atribuir causalidad |
| citas | canceladas/rechazadas | citas en cada estado | conteo semanal |
| turnos | turnos emitidos | tickets en línea + presenciales, sin pruebas | conteo semanal |
| turnos | completados | tickets completados | conteo semanal |
| turnos | abandonados | cancelados, omitidos sin regreso u otra regla documentada | separar estados; no inventar |
| turnos | sesiones activas | días con fila abierta y al menos un ticket real | conteo semanal |
| pedidos | pedidos creados | pedidos para recoger, sin pruebas | conteo semanal |
| pedidos | completados | pedidos marcados completados | conteo semanal |
| pedidos | cancelados | pedidos cancelados | conteo semanal |
| pedidos | valor registrado | suma de totales de pedidos | no llamarlo ventas ni pagos cobrados |
| adopción | negocio activo semanal | realizó ≥1 operación real o cambio operativo esa semana | sí/no por código |
| adopción | enlace publicado | negocio confirma publicación y se verifica acceso | sí/no + fecha |
| adopción | operaciones | citas, turnos o pedidos creados | no sumar pruebas |
| soporte | incidentes | solicitudes agrupadas por causa | conteo por tipo |
| soporte | minutos | tiempo total de atención por negocio | temporizador simple |
| soporte | repetición | misma causa en más de una semana | sí/no |
| renovación | oferta presentada | precio y alcance comunicados | sí/no + fecha |
| renovación | pagada | pago recibido y verificado por canal privado | sí/no |
| renovación | referencia | autorización verificable para una presentación a otro negocio | sí/no; datos privados |

## Tablero mínimo

| Resultado al día 30 | Umbral mínimo |
|---|---:|
| negocios incorporados | 5 |
| negocios que publican el enlace | 4 |
| negocios activos semanalmente | 3 |
| negocios con al menos 10 operaciones acumuladas | 3 |
| renovaciones pagadas | 2 |
| referencias obtenidas | 1 |

Para evitar una lectura falsa: 10 operaciones no demuestran valor económico; dos renovaciones no demuestran un mercado escalable. Son señales mínimas para continuar validando.

## Ritmo por semana

| Momento | Revisión |
|---|---|
| día 0 | línea base: método actual, volumen semanal declarado y problema |
| día 7 | publicación, acceso, primera operación, minutos de soporte |
| día 14 | actividad semanal, operaciones y causa de inactividad |
| día 21 | uso por módulo, función ausente y oferta de renovación |
| día 27 | decisión pendiente, pago o rechazo |
| día 30 | resultado completo y decisión GO/ITERATE/NO-GO |

## Reglas de calidad

- Separar operaciones ficticias de reales.
- Separar creado de completado.
- No presentar total de pedidos como dinero recaudado.
- No inferir aumento de ventas, ahorro o reducción de espera sin línea base comparable.
- Una semana activa requiere acción real, no solo inicio de sesión.
- Una renovación cuenta solo cuando está pagada.
- Registrar inactividad y cancelación; no borrarlas del denominador.

## Formato agregado sugerido

`Semana | Código | Módulo | Publicó | Activo | Creadas | Completadas | Canceladas | Soporte_min | Función_ausente | Renovó_pagando`

Guardar la tabla real en el espacio privado. Al repositorio solo pueden llegar totales anónimos sin campos de contacto o clientes.
