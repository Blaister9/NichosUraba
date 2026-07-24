# Informe técnico completo
## Investigación de mercado y selección de nicho tecnológico en Urabá

**Archivo 2 de 8** · Elaborado el 24 de julio de 2026 · Todas las fuentes consultadas el 24 de julio de 2026

---

## 1. Metodología

### 1.1 Diseño

Investigación documental orientada a una decisión de inversión, ejecutada en cuatro fases:

1. **Delimitación y perfil económico.** Establecer qué territorio se analiza con una fuente institucional y levantar la demografía empresarial.
2. **Búsqueda de problemas por sector.** Doce líneas de búsqueda paralelas: economía y demografía empresarial; comercio y servicios; agroindustria y cadena alimentaria; transporte, logística y actividad portuaria; construcción y contratistas; turismo; salud y servicios profesionales; cumplimiento, SST y gestión documental; procesos de campo; transformación digital y software existente; contratación pública y privada; competidores, precios y sustitutos.
3. **Generación, puntuación y falsación de oportunidades.** Dieciséis oportunidades, matriz ponderada de 100 puntos, pruebas de falsación sobre las cinco mejores y reajuste de puntajes.
4. **Dimensionamiento y decisión** contra un umbral definido *a priori*.

### 1.2 Regla que gobernó toda la investigación

Se prohibió convertir «baja digitalización» en «oportunidad de software». Cada problema debía expresarse como un proceso observable: *una persona concreta ejecuta una tarea concreta de determinada manera, con determinada frecuencia, generando un costo o riesgo identificable*. Los enunciados amplios —«falta de tecnología», «procesos ineficientes», «poco uso de IA»— se rechazaron sistemáticamente.

### 1.3 Clasificación de evidencia

| Nivel | Definición | Uso permitido |
|---|---|---|
| **A** | Documentado explícitamente para Urabá o uno de sus municipios por fuente institucional | Sustenta inversión |
| **B** | Varias fuentes locales permiten inferirlo; ninguna lo mide directamente | Sustenta inversión junto con A |
| **C** | Hipótesis lógica sin evidencia local suficiente | Solo validación |

De las 36 fuentes del Archivo 4: **25 de nivel A, 8 de nivel B, 3 de nivel C.**

### 1.4 Topes normativos de puntuación (definidos antes de puntuar)

- Ninguna oportunidad supera **75** sin evidencia A o B sobre demanda.
- Ninguna supera **80** sin comprador concreto.
- Ninguna supera **85** sin evidencia verificada de gasto o solución actual.

Ninguna oportunidad alcanzó 85, por lo que el tercer tope no fue vinculante, pero sí impidió elevar las dos primeras.

### 1.5 Herramientas y limitaciones del método

Se usaron búsqueda web, consulta directa a documentos institucionales en PDF y consulta programática a la API de datos abiertos de SECOP II (dataset `jbjy-vk9h`). **No se realizaron entrevistas.** Ninguna cita de este informe es una entrevista; las declaraciones citadas son públicas y verificables. Esta es la limitación central del estudio y es la razón de que la conclusión sea VALIDATE y no GO.

---

## 2. Alcance geográfico

**Delimitación adoptada:** la jurisdicción de la Cámara de Comercio de Urabá — Apartadó, Arboletes, Carepa, Chigorodó, Dabeiba, Mutatá, Necoclí, San Juan de Urabá, San Pedro de Urabá y Turbo — con más de 543.000 habitantes y 11.664 km², según su Estudio Económico 2024.

Se eligió esta delimitación porque es la que produce las cifras empresariales usadas. **No coincide exactamente** con la subregión administrativa de la Gobernación de Antioquia. Toda cifra de este paquete declara su cobertura en la columna correspondiente.

**Restricción documentada:** el Estudio Económico 2024 de la CCU publica sus cifras únicamente agregadas para los diez municipios y **no las desagrega por municipio**. En consecuencia, ninguna cifra empresarial de este estudio se atribuye a Apartadó, Turbo, Carepa o Chigorodó por separado. La única fuente municipal específica localizada es el diagnóstico CCU–SENA de MYPES, referido exclusivamente a Apartadó.

---

## 3. Hallazgos sectoriales

### 3.1 Demografía empresarial — el hallazgo que reorientó el estudio

