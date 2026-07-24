# Paquete de decisión — Nicho tecnológico en Urabá

**Fecha:** 24 de julio de 2026 · **Todas las fuentes consultadas:** 24 de julio de 2026

---

```
DECISIÓN:              VALIDATE
NICHO:                 Comercializadoras exportadoras de plátano y banano de Urabá que compran
                       fruta a pequeños productores independientes (modelo agregador)
PROBLEMA:              Registro de recepción de fruta en el acopio y liquidación semanal del
                       pago por productor
COMPRADOR:             Gerente de Operaciones / Director de Fruta de la comercializadora
MUNICIPIO INICIAL:     Chigorodó (eje Chigorodó–Carepa–Apartadó)
INVERSIÓN RECOMENDADA: COP 6.900.000 en 30 días de validación — NO los COP 50–56 millones del MVP
NIVEL DE CONFIANZA:    Medio-bajo (55 %)
SIGUIENTE ACCIÓN:      Fotografiar el formato que hoy se usa en 5 sitios de acopio de Chigorodó
                       y Carepa, en día de recepción
```

**Por qué no es GO:** de las once condiciones del umbral definido antes de investigar, se cumplen nueve. Fallan dos —*solución actual verificable* y *tres señales independientes de demanda*— y son precisamente las que protegen el capital. Ambas se resuelven con trabajo de campo que cuesta menos del 15 % del MVP.

---

## Por dónde empezar

1. **Abra `01_INFORME_EJECUTIVO.html`** en cualquier navegador. Es autosuficiente: se entiende sin abrir ningún otro archivo. Para obtener el PDF: Ctrl+P → *Guardar como PDF* (los estilos de impresión ya están incluidos).
2. Si va a salir a campo la próxima semana, vaya directo a **`07_KIT_VALIDACION.md`**.
3. Si va a decidir el presupuesto, vaya a **`08_PLAN_INVERSION.md`**, sección 9.

---

## Contenido del paquete

| Archivo | Qué contiene |
|---|---|
| `00_LEEME.md` | Este índice |
| `01_INFORME_EJECUTIVO.html` | **Informe ejecutivo autosuficiente.** Decisión, cifras, top 10, comparación de las tres mejores, producto, modelo comercial, estimaciones, riesgos, plan de 30 días y decisión GO/VALIDATE/NO-GO |
| `02_INFORME_TECNICO.md` | Metodología, alcance, hallazgos por sector, análisis competitivo, falsación, estimación de mercado, recomendación y limitaciones |
| `03_HOJA_01_Fuentes_Indice.csv` | Índice de las 36 fuentes con el dato que soporta cada una |
| `03_HOJA_02_Datos_Uraba.csv` | 57 datos con fórmulas vivas, año, cobertura y nivel de evidencia |
| `03_HOJA_03_Problemas.csv` | 15 problemas expresados como procesos observables |
| `03_HOJA_04_Oportunidades.csv` | 16 oportunidades con los 20 campos requeridos |
| `03_HOJA_05_Competidores.csv` | 13 competidores y sustitutos con brecha remanente |
| `03_HOJA_06_Precios.csv` | Anclajes de precio verificados e hipótesis marcadas como tales |
| `03_HOJA_07_Scoring.csv` | Matriz de 100 puntos con fórmulas `=SUM()` y topes normativos |
| `03_HOJA_08_Mercado.csv` | TAM/SAM/SOM con fórmulas vivas y tres escenarios |
| `03_HOJA_09_Supuestos.csv` | 15 supuestos con estado de verificación |
| `03_HOJA_10_Validaciones.csv` | 10 hipótesis con criterio de éxito y de fracaso |
| `03_HOJA_11_Riesgos.csv` | 13 riesgos con exposición calculada |
| `03_HOJA_12_Plan_30_dias.csv` | 16 acciones con responsable, entregable y costo |
| `04_FUENTES.csv` | Base de fuentes: título, institución, fechas, URL, cobertura, sector, nivel, cifra soportada, confiabilidad y limitaciones |
| `05_GRAFICAS.html` | 11 gráficas con fuente, año, unidad y cobertura |
| `06_FICHAS_TOP10.md` | Una ficha por cada una de las diez mejores oportunidades |
| `07_KIT_VALIDACION.md` | Guiones, mensajes, formulario, prototipo, prueba de humo, piloto, carta de intención y criterios de interpretación |
| `08_PLAN_INVERSION.md` | Presupuestos por tramo, herramientas, roles, cronograma, punto de equilibrio y compuertas de decisión |

---

## Cómo rastrear cualquier cifra

Toda cifra es trazable en tres saltos:

```
Gráfica o informe  →  hoja CSV del Archivo 3  →  id_fuente (F01…F36)  →  04_FUENTES.csv  →  URL
```

Ejemplo: la afirmación «2.440 pequeños productores» aparece en el informe ejecutivo → dato `D43` en `03_HOJA_02_Datos_Uraba.csv` → fuente `F09` → declaración pública del presidente de Uniban, septiembre de 2025, con URL en `04_FUENTES.csv`.

---

## Sobre los formatos

El entorno de ejecución no permitió compilar archivos binarios, por lo que el paquete se entrega en formatos abiertos equivalentes:

| Solicitado | Entregado | Cómo obtener el formato original |
|---|---|---|
| PDF + DOCX | `01_INFORME_EJECUTIVO.html` | Abrir en navegador → Ctrl+P → *Guardar como PDF*. Los estilos de impresión ya están incluidos. Para DOCX: abrir el HTML desde Word |
| XLSX (12 hojas) | 12 archivos CSV con fórmulas vivas | Abrir cada CSV en Excel y guardarlo como hoja de un mismo libro. Las fórmulas `=SUM()`, `=C8+C9`, `=C12*C4*12` se calculan al abrir |
| Gráficas (imágenes) | `05_GRAFICAS.html` (SVG vectorial) | Clic derecho sobre cualquier gráfica → guardar imagen; o imprimir a PDF |

Si necesita los binarios nativos, pídalos cuando el entorno de ejecución esté disponible.

---

## Advertencias que conviene leer antes de invertir

1. **El cliente no puede ser la microempresa de Urabá.** El 96 % del tejido son microempresas, el 91 % tiene menos de cinco empleados y solo el 29,1 % sobrevive cinco años. El universo solvente son **93 empresas** (77 medianas + 16 grandes).
2. **El mercado local es pequeño.** Ni siquiera el escenario optimista (COP 187 millones anuales) sostiene tres salarios de mercado dentro de Urabá. La oportunidad es una cabeza de playa, no un negocio final.
3. **El punto de equilibrio con salarios exige 17 clientes**, más de la mitad del SAM regional completo. O los socios reinvierten dos años, o se expande fuera de Urabá antes del mes 18. No hay tercera opción.
4. **El banano no está cubierto por el EUDR.** Cualquier plan basado en esa premisa habría sido una pérdida completa de capital.
5. **La incógnita decisiva es barata de resolver.** Basta ir a cinco acopios en día de recepción y fotografiar lo que se usa hoy.
6. **Ninguna cita de este paquete es una entrevista.** Toda la investigación es documental. Es la razón de que la conclusión sea VALIDATE y no GO.
