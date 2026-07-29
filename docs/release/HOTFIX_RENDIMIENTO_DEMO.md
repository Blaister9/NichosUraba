# Hotfix de rendimiento de la Demo pública

Fecha: 29 de julio de 2026. Commit desplegado: `c73d9e3`.
Línea base previa: `4c3e149`. URL medida: `https://nichosuraba-production.up.railway.app`.

## Causa principal

La aplicación corre en **US West** y PostgreSQL en **US East**, comunicándose por red
privada (`postgres.railway.internal`). Una sola ida y vuelta a la base cuesta **~73 ms**;
en la misma región costaría entre 1 y 5 ms.

Medición que lo aísla, con seis muestras por ruta y la red del observador como piso:

| Sonda | Mediana | Interpretación |
|---|---|---|
| `/health/live` (sin base) | 156 ms | piso de red hasta US West |
| `/health/ready` (una consulta) | 229 ms | **73 ms por ida y vuelta** |

Con ese coste unitario, el tiempo de una página lo determinaba **cuántas consultas
emitía**, no su complejidad. Las tres rutas lentas correspondían exactamente a su
número de idas y vueltas:

| Ruta | Consultas | Predicción | Medido |
|---|---|---|---|
| `/api/v1/admin/businesses` | `1 + 16N`, con N=12 → 193 | 14,1 s | 14,5 s |
| `/api/v1/public/businesses/{slug}` | 8 | 0,58 s | 0,66 s |
| `/api/v1/businesses/{id}/appointments` | `1 + 2N`, con N=17 → 35 | 2,6 s | 2,8 s |

## Causas secundarias

- `PlatformAdministrationStore.ListAsync` recorría los negocios llamando a `GetAsync`,
  y cada `GetAsync` emitía dieciséis consultas. El coste crecía con cada piloto dado
  de alta: la consola se volvía más lenta con el uso.
- `UrabaStore.GetBusinessProfileAsync` emitía ocho consultas secuenciales, **dos de
  ellas idénticas** (`hours` y `publicHours` leían la misma tabla con la misma
  proyección; la expresión `hours.Count > 0 ? hours : publicHours` siempre resolvía a
  `publicHours`).
- `UrabaStore.GetAppointmentsAsync` leía el **mismo** negocio una vez por cita.
- El documento HTML y las respuestas JSON salían **sin comprimir**. Los activos
  estáticos sí iban precomprimidos por `MapStaticAssets`.
- Un logo se mostraba a 64 px descargando el archivo de 1600 px, porque sólo se
  guardaba una variante y conservando el formato de entrada: un PNG seguía siendo PNG.
- La portada de la primera tarjeta, que es el elemento LCP, se pedía con `loading="lazy"`.
- `QueueTracking` sondeaba la API **cada dos segundos** además de recibir avisos del hub,
  y abría un hub nuevo y un sondeo adicional en cada fijación de parámetros.
- Varios controles con `@onclick` no esperaban el circuito, así que se veían operativos
  y descartaban el clic en silencio. El aviso de reconexión estaba en inglés.

## Cambios aplicados

| Área | Cambio |
|---|---|
| Consola administrativa | `ListAsync` y `GetAsync` comparten una proyección única con subconsultas correlacionadas. De `1 + 16N` a **una** sentencia. Se conserva el rastreo en `GetAsync` porque `UpdateModulesAsync` muta sus módulos. |
| Ficha pública | Una sola sentencia; se elimina la consulta duplicada. Se proyectan valores crudos y se formatean en memoria para que todo sea traducible a SQL. |
| Panel de citas | El negocio se lee una vez y los consentimientos en bloque. |
| Caché | `IPublicDirectoryCache`, memoria del proceso, 60 s, invalidación por generación. Sólo directorio y fichas publicadas. **Nunca** paneles privados, datos personales, tokens ni respuestas dependientes del usuario. La vista previa administrativa (`requirePublished: false`) no se cachea. |
| Invalidación | Se invalida en `UrabaStore.SaveChangesAsync` y en los casos de uso de plataforma e imágenes. Se eligió el punto central tras detectar que invalidar sólo en los casos de uso dejaba fichas vencidas al cambiar la visibilidad de un servicio. |
| Compresión | Brotli y Gzip para HTML, JSON y `problem+json`. |
| Imágenes | Lado mayor según uso: logo 320 px, portada 1280 px, galería 1600 px. Salida siempre WebP. La portada de la primera tarjeta pasa a `loading="eager"` con `fetchpriority="high"`. |
| Interactividad | Controles deshabilitados con aviso hasta que el circuito responde en directorio, consola, seguimiento de citas, turnos y pedidos. Aviso de reconexión en español. |
| Sondeo | El respaldo de `QueueTracking` pasa de 2 s a 15 s y no se duplica el hub. |

