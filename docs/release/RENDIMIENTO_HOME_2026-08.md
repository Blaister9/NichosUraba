# Rendimiento de la Home — agosto de 2026

Medición y corrección del coste de la pantalla pública `/` sobre la Demo de Railway, entre los
commits `0c56fd8` y `89309ba`. Continúa el trabajo de
[HOTFIX_RENDIMIENTO_DEMO.md](HOTFIX_RENDIMIENTO_DEMO.md), que ya había corregido el N+1 de las
franjas de recogida y la disponibilidad preguntada día por día.

## La aritmética que explica el tiempo

La aplicación corre en US West y PostgreSQL en US East, así que **una ida y vuelta a la base cuesta
unos 73 ms**. Eso convierte el número de sentencias por petición en el factor que manda, por encima
de la complejidad de cada una. La medición del 2026-08-16 lo confirma con dos cifras:

- `/api/v1/public/businesses` respondía en **382 ms** con la caché caliente, es decir con cero o una
  sentencia. Ese es el piso: red, TLS, borde de Railway y trabajo de la aplicación.
- `/` respondía en **1.855 ms** con **18 sentencias**.

`1855 − 382 = 1473 ms`, y `18 × 73 = 1314 ms`. El modelo predice la observación. No hacía falta
buscar índices ni planes: sobraban viajes.

## De dónde salían las dieciocho

La caché pública de 30–120 s cubría el directorio y las fichas, pero **ninguna de las lecturas por
vertical**. Con la caché caliente el reparto medido con `QueryCounter` era:

| Lectura | Sentencias | ¿Cacheada? |
| --- | ---: | --- |
| Directorio de negocios | 1 | sí → 0 en caliente |
| Ficha de cada negocio sin pedidos (×2) | 1 c/u | sí → 0 en caliente |
| Promociones vigentes | 1 | no |
| Estado de la fila (barbería) | 3 | no |
| Carta de la tienda | 4 | no |
| Franjas de recogida | 3 | no |
| Próxima disponibilidad (belleza) | 7 | no |
| **Total en caliente** | **18** | |

Las cinco últimas se pedían **una vez por negocio**. Con tres negocios eran 18 sentencias; con
treinta habrían sido más de cien, y nadie lo habría notado hasta tener clientes.

## Cambios

### 1. El feed entero en una lectura — `1a9427b`

**Problema.** La Home reconstruía su contenido llamando una vez por dato y por negocio.

**Evidencia.** 18 sentencias por visita, medidas con `QueryCounter`; crecimiento lineal con el
número de negocios.

**Cambio.** `GetHomeFeedAsync`: una proyección diseñada para la Home, con los campos que el feed
pinta y ninguno más. El escaparate de cada negocio —fila, servicios, producto, horarios, ajustes de
recogida— viaja como subconsulta correlacionada dentro de la primera consulta; cuatro lecturas más
resuelven de una vez el personal, las excepciones, las citas ocupadas y la ocupación de las franjas
de **todos** los negocios juntos.

Las reglas no se tocaron. La disponibilidad la sigue calculando `BuildSlots`, que ahora acepta sus
piezas sueltas en lugar de exigir un `SchedulingContext`; las franjas de recogida salen de
`PickupSlotCalculator`, extraído de la pantalla de pedidos para que las dos no puedan ofrecer horas
distintas.

**Antes:** 18 sentencias. **Después:** 6. **Impacto:** −823 ms de mediana.

**Complejidad introducida:** un método de almacén, un DTO específico de la Home y dos extracciones
que además quitan duplicación. La Home quedó más corta que antes: ya no orquesta ocho llamadas.

**Decisión: mantener.**

### 2. No repetir la carga al hidratar — `5073f78`

**Problema.** `InteractiveServer` inicializa el componente dos veces —al prerenderizar y al abrir el
circuito—, así que la Home resolvía el feed entero dos veces por visita.

**Evidencia.** El registro de sentencias del servidor mostraba **dos** consultas de promociones por
cada carga de `/`, y **una** después del cambio. Doce sentencias por visita, no seis.

**Cambio.** El patrón que `Panel.razor` ya usaba: el prerender guarda el feed con
`PersistentComponentState` y la hidratación lo toma de ahí. Sólo viaja lo que ya está pintado en el
HTML; nada personal ni dependiente de la sesión. Si el prerender falla no se guarda nada y la
hidratación vuelve a pedirlo, que es el reintento.

**Antes:** 12 sentencias por visita. **Después:** 6. **Impacto:** no cambia el TTFB —la segunda
carga ocurría después de entregar el HTML— pero adelanta el momento en que los filtros responden y
reduce a la mitad el trabajo de base por visitante. Las tareas largas del hilo principal pasaron de
dos (169 ms y 116 ms) a ninguna medible.