En 2024 la jurisdicción registró **12.134 empresas**, de las cuales el **96,02 % son microempresas** y el **91,34 % emplea entre 1 y 4 personas**. La supervivencia a cinco años de la cohorte 2019 fue del **29,1 %**. En 2024 se crearon 2.592 empresas y salieron 3.149.

> **Conclusión operativa:** el universo de compradores solventes en Urabá no son 12.134 empresas sino **93** (77 medianas + 16 grandes), o a lo sumo las **102** que emplean a 50 o más personas. Vender software por suscripción al resto del tejido empresarial produciría alta rotación, tickets bajos y costo de adquisición desproporcionado. El propio diagnóstico CCU–SENA de Apartadó documenta que las barreras principales de las MYPES son **financieras** y que, si bien más del 85 % declara disposición a participar en programas de digitalización, solo cerca del 30 % se considera preparado.

Dos señales adicionales apuntan en la misma dirección: las empresas de **información y comunicaciones** cayeron de 178 en 2016 a **122 en 2024** —el sector TIC local se contrae— y **transporte y almacenamiento** fue el único clúster con variación negativa en 2024 (−2,7 %).

### 3.2 Agroindustria del banano — donde está el dinero

Urabá concentró en 2025 **32.465 hectáreas de banano de exportación** y **82 millones de cajas**. Colombia exportó 2,5 millones de toneladas por **USD 1.309 millones**, con crecimiento del 21,6 %. Hay unas **320 fincas** de exportación, mayoritariamente afiliadas a Augura; Uniban opera 151 de ellas.

La estructura laboral está documentada con precisión inusual: Sintrainagro negocia con **85 grupos empresariales que poseen 275 fincas, cada una con su propia convención colectiva**, cubriendo unos **22.000 trabajadores**. Se negocian 10 labores comunes a todas las fincas y un procedimiento para otras 28. Augura publica un *Manual de labores en fincas bananeras* específico para Urabá y Magdalena.

Contexto de 2026: tras el récord de 2025, el sector advierte presión por aumento de costos, impacto de lluvias en Urabá y necesidad de un «precio justo» del retail europeo, con proyección de caída. Esto **aumenta** el interés por reducir gasto administrativo y **reduce** el apetito por gasto nuevo — una tensión que el argumento de venta debe resolver hablando de ahorro, no de modernización.

### 3.3 Plátano — el hallazgo con mejor relación evidencia/oportunidad

Urabá tiene unas **8.000 hectáreas de plátano** y cerca de **2.000 productores**, con un promedio de **3,6 hectáreas** por productor. El presidente de Uniban declaró en septiembre de 2025 que la empresa exporta **~4 millones de cajas de plátano al año provenientes de 2.440 pequeños agricultores independientes** de Chigorodó, Belén de Bajirá, Necoclí y Santa María la Antigua del Darién — aproximadamente **77.000 cajas semanales**. El exportador acompaña a estos productores «en todo el proceso: desde la formalización hasta el acceso a insumos y certificaciones».

Este es un **modelo agregador**: una sola relación comercial da acceso operativo a miles de productores. Es exactamente el patrón que un equipo pequeño necesita para no morir en costo de adquisición.

### 3.4 Puerto y logística — cambio estructural en curso

Puerto Antioquia inició operación comercial el **5 de febrero de 2026**, habilitado por la Resolución 20263040003075 del Ministerio de Transporte. Cuenta con muelle de 1.340 m, cinco posiciones de atraque, calado de 16,5 m y plataforma terrestre de 38 ha. Reemplaza el embarque por barcazas por vía fluvial que Uniban describía en 2025.

Las proyecciones oficiales del proyecto hablan de ~800 nuevas empresas y ~17.000 empleos indirectos. **Se clasifican como nivel C** y no se usan para dimensionar nada: son proyecciones del propio proyecto, no resultados observados. A tres meses de la apertura persistía indefinición sobre la titularidad de un corredor de acceso de 20 km entre Río Grande y Nueva Colonia.

### 3.5 Turismo

El alojamiento y los servicios de comida suman **1.887 empresas** (15,55 % del total regional). Necoclí concentró el **25,53 % de los visitantes de Urabá** en el primer trimestre de 2025 y la zona alcanzó **100 % de ocupación hotelera en Semana Santa 2025**, la más alta de Antioquia. El aeropuerto de Carepa movilizó 186.174 pasajeros en 2024.