## Mediciones antes y después

TTFB del documento, mediana, contra la URL pública.

| Ruta | Antes (frío) | Después (frío) | Antes (caliente) | Después (caliente) | Reducción caliente |
|---|---|---|---|---|---|
| Consola de socia `/admin/negocios` | 14 781 ms | 754 ms | 14 577 ms | 535 ms | **−96,3 %** |
| Crear negocio | 14 825 ms | 763 ms | 14 650 ms | 535 ms | **−96,3 %** |
| Panel de citas | 3 188 ms | 890 ms | 2 937 ms | 682 ms | **−76,8 %** |
| Ficha de Bella Urabá | 909 ms | 309 ms | 756 ms | 162 ms | **−78,6 %** |
| Directorio | 398 ms | 391 ms | 280 ms | 168 ms | **−40,0 %** |
| Panel de turnos | 862 ms | 993 ms | 672 ms | 694 ms | +3,3 % (sin cambio real) |
| Panel de pedidos | 539 ms | 885 ms | 386 ms | 381 ms | −1,3 % (sin cambio real) |
| Login | 318 ms | 318 ms | 162 ms | 159 ms | −1,9 % |

Turnos, pedidos y login no cambiaron porque sus rutas de consulta ya estaban acotadas
(≈6, ≈3 y 0 idas y vueltas) y nunca fueron el cuello de botella. Las cifras de la
primera pasada de pedidos (482 ms y 885 ms) resultaron ser variación del entorno: doce
muestras posteriores dieron p50 = 381 ms, min 379 ms.

API, mínimo de cuatro muestras, descontando el piso de red de 156 ms:

| Endpoint | Antes | Después | Servidor antes | Servidor después | Reducción |
|---|---|---|---|---|---|
| `/api/v1/admin/businesses` | 14 520 ms | 570 ms | 14 364 ms | 414 ms | **−97,1 %** |
| `/api/v1/businesses/{id}/appointments` | 2 938 ms | 606 ms | 2 782 ms | 450 ms | **−83,8 %** |
| `/api/v1/public/businesses/{slug}` | 819 ms | 232 ms | 663 ms | 76 ms | **−88,5 %** |
| `/api/v1/admin/businesses/{id}` (403) | 1 409 ms | 312 ms | 1 253 ms | 156 ms | **−87,5 %** |
| `/api/v1/public/businesses` | 311 ms | 234 ms | 155 ms | 78 ms | −49,7 % |

Peso transferido, contra el contenedor local con Brotli activo:

| Respuesta | Antes | Después | Reducción |
|---|---|---|---|
| Documento HTML del directorio | 12 625 B | 5 666 B | −55,1 % |
| JSON de la ficha pública | 2 832 B | 749 B | −73,6 % |

Consultas verificadas contra PostgreSQL real, contando sentencias en el registro:

| Operación | Antes | Después |
|---|---|---|
| Ficha pública, primera lectura | 8 | **1** |
| Ficha pública, lectura cacheada | 8 | **0** |
| Listado administrativo | `1 + 16N` | fijo, ≤8 con catálogos |
| Panel de citas | `1 + 2N` | fijo |