**Decisión: mantener.**

### 3. Fila con el módulo apagado — `89309ba`

Corrección de consistencia, no de rendimiento: un negocio puede conservar la definición de su fila
después de que se le apaga el módulo, y el feed enviaba igual su estado. Se cierra como ya se
cerraban los servicios sin módulo de citas. **Decisión: mantener.**

## HTTP contra Railway — 40 muestras calientes por ruta

TTFB en milisegundos. `cold` es la primera petición de cada ruta, con la caché vencida.

| Ruta | cold | p50 antes | p50 después | p90 después | p95 antes | p95 después | p99 después | máx después |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `/` | 1204 | **1855** | **1032** | 1067 | **2074** | **1109** | 1316 | 1316 |
| `/negocios/…/turnos` | 989 | 792 | 742 | 828 | 1115 | 836 | 938 | 938 |
| `/negocios/…/citas` | 1181 | 897 | 877 | 931 | 947 | 945 | 964 | 964 |
| `/negocios/…/pedidos` | 989 | 927 | 880 | 931 | 1036 | 947 | 950 | 950 |
| `/explorar` | 632 | 416 | 357 | 393 | 546 | 427 | 488 | 488 |
| `/negocios/…` (ficha) | 882 | 618 | 578 | 614 | 649 | 626 | 742 | 742 |
| `/api/v1/public/businesses` | 355 | 382 | 359 | 393 | 435 | 418 | 531 | 531 |

Cero errores y 200 en las 280 peticiones de cada tanda.

**Contra los presupuestos internos:** Home mediana 1.032 ms (< 1.200 ✓) y p95 1.109 ms (< 1.500 ✓).
Fichas públicas: mediana entre 578 y 880 ms (< 1.000 ✓) y p95 entre 626 y 947 ms (< 1.500 ✓).

La Home que queda son `359 ms` de piso más `6 × 73 ms` de consultas más unos 235 ms de render de
Blazor. Bajar de ahí exigiría cachear el estado en vivo de las filas, que es justamente lo que la
pantalla promete que está actualizado.

## Escalado por número de negocios

`HomeFeedScalingTests` siembra negocios de las tres verticales y mide el feed:

| Negocios | Sentencias | Tiempo | Respuesta |
| ---: | ---: | ---: | ---: |
| 6 | 6 | 111 ms | 4.257 car. |
| 13 | 6 | 33 ms | 8.393 car. |
| 28 | 6 | 48 ms | 17.438 car. |
| 53 | 6 | 62 ms | 32.428 car. |
| 103 | 6 | 78 ms | 62.545 car. |

Las filas devueltas crecen; los viajes a la base no. La prueba falla si el conteo deja de ser
constante.

## Carga sostenida

Escalones de 45 s con cada usuario virtual repitiendo su petición a `/`.

| Usuarios | req/s antes | req/s después | p50 antes | p50 después | p95 después | p99 después | máx después | 5xx |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 5 | 1,91 | **4,18** | 2106 | **991** | 1149 | 1552 | 1663 | 0 |
| 10 | 4,73 | **8,00** | 1903 | **1045** | 1138 | 1526 | 1572 | 0 |
| 20 | 9,60 | **15,40** | 1888 | **1047** | 1181 | 1396 | 1556 | 0 |

Lo que importa no es sólo el 60 % más de caudal: es que **la mediana no se mueve** entre 5 y 20
usuarios, ni antes ni después. No hay cola. El servicio era lento por petición, no por congestión.

### Sobre `publicReadGate`

`ServerUrabaConectaApi` está registrado como **scoped** (`Program.cs:170`), así que su
`SemaphoreSlim(1,1)` vive por circuito o por petición, **nunca compartido entre visitantes**. No
puede encolar a un usuario detrás de otro, y los números lo confirman. Sigue haciendo falta para lo
que se creó —impedir dos operaciones simultáneas sobre el `AppDbContext` del circuito— y **no se
toca**. Con el feed resuelto en una llamada, además, apenas le queda nada que serializar en la Home.

## Navegador — 390×844, DPR 2, contra Demo

| Medida | Antes | Después |
| --- | ---: | ---: |
| TTFB del documento | 1638 ms | 1446 ms |
| DOM interactivo | — | 1465 ms |
| DOMContentLoaded | 1868 ms | 1648 ms |
| `load` | 2017 ms | 1651 ms |
| CLS | 0 | 0 |
| Tareas largas | 169 ms + 116 ms | ninguna medible |
| Circuito SignalR arriba | ~2,3 s | ~1,98 s |