**Pero:** la supervivencia a cinco años del sector turístico en Urabá es del **22,5 %**, peor que el promedio regional, y el 99,6 % de esas empresas son microempresas. La ocupación del 100 % es un pico estacional, no una condición permanente. El sector es grande en número y frágil en capacidad de pago.

### 3.6 Sanidad vegetal

El ICA declaró a Urabá **área libre de Fusarium Raza 4 Tropical mediante la Resolución 095026 de 2021**, tras cinco años de vigilancia con 90 % de cobertura del área sembrada, y estableció medidas fitosanitarias para su mantenimiento. En 2024 adoptó el plan nacional (contexto de más de 575.000 ha en riesgo en 32 departamentos) y en 2026 reforzó la vigilancia. En diciembre de 2025 se confirmó un caso en Ecuador.

### 3.7 Certificaciones y regulación de mercado

La producción colombiana cuenta con GlobalGAP, GRASP, Rainforest Alliance y Fairtrade. Los minoristas exigen con frecuencia **doble certificación RA + GG**, tanto en fincas como en plantas de manipulación.

**Hallazgo de falsación relevante:** el Reglamento europeo de productos libres de deforestación (EUDR) cubre siete materias primas — ganado, cacao, café, aceite de palma, caucho, soya y madera — y **el banano no está incluido**. Se descartó por completo la hipótesis, inicialmente atractiva, de construir un producto de trazabilidad EUDR para el banano de Urabá. El EUDR sí aplicaría a la ganadería regional, con cumplimiento desde el 30 de diciembre de 2026 para empresas grandes y medianas y desde el 30 de junio de 2027 para pymes.

### 3.8 Contratación pública — evidencia de gasto real en la región

Consulta a SECOP II (ciudad = Apartadó, término «software»):

| Entidad | Objeto | Valor | Año | Proveedor |
|---|---|---:|---|---|
| CORPOURABÁ | Actualización, soporte y mantenimiento del software SINAP V6 | COP 44.629.200 | 2025 | Integral V6 S.A.S. |
| Terminal de Transporte de Apartadó | Software INTEGRA + facturación electrónica cloud e-GOPETT | COP 33.476.280 | 2025 | Consultores Tecnológicos S.A.S. |
| CORPOURABÁ | Mismo objeto, vigencia anterior | COP 13.600.000 | 2022 | Integral V6 S.A.S. |

Estos contratos prueban que **sí existe gasto en software en Urabá**, que hay proveedores establecidos con relaciones vigentes, y que el orden de magnitud anual por comprador institucional está entre COP 33 y 45 millones. El crecimiento de 13,6 a 44,6 millones en el mismo objeto entre 2022 y 2025 es una señal de que ese gasto se expande.

*Limitación:* la consulta se restringió a una ciudad y a un término libre. Una búsqueda exhaustiva por objeto contractual en los cuatro municipios arrojaría más casos.

---

## 4. Análisis competitivo

| Competidor | Tipo | Amenaza | Comentario |
|---|---|---|---|
| **Agrosoft LATAM (XASS)** | ERP especializado en banano | **Alta** | 30 años, +40.000 ha gestionadas, 7 países. Su módulo SCM1 incluye explícitamente «tarja, asignación de precios y liquidación de fruta». Su nómina calcula IESS (Ecuador), no PILA ni nómina electrónica DIAN. Sin cliente verificado en Colombia ni en Urabá. |
| **Excel y WhatsApp** | Sustituto improvisado | **La más alta de todas** | Gratis, conocido, flexible. Gana casi siempre. La brecha solo existe si el volumen y el requisito de trazabilidad superan lo que Excel puede sostener. |
| **Desarrollo propio del cliente** | Sustituto interno | **Alta y no verificada** | Uniban es una empresa grande con capacidad de TI. Es la hipótesis que más puede matar la oportunidad principal, y debe verificarse en la primera entrevista. |
| **MyRA (Rainforest Alliance)** | Plataforma del certificador | Media | Gratuita para el titular del certificado. Cubre el trámite, no la generación diaria de evidencia. |
| **CTAIMA / Verifty / GCG Control / Twind** | Gestión de contratistas y SG-SST | Alta en su categoría | CTAIMA declara red de más de 100.000 contratistas. Categoría saturada. |
| **Consultores (certificación y SG-SST)** | Sustituto humano | Media | Relación establecida. Precios de referencia SG-SST: asesoría desde COP 349.000/mes; mantenimiento COP 250.000–680.000/mes. |
| **SM Solución** | Software agrícola | **No verificable** | Sitio en Google Sites sin fecha, sin clientes, sin precios. Se registra pero no se cuenta como competencia real. |

