# Fichas de oportunidad — top 10

**Archivo 6 de 8.** Una ficha por oportunidad, ordenadas por puntaje. Cada una es autocontenida y puede imprimirse por separado.

Leyenda de niveles: **A** = evidencia directa para Urabá · **B** = indirecta fuerte · **C** = hipótesis plausible sin evidencia local.

---

## Ficha 1 · Recepción y liquidación de fruta de pequeños productores — **76/100** · VALIDAR

| | |
|---|---|
| **Nicho** | Comercializadoras exportadoras de plátano y banano que compran a pequeños productores independientes |
| **Municipio inicial** | Chigorodó (eje Chigorodó–Carepa–Apartadó) |
| **Usuario directo** | Recibidor o inspector de calidad en el sitio de acopio |
| **Quien sufre** | Coordinador del acopio y auxiliar administrativo que liquida |
| **Quien aprueba y paga** | Gerente de Operaciones o Director de Fruta de la comercializadora |
| **Nivel de evidencia** | **A** sobre la existencia y escala del flujo · **C** sobre el método actual |

**Problema.** El recibidor registra en el acopio qué productor entregó cuántas cajas, con qué calidad y cuánto se rechazó; después, en oficina, ese registro se convierte en la liquidación semanal de pago, aplicando precio por calidad y descontando insumos y anticipos.

**Proceso actual.** *No verificado.* Hipótesis: planilla física en el acopio y transcripción posterior a Excel.

**Frecuencia.** Diaria en cosecha; liquidación semanal.

**Consecuencia.** Disputas de pago con el productor, reproceso administrativo y dificultad para reconstruir la trazabilidad que exigen las certificaciones y el comprador extranjero.

**Evidencia local.** El presidente de Uniban declaró en septiembre de 2025 que la empresa exporta ~4 millones de cajas de plátano al año provenientes de **2.440 pequeños agricultores independientes** de Chigorodó, Belén de Bajirá, Necoclí y Santa María la Antigua del Darién — unas 77.000 cajas semanales. Complementariamente: ~8.000 ha y ~2.000 productores de plátano en Urabá, con promedio de 3,6 ha por productor.

**Alternativas existentes.** Agrosoft LATAM (módulo SCM1, «liquidación de fruta»); desarrollos propios; Excel y papel.

**Brecha.** Captura offline en el punto de acopio, comprobante inmediato al productor y cumplimiento colombiano. Agrosoft está orientado a la finca grande y a la exportadora, con nómina y contabilidad bajo normativa ecuatoriana.

**Producto.** App de captura offline + panel web de consolidación y liquidación que exporta al Excel que el cliente ya usa.
**Formato.** PWA offline-first (IndexedDB + cola de sincronización) sobre .NET 8 y PostgreSQL.
**MVP (8 semanas).** Captura offline · sincronización diferida · comprobante al productor · consolidado por acopio y productor · motor de liquidación · exportación a Excel · bitácora inmutable.
**Modelo de ingresos.** Suscripción mensual por sitio de acopio + implementación única.
**Precio hipotético.** COP 690.000/mes por acopio · COP 2.900.000/mes por comercializadora. *Hipótesis, no cotización.*
**Canal.** Cámara de Comercio de Urabá · Augura · Fenalco Antioquia seccional Urabá · visita directa a acopios.

**Riesgos.** Que ya esté resuelto · mercado local pequeño · concentración en Uniban · ciclo de decisión largo.
**Evidencia faltante.** Cómo se registra hoy · cuánto cuesta el reproceso · quién aprueba la compra · tolerancia al precio.

---

## Ficha 2 · Liquidación de nómina de campo por labores — **71/100** · VALIDAR (solo como módulo)

| | |
|---|---|
| **Nicho** | Grupos empresariales bananeros de Urabá bajo convención colectiva con Sintrainagro |
| **Usuario directo** | Supervisor de campo |
| **Quien aprueba y paga** | Gerente administrativo del grupo |
| **Nivel de evidencia** | **A** |

**Problema.** Liquidar semanalmente la nómina de campo aplicando tarifas por labor, cuadrilla y lote, según la convención colectiva propia de cada finca.

**Proceso actual.** Software especializado, ERP colombiano o desarrollo interno.

**Frecuencia.** Semanal, sin excepción.

