# 14 — Costos y recursos

> **Ningún recurso de pago se ha creado.** Este documento existe para que la decisión de crearlos
> se tome con la información delante. Ver la autorización requerida al final.

## Recursos que exige Production

| # | Recurso | ¿Existe? | ¿Genera costo? |
| --- | --- | --- | --- |
| 1 | Servicio web Production (contenedor) | No | Sí, por uso |
| 2 | PostgreSQL Production (instancia **nueva**, separada de Demo) | No | Sí, por uso y volumen |
| 3 | Volumen para `/app/keys` Production | No | Sí, por GB-segundo |
| 4 | Bucket R2 Production (separado del de Demo) | Por confirmar | Cloudflare R2: gratis hasta 10 GB/mes |
| 5 | Credenciales R2 acotadas a ese bucket | No | No |
| 6 | Certificado X.509 para Data Protection | No | No |

Los tres primeros son los que mueven la factura. El bucket de R2 es aparte de Railway y, al
volumen de imágenes de cinco a diez negocios, cae holgadamente dentro del plan gratuito.

## Precios de Railway

Consultados el 26 de julio de 2026 y **pendientes de reverificar antes de crear nada**:

| Concepto | Precio |
| --- | --- |
| Plan Hobby | Mínimo USD 5/mes, que se acreditan como uso |
| Memoria | USD 0,00000386 por GB-segundo |
| CPU | USD 0,00000772 por vCPU-segundo |
| Volumen | USD 0,00000006 por GB-segundo |
| Egreso | USD 0,05 por GB |

Hobby admite hasta 10 volúmenes, lo que alcanza para los cuatro que quedarían en uso (PostgreSQL
y llaves, por ambiente).

## Impacto de duplicar el ambiente

Hoy Demo consume el crédito de USD 5 de Hobby. Production **duplica** los tres recursos que
cobran por uso: un contenedor web más, una base más y un volumen más.

Estimación de orden de magnitud, no una cotización:

- Un contenedor pequeño y una base pequeña, funcionando de continuo, consumen del orden de USD 3
  a 6 mensuales **cada ambiente**, según memoria asignada y tráfico.
- Con Demo y Production activos a la vez, es razonable esperar que **el consumo supere el crédito
  de USD 5 incluido en Hobby** y aparezca un excedente facturable.

**Los números reales sólo salen del estimador de Railway con la configuración concreta.** No se
declara aquí una cifra que después no se cumpla.

## Cómo evitar el excedente

Tres caminos, en orden de preferencia:

1. **Apagar Demo cuando Production esté en marcha.** Demo cumplió su papel: enseñar el producto.
   Si se necesita puntualmente, se enciende. Es el camino que probablemente mantiene todo dentro
   del crédito de Hobby.
2. **Reducir la memoria asignada** a ambos servicios al mínimo con que arranquen holgados.
3. **Aceptar el excedente**, que requiere autorización expresa.

## Guardas de gasto

- Alerta suave configurada en USD 4.
- Límite duro mensual en USD 5, si la cuenta lo permite.
- Una réplica por servicio. Sin autoescalado.
- Volumen de llaves pequeño; volumen de PostgreSQL dimensionado por métricas reales.
- **No subir de plan ni confirmar método de pago sin autorización expresa.**

## Autorización requerida

Antes de crear cualquiera de los recursos 1, 2 y 3:

- [ ] Reverificar los precios vigentes en Railway
- [ ] Decidir si Demo permanece encendida
- [ ] Estimar el consumo con la configuración concreta en el estimador
- [ ] **Autorización explícita de la persona responsable si el resultado excede el plan actual**

Si el ambiente cabe sin costo adicional dentro de Hobby, se documenta aquí y se procede. Si
requiere pago adicional, **el trabajo se detiene en este punto** hasta la autorización.