**Ningún proveedor de software especializado publica precios.** Se registra la ausencia y no se estima ninguna cifra, conforme a las reglas de veracidad de este estudio.

---

## 5. Matriz de puntuación

Ponderación aplicada: evidencia local de demanda 25 · severidad 15 · frecuencia 10 · comprador y presupuesto 10 · evidencia de gasto o solución actual 10 · acceso a primeros clientes 8 · brecha frente a competidores 8 · viabilidad del MVP 6 · ingresos recurrentes 4 · escalabilidad 2 · riesgo bajo 2.

| # | Oportunidad | Puntaje | Evid. | Decisión |
|---|---|---:|---|---|
| 1 | Recepción y liquidación de fruta de pequeños productores | **76** | A/C | Validar — hipótesis líder |
| 2 | Nómina de campo por labores (convención Sintrainagro) | 71 | A | Validar — solo como módulo |
| 3 | Expediente digital de auditoría de certificaciones | 70 | B | Validar — alternativa |
| 4 | Bitácora de bioseguridad Foc R4T | 69 | A/B | Validar por vía institucional |
| 5 | Consola SG-SST multiempresa para consultores | 67 | B | Descartar como apuesta principal |
| 6 | Habilitación documental de contratistas | 65 | C | Descartar |
| 7 | Transporte de fruta a Puerto Antioquia | 63 | A/C | Vigilar 12 meses |
| 8 | Transporte de personal a fincas | 63 | C | Vigilar |
| 9 | GMAO en empacadoras y flota | 63 | C | Vigilar |
| 10 | Tablero multi-finca sobre Excel | 63 | B | Producto puente |
| 11 | Insumos y periodo de carencia | 62 | C | Descartar |
| 12 | Pedidos por WhatsApp | 57 | C | Descartar |
| 13 | Actas de obra para contratistas | 55 | B | Descartar |
| 14 | Reservas turísticas Necoclí–Capurganá | 54 | A/C | Descartar |
| 15 | Historia clínica en IPS pequeñas | 51 | C | Descartar |
| 16 | Trazabilidad ganadera y guías ICA | 48 | C | Descartar — vigilar por EUDR |

Detalle criterio por criterio, con justificación y nivel de confianza: `03_HOJA_07_Scoring.csv`.

---

## 6. Pruebas de falsación

Se intentó activamente demostrar que las cinco mejores oportunidades eran malas inversiones.

| # | Argumento en contra | Resultado | Ajuste |
|---|---|---|---:|
| 1 | Agrosoft SCM1 ya hace liquidación de fruta; Uniban puede desarrollar internamente | **Parcialmente exitosa.** La brecha real no es el proceso sino el segmento: captura offline en acopios rurales con miles de microproductores y cumplimiento colombiano. Brecha más estrecha de lo que parecía. | −6 |
| 2 | La nómina es un sistema crítico; exige PILA, nómina electrónica DIAN y una convención distinta por finca | **Exitosa.** Degradada de candidata principal a módulo complementario de captura de labores. | −9 |
| 3 | Rainforest Alliance provee MyRA gratis y las consultoras venden el acompañamiento | **Parcial.** MyRA cubre el trámite, no la evidencia diaria. Se mantiene con puntaje reducido. | −5 |
| 4 | La bioseguridad es un bien público sectorial: la financiaría el ICA o Augura, no cada finca | **Exitosa en el modelo de ingresos.** Reclasificada como contrato institucional único, no SaaS por finca. | −7 |
| 5 | El mercado de SG-SST está saturado con actores de red instalada masiva | **Exitosa.** Descartada como apuesta principal. | −10 |

**Falsación transversal adicional:** se descartó por completo la hipótesis de trazabilidad EUDR para banano al verificar que el banano no está entre las siete materias primas cubiertas por el reglamento.

---

## 7. Estimación de mercado

