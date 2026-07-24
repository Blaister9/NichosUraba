# Plan de inversión y ejecución — Archivo 8 de 8

Todas las cifras en pesos colombianos (COP), julio de 2026. El plan tiene **dos tramos separados por una compuerta de decisión**: el segundo tramo no se desembolsa si el primero no produce el resultado esperado.

---

## 1. Presupuesto de validación — Tramo 1 (30 días)

**Se desembolsa ahora. Total: COP 6.900.000.**

| Concepto | Detalle | Monto |
|---|---|---:|
| Desplazamientos en Urabá | Apartadó ↔ Chigorodó ↔ Carepa ↔ Turbo · ~20 salidas | 1.850.000 |
| Combustible y peajes | Vehículo propio | 620.000 |
| Alimentación en terreno | 3 personas × 12 jornadas | 1.080.000 |
| Comunicaciones | Planes de datos y llamadas × 3 | 380.000 |
| Impresión de material | Guiones, fichas, cartas de intención, hoja de propuesta | 200.000 |
| Reserva de campo | Imprevistos de terreno | 600.000 |
| **Subtotal trabajo de campo** | *(coincide con la hoja `03_HOJA_12_Plan_30_dias.csv`)* | **4.730.000** |
| Herramientas de prototipado | Figma Professional 1 mes × 3 asientos | 180.000 |
| Hosting y dominio de prueba | 3 meses | 290.000 |
| Revisión jurídica | Carta de intención y política de tratamiento de datos (Ley 1581 de 2012) | 400.000 |
| Certificados del registro mercantil | Consulta de empresas por CIIU en la CCU | 300.000 |
| Contingencia (≈15 %) | | 1.000.000 |
| **TOTAL TRAMO 1** | | **6.900.000** |

**No incluye salarios de los tres socios**, que se asumen como aporte de capital de trabajo. Si se valoraran a precio de mercado (≈COP 4.000.000/mes × 3 × 1 mes × 40 % de dedicación), el costo real de oportunidad sería de unos COP 4.800.000 adicionales. Conviene tenerlo presente al evaluar el retorno.

---

## 2. Presupuesto de MVP — Tramo 2 (8 semanas)

**Se desembolsa SOLO si el Tramo 1 arroja GO** según los criterios del Archivo 7, sección 11.

| Concepto | Detalle | Conservador | Base |
|---|---|---:|---:|
| Desarrollo | 2 desarrolladores × 8 semanas (costo de oportunidad, no caja) | 25.600.000 | 25.600.000 |
| Diseño de interfaz | Contratado por 3 semanas | 4.500.000 | 3.200.000 |
| Infraestructura año 1 | VPS + PostgreSQL gestionado + respaldos + monitoreo | 4.200.000 | 3.400.000 |
| Dispositivos de prueba | 3 teléfonos Android gama media resistentes | 2.700.000 | 2.100.000 |
| Certificado SSL, tienda y firma de código | | 900.000 | 700.000 |
| Herramientas de desarrollo | Repositorio, CI, seguimiento de errores | 1.400.000 | 1.100.000 |
| Trabajo de campo del piloto | Visitas semanales durante una cosecha | 5.200.000 | 4.000.000 |
| Constitución de la SAS y contabilidad año 1 | | 3.800.000 | 3.200.000 |
| Contingencia | 15 % | 7.245.000 | 6.495.000 |
| **TOTAL TRAMO 2** | | **55.545.000** | **49.795.000** |
| *De los cuales salida real de caja* | *(excluye el costo de oportunidad del desarrollo)* | *29.945.000* | *24.195.000* |

---

## 3. Herramientas e infraestructura

| Capa | Elección | Justificación | Costo mensual |
|---|---|---|---:|
| Backend | .NET 8 + PostgreSQL | El equipo ya domina C# y .NET; PostgreSQL evita licencias | — |
| Cliente de campo | PWA con IndexedDB y cola de sincronización | Offline-first sin pasar por tiendas de aplicaciones; una sola base de código | — |
| Panel web | Blazor o React + TypeScript | Stack conocido por el equipo | — |
| Servidor | VPS en región de baja latencia (Bogotá o Miami) | Evita el costo variable impredecible de la nube grande | 280.000 |
| Base de datos gestionada | PostgreSQL con respaldo diario | Evita administrar respaldos manualmente | 190.000 |
| Almacenamiento de fotos | Object storage compatible con S3 | Barato y desacoplado | 60.000 |
| Monitoreo y errores | Nivel gratuito hasta el primer año | | 0 |
| Repositorio y CI | Nivel gratuito | | 0 |
| Correo y dominio | | | 45.000 |
| **Total infraestructura** | | | **575.000** |