**Consecuencia.** Los errores generan conflicto laboral, retroactivos y riesgo de sanción; el sindicato revisa.

**Evidencia local.** Sintrainagro negocia con **85 grupos empresariales que poseen 275 fincas, cada una con su propia convención colectiva**, cubriendo unos **22.000 trabajadores**. Se negocian 10 labores comunes a todas las fincas y un procedimiento para otras 28 labores surgidas en la actividad. Augura publica un *Manual de labores en fincas bananeras* específico para Urabá y Magdalena.

**Brecha.** El módulo de nómina de Agrosoft calcula IESS (Ecuador), no PILA ni nómina electrónica DIAN.

**Producto.** Módulo de captura de labores en campo que **alimenta** la nómina existente, sin reemplazarla.
**MVP.** Parte diario por cuadrilla, lote y labor, con exportación al layout de nómina vigente del cliente.
**Precio.** No estimado; requiere validación previa.

**Riesgos.** Sustituir un sistema crítico · el MVP no cabe en 8 semanas si se intenta la nómina completa · ciclo de venta largo.
**Falsación aplicada (−9 puntos).** Ningún grupo bananero cambia su nómina por un proveedor nuevo de tres personas. Degradada de candidata principal a módulo complementario.

---

## Ficha 3 · Expediente digital de auditoría de certificaciones — **70/100** · VALIDAR (alternativa a la #1)

| | |
|---|---|
| **Nicho** | Fincas y empacadoras bananeras certificadas de Urabá (~320 fincas) |
| **Usuario directo** | Auxiliar de calidad de la finca |
| **Quien aprueba y paga** | Jefe de Calidad o HSEQ del grupo bananero |
| **Nivel de evidencia** | **B** |

**Problema.** Generar y conservar los registros diarios que exigen GlobalGAP, GRASP, Rainforest Alliance y Fairtrade, y prepararlos para la auditoría anual.

**Proceso actual.** Papel, Excel, consultores de certificación y la plataforma MyRA del propio certificador.

**Frecuencia.** Registro diario; auditoría anual.

**Consecuencia.** Perder o suspender una certificación equivale a perder acceso a los supermercados europeos, que reciben la mayor parte del banano colombiano.

**Evidencia local.** Los minoristas y distribuidores exigen con frecuencia **doble certificación RA + GG**, tanto en fincas como en las plantas donde se manipula la fruta. La producción colombiana cuenta con GlobalGAP, GRASP, Rainforest Alliance y Fairtrade.

**Brecha.** MyRA cubre el trámite ante el certificador, no la generación diaria de evidencia en finca.

**Producto.** Expediente digital con checklists configurables por norma y evidencia fotográfica fechada.
**MVP.** Checklist por norma + evidencia fotográfica + exportación del paquete de auditoría.
**Modelo.** Suscripción anual por finca certificada.

**Falsación aplicada (−5 puntos).** Rainforest Alliance provee MyRA gratuitamente a los titulares de certificado y las consultoras ya venden el acompañamiento documental.
**Evidencia faltante.** Cuántas horas cuesta hoy preparar una auditoría y quién las paga.

---

## Ficha 4 · Bitácora digital de bioseguridad fitosanitaria (Foc R4T) — **69/100** · VALIDAR por vía institucional

| | |
|---|---|
| **Nicho** | Fincas bananeras y plataneras bajo el estatus de área libre de Foc R4T |
| **Usuario directo** | Portero y jefe de sanidad vegetal |
| **Quien pagaría** | Augura o el ICA — **no la finca individual** |
| **Nivel de evidencia** | **A** en la norma · **B** en la operación |

**Problema.** Registrar el ingreso de personas y vehículos y los puntos de desinfección exigidos para mantener el estatus de área libre.

**Proceso actual.** Planilla física en portería. No se identificó software específico.

**Consecuencia.** Perder el estatus de área libre afectaría a toda la región, no a una sola finca.

