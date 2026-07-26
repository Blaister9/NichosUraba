# Runbook de Demo

## Preflight

- Confirmar rama y SHA desplegado.
- Confirmar build Release, 113 pruebas y ausencia de cambios pendientes del modelo.
- Confirmar que solo existen los negocios ficticios Salón Bella Urabá, Barbería El Corte y
  Restaurante Sazón Local en el directorio. El negocio de aislamiento permanece privado.
- Confirmar que las credenciales se entregaron por canal privado.

## Smoke público

1. `GET /health/live` y `GET /health/ready`: `200`.
2. Abrir `/` y los perfiles:
   `/negocios/salon-bella-uraba`, `/negocios/barberia-el-corte` y
   `/negocios/restaurante-sazon-local`.
3. Crear una cita, un turno y un pedido con identidades y teléfonos ficticios.
4. Conservar sus códigos solo durante la prueba, validar seguimiento y descartarlos.
5. Repetir a 390×844 y verificar que no exista scroll horizontal.

## Smoke privado

- Ingresar como administración Demo; verificar `/admin/negocios`.
- Crear un negocio `Draft` ficticio y no publicarlo.
- Verificar que una propietaria normal recibe `403` en la API global.
- Verificar que una cuenta de otro negocio no puede leer ni operar el recurso.
- Crear una cuenta piloto ficticia y comprobar redirección obligatoria al cambio de clave. No
  completar el cambio con una clave que vaya a compartirse.

## Reinicio controlado

Registrar conteos, reiniciar solo el servicio web, confirmar `ready=200`, repetir conteos e ingresar
con la sesión esperada. Validar que un dato protegido creado antes del reinicio sigue legible; esto
demuestra que `/app/keys` no se perdió.

## Rotación

La cuenta administrativa usa una clave distinta de las cuentas de negocios. Rotarla desde Identity
o recrear el entorno, actualizar el secreto externo y registrar fecha/responsable sin registrar el
valor. La HMAC no se rota durante el piloto porque invalidaría seguimientos existentes.