CLS es 0 en ambos casos: las imágenes ya llevan `width`/`height`. La API de LCP no devolvió entradas
en el navegador de automatización, así que **LCP no quedó medido directamente**; `load` y el tiempo
de la imagen de portada se usan como aproximación.

### Imágenes

Auditadas y **no eran el cuello**, así que no se tocaron:

| Pieza | Natural | Renderizada | Peso | Carga |
| --- | --- | --- | ---: | --- |
| Portada Brío (LCP) | 1280×854 | 390×371 | 135 KB | `eager`, `fetchpriority=high` |
| Producto Lúmina | 768×768 | 390×343 | 12 KB | `lazy` |
| Portada Lúmina | 1280×854 | 390×343 | 96 KB | `lazy` |

Ya son WebP, la primera tiene prioridad y las demás son diferidas, y sólo dos piezas quedan sobre el
pliegue. El CSS son 27 KB con brotli y `blazor.web.js` 47 KB, ambos con
`Cache-Control: max-age=31536000, immutable` por los nombres con huella. Con `load` en 1651 ms de
los cuales 1446 son TTFB, el margen de las imágenes es pequeño frente al del servidor.

## Navegación

Home → filtro «Belleza» → desplazar a 688 px → ficha de citas → atrás. Al volver: filtro «Belleza»
activo, tres piezas, desplazamiento restaurado a 647 px —la página filtrada es más corta y lo
acota—, municipio conservado, filtros ya habilitados y **sin parpadeo de esqueleto**, porque el feed
llega pintado en el HTML del prerender.

## PWA

El service worker sólo precachea `offline.html`, el manifiesto y los iconos, y para navegaciones va
a la red con respaldo al offline: **nunca sirve HTML dinámico cacheado**, así que el estado en vivo
sigue siendo real. Los recursos con huella ya los cachea el navegador como inmutables. No hay
diferencia apreciable de arranque entre navegador y PWA instalada, y no se justificó ningún cambio.

## Investigado y descartado

- **Cachear el feed completo.** Bajaría la Home a ~600 ms, pero envejecería hasta 60 s el conteo de
  la fila, que es exactamente lo que el distintivo «EN VIVO» promete. No se hizo.
- **Bajar de seis sentencias a tres o cuatro.** Fundir promociones, personal y excepciones en la
  consulta principal ahorraría unos 220 ms a cambio de una consulta bastante menos legible. Por
  debajo del presupuesto ya cumplido, no compensa.
- **`preconnect` al host de imágenes.** Las fotos viven en un bucket R2 de otro origen, que exige
  DNS y TLS propios. La ganancia esperada es de decenas de milisegundos sobre un LCP que no se pudo
  medir directamente, y el host es configurable por ambiente. Queda como candidato **cuando LCP sea
  medible**, no antes.
- **Índices nuevos.** Ninguno. El tiempo se explicaba por número de viajes; con 103 negocios el feed
  se resuelve en 78 ms de servidor.
- **Quitar o paralelizar `publicReadGate`.** Demostrado que no encola entre usuarios.
- **Mover PostgreSQL a la región de la aplicación.** Ver abajo.

## Deuda que sigue siendo relevante

1. **Las regiones siguen separadas.** Los 73 ms por consulta son la mitad del tiempo restante de la
   Home. Unificar región dejaría las seis sentencias en menos de 30 ms en total y la Home cerca de
   600 ms sin tocar una línea. Exige recrear un servicio con volumen y **no se ha hecho**: es una
   recomendación, no una acción.
2. **Las fichas públicas también cargan dos veces** (prerender e hidratación). Están dentro de
   presupuesto —entre 578 y 880 ms de mediana—, así que no se tocaron; aplicarles
   `PersistentComponentState` reduciría a la mitad su trabajo de base por visita.
3. **LCP no es medible** con la automatización actual. Sin eso, cualquier trabajo sobre imágenes o
   `preconnect` sería a ciegas.
4. **Las lecturas públicas de fila y pedidos no usan `AsNoTracking`** (`QueueStore`, `OrderingStore`).
   Cuesta CPU y memoria, no viajes, y no apareció en ninguna medición. Es limpieza, no rendimiento.

## Cómo reproducir

```powershell
powershell -File tests/load/measure-endpoints.ps1 -Label mi-medicion -Samples 40
```

```powershell
powershell -File tests/load/sustained-load.ps1 -Label mi-carga -SecondsPerStep 45
```

Los conteos de sentencias los fijan `HomeCompositionTests` (techo de 8 para la pantalla completa) y
`HomeFeedScalingTests` (constante entre 3 y 100 negocios).