**Evidencia local (verificada por lectura directa de la fuente).** El ICA declaró a Urabá **área libre de Foc R4T mediante la Resolución 095026 de 2021**, tras cinco años de vigilancia con 90 % de cobertura del área sembrada. La resolución aplica en Arboletes, Apartadó, Carepa, Chigorodó, Necoclí, San Pedro de Urabá, San Juan de Urabá y Turbo (Antioquia), y en Acandí, Belén de Bajirá, Carmen del Darién, Riosucio y Unguía (Chocó). **El ICA y Augura tienen instalados 15 puestos de control en Urabá**, con acompañamiento de la DITRA, la Policía Nacional y el Ejército, donde se inspecciona la movilización de material vegetal y se realiza lavado y desinfección de vehículos. Más de 46.000 personas capacitadas. En 2026 el ICA reforzó la vigilancia; en diciembre de 2025 se confirmó un caso en Ecuador.

**Falsación aplicada (−7 puntos), y la fuente la confirma.** La red de 15 puestos de control la operan y financian el ICA y Augura, no la finca individual. La bioseguridad es un bien público sectorial. Sin autoridad de compra privada, esto no es un SaaS por finca sino un contrato institucional único, con ciclo de venta muy largo.

**Cómo abordarla si se quiere.** No vender a fincas. Presentar a Augura una propuesta de digitalización de los 15 puestos de control ya existentes, con tablero agregado regional. Un solo comprador, un solo contrato.

---

## Ficha 5 · Consola multiempresa de SG-SST para consultores — **67/100** · DESCARTAR como apuesta principal

| | |
|---|---|
| **Nicho** | Consultores de SG-SST que administran carteras de 10 a 30 mipymes |
| **Comprador** | El propio consultor — **comprador agregador**: una relación da acceso a decenas de empresas |
| **Nivel de evidencia** | **B** |

**Problema.** Mantener el SG-SST de varias empresas pequeñas con plantillas duplicadas por cliente.

**Evidencia de gasto (la mejor de todo el estudio).** Precios publicados en Colombia: asesoría SG-SST mensual **desde COP 349.000**, diseño documental para microempresa **desde COP 690.000**, implementación en pyme entre **COP 1,5 y 3,5 millones**, y mantenimiento mensual entre **COP 250.000 y 680.000**.

**Precio hipotético.** COP 350.000/mes por consultor con hasta 15 empresas.

**Falsación aplicada (−10 puntos, la más severa).** El mercado está saturado: CTAIMA (con red declarada de más de 100.000 contratistas conectados), Verifty, GCG Control, Twind y decenas de productos listados en comparadores comerciales. Entrar sería competir en precio contra actores con efecto de red. **Se descarta como apuesta principal**, aunque la lógica del comprador agregador sigue siendo válida y vale la pena conservarla como patrón.

---

## Ficha 6 · Habilitación documental de contratistas — **65/100** · DESCARTAR

| | |
|---|---|
| **Nicho** | Empresas de Urabá que reciben contratistas en finca, empacadora o zona portuaria |
| **Comprador** | Coordinador HSEQ o de Compras |
| **Nivel de evidencia** | **C** para Urabá |

**Problema.** Recolectar mensualmente los soportes de seguridad social, exámenes médicos y cursos antes de autorizar el ingreso de cada contratista.

**Por qué se descarta.** El problema es real a nivel nacional pero **no está documentado para Urabá**, y el mercado tiene competidores con red instalada masiva. La brecha es prácticamente nula.

---

## Ficha 7 · Coordinación de transporte de fruta hacia Puerto Antioquia — **63/100** · VIGILAR 12 MESES

| | |
|---|---|
| **Nicho** | Exportadoras y transportadores que despachan hacia el terminal de Turbo |
| **Comprador** | Jefe de Logística de la exportadora |
| **Nivel de evidencia** | **A** sobre el cambio · **C** sobre el proceso |

**Evidencia local.** Puerto Antioquia inició operación comercial el **5 de febrero de 2026** (habilitación por Resolución MinTransporte 20263040003075 del 29 de enero de 2026), con muelle de 1.340 m, cinco posiciones de atraque y calado de 16,5 m. Sustituye el embarque por barcazas por vía fluvial que Uniban describía en 2025. A tres meses de la apertura persistía indefinición sobre un corredor de acceso de 20 km entre el corregimiento Río Grande y Nueva Colonia.

**Por qué solo vigilar.** El comprador es difuso, hay dependencia de una sola gran empresa y es muy probable que el puerto implante su propio sistema comunitario. **Revisar en 12 meses**, cuando se sepa qué plataforma adoptó el puerto y qué quedó sin cubrir.

