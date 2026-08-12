# 04 — Secretos: auditoría y manejo

El inventario de nombres y propósitos está en `SECRETS_INVENTORY.md`. Este documento explica qué
se auditó, qué hay que rotar y cómo se generan los valores nuevos.

## Auditoría del repositorio

Barrido sobre **todo el historial de Git** (`git rev-list --all`), no sólo sobre la copia de
trabajo, buscando:

| Patrón buscado | Resultado |
| --- | --- |
| `Password=` en cadenas de conexión | Sin coincidencias reales |
| Claves de acceso y secretos de R2 (`AccessKey`, `SecretKey`, `r2.cloudflarestorage`) | Sin coincidencias |
| Claves HMAC con valor sustantivo | Sin coincidencias |
| `CertificateBase64` con material, bloques `BEGIN PRIVATE KEY` | Sin coincidencias |
| `.env` confirmado alguna vez | Nunca estuvo bajo control de versiones |
| Carpeta `private/` confirmada | Nunca estuvo bajo control de versiones |

`.gitignore` cubre `.env`, `private/`, `**/bin/`, `**/obj/` y
`src/UrabaConecta.Web/UrabaConecta.Web/keys/`.

**Única credencial literal en el repositorio:** `DevelopmentSeeder.DemoPassword`. Es una
contraseña de Development, deliberadamente pública, y figura en la lista negra de
`StartupGuard.ForbiddenProductionPasswords`: si aparece en cualquier variable de Production, la
aplicación no arranca.

Conclusión: **no hay secretos productivos filtrados en Git.** No hace falta reescribir historial.

## Credenciales de las cuentas Demo

Regla, porque la confusión ya costó un incidente de acceso:

| Variable | Para qué sirve | Qué NO hace |
| --- | --- | --- |
| `DemoSeed__AdminPassword`, `DemoSeed__BusinessPassword` | Fijan la contraseña **en el momento de crear** cada cuenta sembrada | No cambian la contraseña de una cuenta que ya existe |
| `DemoAccess__SharedPassword` | Reconcilia de forma deliberada las cinco cuentas comerciales de Demo | No debe quedarse puesta |

`DevelopmentSeeder.EnsureUser` sólo asigna contraseña al **crear** el usuario: si ya existe,
actualiza el nombre visible y se va. Rotar `DemoSeed__*Password` después del primer sembrado no rota
nada, y deja las variables diciendo una cosa mientras la base dice otra. Eso fue exactamente lo que
pasó: en agosto de 2026 ninguna de las dos variables abría ya su cuenta.

**No conviertas el sembrado en un sincronizador de contraseñas.** Si `EnsureUser` reescribiera la
contraseña en cada arranque, una variable mal puesta restablecería en silencio todas las cuentas
Demo cada vez que el contenedor se reinicia, y nadie se enteraría hasta la siguiente capacitación.

### Reconciliación deliberada

Para volver a dejar las cuentas con una credencial conocida existe `DemoAccessNormalizer`, que ya
cubre las cinco cuentas que se usan para capacitar: `admin@urabaconecta.demo`,
`socia@urabaconecta.demo`, `propietaria@bella.demo`, `propietario@corte.demo` y
`propietario@sazon.demo`. Usa el flujo soportado de Identity —token de restablecimiento y
`ResetPasswordAsync`—, limpia bloqueos e intentos fallidos, corrige rol y membresía, y **verifica
las cinco cuentas antes de dar el trabajo por bueno**. Fuera de `Demo` lanza excepción.

Se activa por la **presencia** del secreto, no por un interruptor permanente:

1. Poner `DemoAccess__SharedPassword` en el servicio Demo.
2. Reiniciar el servicio.
3. Comprobar en el registro: «Se normalizaron y verificaron cinco accesos comerciales».
4. Comprobar el inicio de sesión de un rol.
5. **Borrar la variable.** Mientras siga puesta, cada reinicio vuelve a restablecer esas cinco
   cuentas y desactiva cualquier otra membresía suya. Es un bisturí, no una política.

La contraseña resultante se guarda donde se guardan las credenciales operativas, no en el repositorio
ni en el registro.

## Qué rotar antes de Production

Production estrena todo. No se hereda ningún valor de Demo:

- Cadena de conexión: la genera Railway al crear la instancia nueva.
- `URABACONECTA_TRACKING_HMAC_KEY`: nueva. Compartirla con Demo permitiría resolver códigos de
  seguimiento de un ambiente en el otro.
- `URABACONECTA_INVITATION_HMAC_KEY`: nueva.
- Credenciales de R2: par nuevo, con permiso únicamente sobre el bucket de Production.
- Certificado de Data Protection: nuevo, generado fuera del repositorio.

Demo **no se rota** mientras siga en uso, salvo que uno de sus secretos se haya expuesto de forma
que también afecte a otro sistema.

## Generación de claves

Clave aleatoria de 32 bytes en base64:

```bash
openssl rand -base64 32
```

O sin OpenSSL, con PowerShell:

```powershell
[Convert]::ToBase64String((1..32 | ForEach-Object { Get-Random -Maximum 256 } | ForEach-Object { [byte]$_ }))
```

El certificado X.509 para Data Protection se genera aparte: ver `06_DATA_PROTECTION.md`.

## Manejo

- Los valores se escriben **directamente en el panel de variables de Railway**, una sola vez.
- No se envían por WhatsApp, ni por correo, ni se pegan en un chat.
- No se proyectan en pantalla durante demostraciones.
- Si un secreto aparece en una captura, un video o un registro: se rota.

## Registro de rotaciones

Se lleva a mano. Sólo fechas y motivo, nunca valores.

| Fecha | Secreto | Motivo | Responsable |
| --- | --- | --- | --- |
| _(pendiente)_ | | | |