**Regla de arquitectura innegociable:** el sistema debe funcionar completo con el teléfono en modo avión durante una jornada de recepción entera. Si esa prueba falla, el producto no sirve para Urabá.

---

## 4. Roles

| Socio | Rol | Dedicación tramo 1 | Dedicación tramo 2 |
|---|---|---|---|
| Ingeniero mecatrónico / desarrollador | Producto y arquitectura. Dueño del prototipo, del MVP y de la decisión técnica. | 40 % | 100 % |
| Socio comercial | Dueño del embudo. Consigue las entrevistas, las cartas de intención y el piloto. **Es el rol crítico del tramo 1.** | 80 % | 60 % |
| Tercer socio | Operación y datos. Ficha de entrevistas, síntesis, libro de datos, soporte en implementación. | 60 % | 80 % |

**Riesgo organizacional declarado (R11):** si solo una persona escribe código, el proyecto depende de una persona. Desde la semana 1 del Tramo 2 debe repartirse el dominio del código y documentarse.

---

## 5. Cronograma

```
        Mes 1        Mes 2      Mes 3     Mes 4    Mes 5    Mes 6
     ├──────────┤ ├──────────────────┤ ├───────────────┤ ├─────────┤
     TRAMO 1        COMPUERTA           TRAMO 2           PILOTO
     Validación     GO/VALIDATE-2/      MVP 8 semanas     1 cosecha
     30 días        NO-GO
                    │
                    ├── NO-GO ──> activar oportunidad #3 con el mismo protocolo
                    └── GO ─────> construir
```

| Hito | Fecha relativa | Criterio de cumplimiento |
|---|---|---|
| H0 · Inicio | Día 0 | Presupuesto tramo 1 aprobado |
| H1 · 12 entrevistas completas | Día 14 | 12 fichas llenas con citas textuales |
| H2 · 5 acopios visitados | Día 14 | 5 fotografías del proceso real |
| H3 · Prototipo probado | Día 21 | 8 sesiones cronometradas |
| **H4 · Compuerta de decisión** | **Día 30** | **Acta firmada por los tres socios** |
| H5 · MVP funcional | Día 86 | Prueba en modo avión superada |
| H6 · Piloto iniciado | Día 100 | Línea base de 4 semanas tomada |
| H7 · Primer contrato pago | Día 180 | Factura emitida y pagada |

---

## 6. Costos mensuales de operación (a partir del Tramo 2)

| Concepto | Mensual |
|---|---:|
| Infraestructura (sección 3) | 575.000 |
| Contabilidad y cumplimiento tributario | 450.000 |
| Comunicaciones y desplazamientos de soporte | 900.000 |
| Herramientas de trabajo | 220.000 |
| **Costo fijo mensual (sin salarios)** | **2.145.000** |
| Salarios de mercado de 3 socios *(referencia, no desembolso inicial)* | 12.000.000 |
| **Costo fijo mensual con salarios** | **14.145.000** |

---

## 7. Punto de equilibrio preliminar

**Fórmula:** clientes necesarios = costo fijo mensual ÷ (precio mensual × margen bruto)

*Nota sobre el precio medio ponderado.* Se usa COP 1.100.000 y no el promedio aritmético de la mezcla del escenario base (COP 1.242.500, resultante de 2 comercializadoras a 2,9 M y 6 acopios a 690 mil), deliberadamente, para que el punto de equilibrio quede por el lado seguro. Con el promedio real el equilibrio del escenario B bajaría de 17 a 15 clientes, lo que no cambia la conclusión.

**Escenario A — sin salarios (los socios reinvierten):**

- Costo fijo: 2.145.000 · Margen bruto: 76 % · Precio medio ponderado: 1.100.000
- Ingreso neto por cliente: 1.100.000 × 0,76 = **836.000**
- **Punto de equilibrio: 2.145.000 ÷ 836.000 ≈ 2,6 → 3 clientes**

**Escenario B — con salarios de mercado:**