---

## Ficha 8 · Gestión de transporte de personal a fincas — **63/100** · VIGILAR

| | |
|---|---|
| **Comprador** | Jefe de Talento Humano de grupo bananero |
| **Nivel de evidencia** | **C** |

**Problema.** Programar rutas y cupos de buses para llevar trabajadores a las fincas y registrar novedades diarias.

**Evidencia.** Se infiere de la existencia de 22.000 trabajadores en 275 fincas dispersas. **No documentado directamente por ninguna fuente.** Frecuencia diaria y lógica clara, pero sin evidencia local no puede sustentar inversión.

---

## Ficha 9 · Gestión de mantenimiento (GMAO) en empacadoras y flota — **63/100** · VIGILAR

| | |
|---|---|
| **Comprador** | Jefe de Mantenimiento |
| **Nivel de evidencia** | **C** |

**Problema.** Programar y registrar el mantenimiento preventivo y correctivo de activos dispersos: empacadoras, cable vías, flota agrícola.

**Por qué solo vigilar.** Existe base instalada de activos, pero no hay evidencia local de dolor ni de gasto, y la categoría GMAO está madura y muy competida internacionalmente.

---

## Ficha 10 · Tablero de indicadores multi-finca sobre los Excel existentes — **63/100** · PRODUCTO PUENTE

| | |
|---|---|
| **Nicho** | Grupos empresariales de Urabá con tres o más unidades o fincas |
| **Usuario directo** | Analista o asistente de gerencia |
| **Quien aprueba y paga** | Gerente General |
| **Nivel de evidencia** | **B** |

**Problema.** Consolidar cada mes los Excel de varias unidades para producir el informe gerencial.

**Consecuencia.** Decisiones tardías y cifras que no concuerdan entre unidades.

**Evidencia local.** El diagnóstico CCU–SENA de Apartadó (2025) documenta que solo cerca del **25 % de las MYPES cuenta con procedimientos formales de gestión** y cerca del **60 % toma decisiones de forma centralizada en el propietario**, con bajo nivel de documentación de procesos.

**Brecha.** Ninguna técnica: Power BI y Excel avanzado existen. La brecha es de **capacidad y de tiempo**, no de herramienta.

**Producto.** Servicio de tablero gerencial montado sobre los Excel que el cliente ya tiene. No reemplaza nada.
**Formato.** Power BI o web ligera — encaja exactamente con la experiencia declarada del equipo en Microsoft Power Platform.
**Precio hipotético.** COP 4.500.000 de implementación + COP 450.000 mensuales de mantenimiento.

**Por qué está aquí a pesar de su bajo puntaje.** No es un buen producto por sí solo: se parece más a consultoría y no escala. Pero **dos implementaciones cubren íntegramente el presupuesto de validación de 30 días**, y cada una mete al equipo dentro de la operación real de un cliente — que es precisamente el acceso que la validación de la Ficha 1 necesita.

**Advertencia.** No puede consumir más del 30 % del tiempo del socio comercial durante el Tramo 1. Si lo hace, el equipo termina siendo una consultora sin producto, que es el desenlace más probable si nadie vigila esta línea.

---

### Oportunidades evaluadas y descartadas por debajo del top 10

| # | Oportunidad | Puntaje | Razón principal del descarte |
|---|---|---:|---|
| 11 | Control de insumos y periodo de carencia | 62 | Ya cubierto por módulos de los ERP agrícolas existentes |
| 12 | Pedidos por WhatsApp para distribuidores | 57 | Categoría extremadamente saturada; comprador microempresarial sin presupuesto |
| 13 | Actas de obra para contratistas municipales | 55 | Ciclo de venta público y riesgo de cartera altos para un equipo de tres |
| 14 | Reservas y cupos turísticos Necoclí–Capurganá | 54 | Microempresas con supervivencia a 5 años de 22,5 %; OTAs y alternativas gratuitas |
| 15 | Historia clínica en IPS pequeñas | 51 | Solo 90 empresas de salud registradas y en descenso; sector regulado y saturado |
| 16 | Trazabilidad ganadera y guías ICA | 48 | Sin evidencia local. **Vigilar:** el ganado sí está cubierto por el EUDR, con cumplimiento desde el 30-12-2026 |