## Interactividad

Tres pasadas por ruta, un proceso corto por ruta, guardando cada pasada al terminar.
El criterio es el efecto observable en el DOM, nunca una espera fija. `:disabled` se
usa en lugar de `.disabled` porque un botón dentro de un `<fieldset disabled>` no lleva
el atributo pero tampoco acepta clics.

| Ruta | Botón visible | Circuito listo | Botón habilitado | Ventana visible-pero-inactiva | Primer clic efectivo | Aviso de espera |
|---|---|---|---|---|---|---|
| Directorio | 665–859 ms | 1 544–1 916 ms | 2 040–2 419 ms | 1 324–1 517 ms | 205–227 ms | sí |
| Crear negocio | 1 274–1 330 ms | 2 118–2 504 ms | 2 828–3 201 ms | 1 512–1 869 ms | 242–262 ms | sí |
| Panel de citas | 1 341–1 608 ms | 2 552–2 660 ms | 3 096–3 161 ms | 1 509–1 742 ms | 214–225 ms | no |
| Panel de turnos | 1 319–1 469 ms | 2 215–2 618 ms | 2 984–3 387 ms | 1 584–2 023 ms | — | no |
| Panel de pedidos | 1 068–1 082 ms | 1 936–2 143 ms | 2 426–2 634 ms | 1 302–1 502 ms | — | no |

Transporte: **WebSockets** en las cinco rutas, en las quince pasadas. Nunca Long Polling.

La ventana sigue existiendo, entre 1,3 y 2,0 s, porque es inherente a prerenderizar y
luego conectar el circuito. Lo que cambió es que **ya no descarta clics en silencio**:
en las quince pasadas el control principal estaba deshabilitado al pintarse. Antes de
este cambio los guiones de verificación del repositorio tenían que reintentar cada
botón hasta seis veces y terminaban creando por API lo que el clic no lograba
(`private/remote-demo-commercial.mjs`, «falta de efecto observable del clic remoto»).

## Resultados funcionales

Recorrido completo sin recargar en ningún paso:

- Consola de socia carga con doce negocios y el filtro responde.
- Asistente de creación avanza los cuatro pasos.
- Borrador guardado con confirmación visible, sin refresco manual.
- Propietario pausa y reanuda la jornada; la vista refleja cada cambio sola.
- El cambio persiste al recargar.
- El borrador de verificación quedó **archivado**: no deja residuo.

`/health/live` y `/health/ready` responden 200. Barrido de catorce rutas públicas:
**cero respuestas 5xx**.

## Pruebas

`dotnet build -c Release` y `dotnet test -c Release --no-build`: **191 aprobadas, 0 con error**
(97 de dominio, 66 de integración, 28 de extremo a extremo). Contenedor local saludable.

Nota: el enunciado habla de 181 pruebas, pero el commit `4c3e149` contiene **184** casos
ejecutables (175 métodos, algunos `[Theory]` con varios casos). Se añadieron 7.

Las pruebas nuevas afirman el **número de sentencias**, no el tiempo de reloj, que sería
inestable. `QueryCounter` vive en el contenedor de la fábrica, que xunit crea una vez por
clase, así que no comparte estado entre clases que corren en paralelo.

Una de esas pruebas detectó una regresión introducida por la propia caché: al cambiar la
visibilidad de un servicio, la ficha pública seguía sirviendo la versión anterior. De ahí
que la invalidación se centralizara en `SaveChangesAsync`.

## Riesgos pendientes

1. **Regiones separadas.** El coste de 73 ms por consulta sigue ahí. Se redujo el número
   de idas y vueltas, no su precio.
2. **Imágenes existentes sin cambio.** La normalización por uso y WebP aplica **sólo a
   cargas nuevas**; se guarda una variante por imagen. El directorio sigue transfiriendo
   1,21 MB de logos y portadas. Reprocesar las existentes implica escribir en el bucket
   productivo: no se hizo.