**Fórmula:** Ingreso anual = (compradores × precio mensual) × 12 + (implementaciones × tarifa). Moneda: COP.

| Métrica | Conservador | Base | Optimista |
|---|---:|---:|---:|
| TAM Colombia (~120 compradores × COP 9 M/año) | COP 1.080 M/año | COP 1.080 M/año | COP 1.080 M/año |
| SAM Urabá (6 comercializadoras + 25 acopios) | COP 415 M/año | COP 415 M/año | COP 415 M/año |
| Clientes a 24 meses | 1 + 3 | 2 + 6 | 3 + 10 |
| **SOM — ingreso recurrente anual** | **COP 59,6 M** | **COP 119,3 M** | **COP 187,2 M** |
| Implementaciones acumuladas | COP 15,5 M | COP 31,0 M | COP 49,0 M |
| Margen bruto | 70 % | 76 % | 80 % |
| Costo de adquisición por cliente | COP 3,5 M | COP 2,4 M | COP 1,6 M |
| Tiempo hasta el primer contrato pago | 7 meses | 4 meses | 2 meses |

**Nivel de confianza: bajo.** El modelo descansa sobre dos variables no validadas: el precio (hipótesis anclada en precios públicos de SG-SST, software contable y contratos públicos de Urabá) y el número de sitios de acopio (inferencia explícita de 2.440 productores ÷ ~150 por acopio). Ambas están marcadas como tales en `03_HOJA_09_Supuestos.csv`.

**Punto de equilibrio.** Con costo fijo mensual de COP 2.145.000 (sin salarios) y margen del 76 %, se requieren **3 clientes**. Con salarios de mercado (COP 14.145.000 mensuales), se requieren **17 clientes** — más de la mitad del SAM regional completo. El negocio solo se sostiene si los socios reinvierten durante los primeros dos años **o** si se expande fuera de Urabá antes del mes 18.

---

## 8. Recomendación

**VALIDATE.** Se autoriza COP 6.900.000 y 30 días de trabajo de campo sobre la oportunidad #1. **No se autoriza construir el MVP.**

Contraste contra el umbral de once condiciones definido *a priori*: se cumplen nueve. Fallan dos, y son precisamente las que protegen el capital:

- **Solución actual verificable — NO CUMPLE.** No se pudo comprobar con fuentes públicas qué usa hoy una comercializadora de Urabá para liquidar fruta de pequeños productores.
- **Al menos tres señales independientes de demanda — NO CUMPLE.** Se documentaron dos sólidas.

Ambas se resuelven con visitas de campo que cuestan menos del 15 % del MVP. Construir antes de resolverlas sería invertir sobre una inferencia.

Criterios de GO, VALIDATE-2 y NO-GO: Archivo 7, sección 11. Compuertas de desembolso: Archivo 8, sección 9.

---

## 9. Limitaciones

1. **Toda la investigación es documental. No se realizaron entrevistas.** Ninguna cita de este paquete es una entrevista.
2. **La CCU no desagrega sus cifras por municipio.** Ninguna cifra empresarial se atribuye a un municipio individual, salvo el diagnóstico CCU–SENA, referido solo a Apartadó.
3. **No se localizó el Estudio Económico 2025 de la CCU** en su portal público a la fecha de consulta. El dato más reciente disponible es el de 2024, publicado en enero de 2025. Puede solicitarse directamente a `contacto@ccuraba.org.co`.
4. **No fue posible elaborar la gráfica de empresas por municipio**, por la limitación anterior. Se deja constancia de la ausencia en lugar de estimar un reparto: un reparto inventado se citaría después como si fuera un dato.
5. **El número de sitios de acopio es una inferencia**, no un dato, y es la variable más frágil del modelo de mercado.
6. **Los precios propuestos son hipótesis**, no cotizaciones. Ningún proveedor de software especializado del sector publica precios.
7. **La consulta a SECOP II fue parcial:** una sola ciudad y un solo término de búsqueda.
8. **Las proyecciones de Puerto Antioquia** (800 empresas, 17.000 empleos) son del propio proyecto y se clasificaron como nivel C. No se usaron para dimensionar nada.
9. **Discrepancia registrada:** la superficie bananera de Urabá aparece como 32.465 ha (Augura, 2025) y como 35.123 ha (prensa económica, 2023–2024). Se prefirió la cifra de Augura por ser más reciente y provenir del gremio. Ambas se conservan en el Archivo 4.
10. **El entorno de ejecución no permitió compilar archivos binarios.** El informe ejecutivo se entrega en HTML imprimible a PDF y el libro de datos como doce hojas CSV con fórmulas vivas, en lugar de PDF/DOCX/XLSX nativos.