- Costo fijo: 14.145.000 · Ingreso neto por cliente: 836.000
- **Punto de equilibrio: 14.145.000 ÷ 836.000 ≈ 16,9 → 17 clientes**

> **Lectura obligatoria de estos dos números.** El escenario A es alcanzable: tres clientes cabe dentro del escenario base a 24 meses. El escenario B **no cabe dentro del SAM de Urabá**, que se estimó en 31 compradores direccionables en total. Alcanzar 17 clientes exigiría más de la mitad de todo el mercado regional. Es decir: **este negocio puede sostenerse solo si los socios reinvierten durante los primeros dos años, o si se expande fuera de Urabá antes del mes 18.** No hay una tercera opción, y conviene decidirlo antes de empezar y no después.

---

## 8. Metas a 30, 60 y 90 días

| Plazo | Meta | Medida | Verde | Amarillo | Rojo |
|---|---|---|---|---|---|
| **30 días** | Falsar o confirmar la hipótesis | Entrevistas completas | ≥ 12 | 8–11 | < 8 |
| | | Acopios observados | ≥ 5 | 3–4 | < 3 |
| | | Cartas de intención | ≥ 2 | 1 | 0 |
| | | Software competidor mencionado | 0–1 vez | 2 veces | ≥ 3 veces |
| **60 días** | MVP a mitad de camino | Captura offline funcionando | Sí | Parcial | No |
| | | Prueba en modo avión | Superada | Con fallas | Falla |
| | | Sitio piloto confirmado con fecha | Sí | En conversación | No |
| **90 días** | Piloto en marcha | Semanas de piloto ejecutadas | ≥ 4 | 2–3 | < 2 |
| | | Registros capturados en producción | ≥ 2.000 | 500–1.999 | < 500 |
| | | Reducción del tiempo de cierre | ≥ 40 % | 20–39 % | < 20 % |
| | | Segunda organización interesada | Sí | En conversación | No |

---

## 9. Condiciones para ampliar o detener la inversión

### Ampliar (desembolsar el Tramo 2 y luego crecer)

- **Día 30:** se cumplen los cuatro criterios de GO del Archivo 7.
- **Día 90:** el piloto muestra reducción de al menos 40 % en el tiempo de cierre **y** existe una segunda organización con fecha de piloto.
- **Día 180:** primer contrato pago firmado y facturado.

### Detener (NO-GO, sin prórroga)

- **Día 30:** tres o más entrevistados nombran un software que ya lo resuelve · o cero cartas de intención · o el precio máximo defendible queda bajo COP 400.000 mensuales.
- **Día 90:** el piloto no reduce el tiempo de cierre en al menos 20 %, o hay pérdida de registros en producción.
- **Día 180:** ningún contrato pago. *En este punto se han consumido unos COP 31 millones de caja real (6,9 del Tramo 1 más 24,2 del Tramo 2 en escenario base); detener aquí preserva la capacidad de intentar la siguiente hipótesis.*
- **En cualquier momento:** Agrosoft LATAM u otro proveedor anuncia un producto específico para pequeños productores en Colombia con precio por debajo del propuesto.

### Reasignar

Si se activa el NO-GO, el capital remanente y los contactos abiertos se trasladan a la oportunidad **#3 — expediente digital de auditoría de certificaciones** (70 puntos), que comparte comprador, canal y sector, y por tanto reutiliza casi todo el trabajo de campo ya hecho.

---

## 10. Producto puente para financiar la validación

La oportunidad **#10 — tablero de indicadores multi-finca sobre los Excel existentes** (63 puntos) no es un buen producto por sí sola, pero sí un buen servicio de entrada:

- Encaja exactamente con la experiencia declarada del equipo en Microsoft Power Platform.
- Hipótesis de precio: COP 4.500.000 de implementación y COP 450.000 mensuales de mantenimiento.
- Dos implementaciones cubren íntegramente el presupuesto del Tramo 1.
- Y sobre todo: **cada implementación mete al equipo dentro de la operación real de un cliente**, que es precisamente el acceso que la validación necesita.

Se recomienda venderlo en paralelo desde la semana 2, con una condición estricta: **no puede consumir más del 30 % del tiempo del socio comercial durante el Tramo 1.** Si lo hace, la validación se degrada y el equipo termina siendo una consultora sin producto — que es el desenlace más probable si nadie vigila esta línea.