3. **Negocio de prueba publicado.** `prueba_1` (slug `1234`, Carepa) está **publicado y
   visible** en el directorio con un logo PNG de **696 KB**, el 55 % del peso de imágenes
   de la página. Suspenderlo es un cambio de datos, no de código.
4. **Ventana sin etiqueta en los paneles operativos.** En citas, turnos y pedidos los
   controles se deshabilitan entre 1,3 y 2,0 s sin texto que lo explique. No se pierde
   ningún clic y el contenido es legible, pero conviene el mismo aviso que ya tienen el
   directorio y la consola.
5. **Arranque en frío.** La primera petición tras un despliegue costó 7,8 s de TTFB
   (JIT, construcción del modelo de EF y primera conexión cruzada). En estado estable el
   directorio responde en 168 ms. Conviene calentar la Demo antes de una reunión.
6. **Compresión sobre HTTPS.** `EnableForHttps = true` es necesario aquí. Se acepta el
   riesgo teórico tipo BREACH: las páginas afectadas no reflejan entrada controlada por
   un tercero junto a un secreto.
7. **Sondeo de respaldo a 15 s.** Si el hub fallara, el seguimiento de turnos tardaría
   hasta 15 s en reflejar un cambio, frente a 2 s antes.
8. **Registro de EF en Information.** Cada sentencia se registra (con parámetros
   redactados, sin datos personales). Sirvió como telemetría de esta medición; bajarlo a
   `Warning` tras la reunión reduce volumen y CPU.

## Recomendación sobre la región, sin migrarla

Unificar app y PostgreSQL en **US East** es la corrección de raíz: llevaría cada consulta
de ~73 ms a 1–5 ms y mejoraría todas las rutas a la vez, incluidas las que este trabajo
no tocó. US East se prefiere sobre US West porque el volumen de datos vive allí y mover
la base es lo arriesgado.

**No se ejecutó** porque Railway no cambia la región de un servicio con volumen en
caliente: exige recrear el servicio y restaurar el volumen, con ventana de indisponibilidad
y riesgo de pérdida si la restauración falla.

Procedimiento y riesgo, para decidir con calma y **fuera** de la ventana de la reunión:

1. Respaldo verificado de PostgreSQL con `docs/deployment/04_BACKUP_Y_RESTORE.md`, y
   comprobación de que el respaldo restaura en un entorno aparte.
2. Respaldo del volumen `/app/keys`. **Perderlo impide descifrar los datos personales
   ya guardados**: sin las llaves de Data Protection, los teléfonos y alias cifrados
   quedan irrecuperables.
3. Crear el servicio de base en US East, restaurar y verificar conteos por tabla.
4. Repuntar `ConnectionStrings__DefaultConnection` al nuevo host interno.
5. Verificar `/health/ready`, y que `/health/ready` menos `/health/live` baje de ~73 ms
   a menos de 10 ms. Esa diferencia es la comprobación de que la migración sirvió.
6. Conservar el servicio anterior sin borrar hasta validar, como plan de retorno.

Alternativa de menor riesgo si no se desea migrar: mover el **servicio web** a US East,
que no tiene datos y sólo requiere recrear el volumen de llaves con su respaldo.

## Reproducir la medición

Los guiones viven fuera del repositorio, en el directorio temporal de la sesión, y toman
el secreto por variable de entorno del proceso: nunca se escribe en archivos ni registros.

- `dblat.mjs` aísla el coste por ida y vuelta a la base.
- `apitimes.mjs` cronometra los endpoints autenticados sin prerenderizado ni circuito.
- `route.mjs <ruta>` mide una ruta por proceso y guarda cada pasada al terminar.
- `funcional.mjs` recorre consola, asistente y cambio de estado, y archiva lo que crea.

Lección de método: una medición larga en un solo proceso silencioso no deja ver nada
hasta que termina, y si falla se pierde todo. Conviene un proceso corto por ruta que
persista cada pasada en cuanto la obtiene.
