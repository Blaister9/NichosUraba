# Costos y límites

Precios consultados el 26 de julio de 2026 en la página oficial de Railway:

- Free: USD 0/mes; prueba de 30 días con USD 5 de crédito y luego USD 1/mes. Después de la prueba
  permite un solo volumen, insuficiente para PostgreSQL más `/app/keys`.
- Hobby: mínimo USD 5/mes, incluidos como crédito de uso; admite hasta 10 volúmenes.
- Pro: mínimo USD 20/mes.
- Uso: memoria USD 0.00000386/GB-segundo, CPU USD 0.00000772/vCPU-segundo, volumen
  USD 0.00000006/GB-segundo y egreso USD 0.05/GB.

La opción mínima sostenible para este diseño después de la prueba es Hobby. El gasto real depende
del consumo del web y PostgreSQL; debe revisarse el estimador de Railway antes de confirmar.

## Guardas

- Una réplica por servicio; sin autoscaling horizontal.
- Volumen de llaves pequeño y volumen PostgreSQL según métricas.
- Crear alerta suave a USD 4 y límite duro mensual a USD 5 si Railway lo ofrece para la cuenta.
- Alertar también por CPU, memoria, disco y reinicios.
- No subir de plan ni confirmar método de pago sin autorización expresa.

Si la cuenta está en prueba gratuita, se puede desplegar sin tarjeta mientras haya crédito y dos
volúmenes disponibles. Al acercarse el vencimiento, exportar backup o solicitar autorización para
Hobby antes de continuar.