---

## 10. Control de calidad ejecutado

| Verificación | Resultado |
|---|---|
| Cada cifra tiene fuente identificada | Sí — columna `id_fuente` en todas las hojas |
| Documentos primarios abiertos y leídos directamente | 8 de las fuentes principales: Estudio Económico 2024 de la CCU (PDF completo), diagnóstico CCU–SENA (PDF completo), Fenalco Antioquia, portal CTDE, Agrosoft LATAM, declaración del presidente de Uniban, API de SECOP II, resolución del ICA sobre Foc R4T |
| Las fuentes dicen efectivamente lo citado | Verificado en los 8 documentos abiertos. La resolución del ICA aportó un detalle adicional (15 puestos de control operados por ICA y Augura) que **confirmó** la falsación aplicada a la oportunidad #4 |
| Duplicados eliminados | Sí — 36 fuentes únicas |
| Fechas y cobertura revisadas | Sí — año y cobertura en cada fila |
| Fórmulas del libro de datos | Verificadas fila por fila. Se evitaron funciones con separador de argumentos (`ROUND`, etc.) para que no fallen según la configuración regional de Excel |
| Suma de pesos de la matriz = 100 | Verificado con fórmula en la fila `VERIFICACION` de `03_HOJA_07_Scoring.csv` |
| Puntajes recalculados uno a uno | Verificados los 16 |
| Gráficas coinciden con los datos | Sí — todas construidas a partir de los valores de las hojas CSV |
| Oportunidades no son variaciones repetidas | Revisado — la #2 y la #11 se conservan porque tienen comprador, proceso y frecuencia distintos de la #1 |
| Recomendación contrastada contra el umbral | Sí — 9 de 11 condiciones. Resultado: VALIDATE |
| Evidencia contradictoria buscada activamente | Sí — cinco pruebas de falsación más la verificación del alcance del EUDR |
| Inferencias marcadas explícitamente | Sí — 15 supuestos con estado de verificación en `03_HOJA_09_Supuestos.csv` |
| Presupuestos cuadrados | Tramo 1: 4.730.000 + 2.170.000 = 6.900.000. Tramo 2 conservador: 48.300.000 + 15 % = 55.545.000. Base: 43.300.000 + 15 % = 49.795.000 |
| Plan de 30 días ejecutable | 16 acciones con responsable, día, municipio, entregable verificable y costo |

**Verificaciones que no se pudieron completar:** no se abrieron directamente los sitios de CTAIMA, Verifty, GCG Control ni Twind; su descripción proviene de resultados de búsqueda y está marcada con la confiabilidad correspondiente. Tampoco se abrió el texto íntegro del reglamento EUDR: la exclusión del banano se confirmó con múltiples resultados independientes, incluida la página oficial de la Comisión Europea.

---

## 11. Conclusiones que cambian una decisión

1. **El cliente no puede ser la microempresa de Urabá.** 96 % del tejido son microempresas, 91 % tiene menos de cinco empleados, y menos de un tercio sobrevive cinco años. El universo solvente son 93 empresas.
2. **El dinero está en la cadena agroexportadora,** no en el registro mercantil general.
3. **El modelo agregador es la única forma viable de adquisición** para un equipo de tres personas: una relación con una comercializadora da acceso a miles de productores; una relación con un consultor SST da acceso a decenas de mipymes.
4. **El banano no está cubierto por el EUDR.** Cualquier plan basado en esa premisa habría sido una pérdida completa de tiempo y capital.
5. **El mercado de Urabá es demasiado pequeño para sostener el negocio por sí solo.** Conviene decidirlo antes de empezar, no después: la ruta a Magdalena, La Guajira y Cesar (20.478 ha adicionales) no es opcional, es parte del plan.
6. **La incógnita decisiva cuesta menos del 15 % del MVP.** Basta con fotografiar el formato que hoy se usa en cinco sitios de acopio de Chigorodó y Carepa.
