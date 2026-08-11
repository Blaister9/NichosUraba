# 06 — Data Protection

## Por qué importa aquí más de lo habitual

El anillo de llaves de ASP.NET Data Protection no sólo firma cookies de sesión. En esta
aplicación **cifra los datos personales de los clientes**: alias, teléfono y notas de citas,
turnos y pedidos, a través de `IPersonalDataProtector`.

Consecuencias directas:

- Si se pierde el volumen `/app/keys`, esos datos quedan **ilegibles de forma permanente**. Un
  respaldo de PostgreSQL restaurado sin las llaves devuelve filas que no se pueden descifrar.
- Si alguien obtiene una copia del volumen sin cifrar, puede descifrar los datos personales de
  todos los clientes.

Por eso las llaves se respaldan aparte y, cuando es posible, se cifran en reposo con un
certificado.

## Estado actual

| Aspecto | Estado |
| --- | --- |
| Persistencia | Volumen montado en `/app/keys`, exigido por `StartupGuard` en Production |
| Aislamiento entre ambientes | `DataProtection__ApplicationName` distinto por ambiente |
| Cifrado en reposo | **Opcional y hoy sin configurar.** Sin certificado, el anillo queda persistido en claro |
| Visibilidad | La pantalla privada de salud informa la ruta y si hay certificado |

## Configurar el cifrado con certificado X.509

### 1. Generar el certificado, fuera del repositorio

```powershell
$cert = New-SelfSignedCertificate -Subject "CN=UrabaConecta Data Protection" -CertStoreLocation "Cert:\CurrentUser\My" -KeyExportPolicy Exportable -KeySpec KeyExchange -NotAfter (Get-Date).AddYears(5)
```

Exportar a un `.pfx` en una carpeta que **no** esté dentro del repositorio:

```powershell
$clave = Read-Host -AsSecureString "Contraseña del PFX"
Export-PfxCertificate -Cert $cert -FilePath "$HOME\llaves\uraba-dp.pfx" -Password $clave
```

### 2. Convertir a base64

```powershell
[Convert]::ToBase64String([IO.File]::ReadAllBytes("$HOME\llaves\uraba-dp.pfx")) | Set-Clipboard
```

### 3. Configurar en Railway

| Variable | Contenido |
| --- | --- |
| `DataProtection__ApplicationName` | `UrabaConecta.Production` |
| `DataProtection__KeysPath` | `/app/keys` |
| `DataProtection__CertificateBase64` | El base64 del paso 2 |
| `DataProtection__CertificatePassword` | La contraseña del `.pfx` |

### 4. Guardar el `.pfx` y su contraseña

En un gestor de contraseñas o una caja fuerte offline. **Sin el certificado, las llaves cifradas
con él son irrecuperables.**

## Lo que nunca va a Git

- El archivo `.pfx`
- La contraseña del `.pfx`
- El base64 del certificado
- Cualquier llave privada
- El contenido de `/app/keys`

`.gitignore` ya cubre `src/UrabaConecta.Web/UrabaConecta.Web/keys/`.

## Respaldo de las llaves

```bash
railway run --service <servicio> tar czf - /app/keys > llaves-$(date -u +%Y%m%d).tar.gz
```

Guardar cifrado y fuera de la máquina de trabajo. Repetir cada vez que el anillo rote (por
omisión, cada 90 días genera una llave nueva; las anteriores siguen haciendo falta para descifrar
lo antiguo, así que **nunca se descarta un respaldo viejo de llaves**).

## Restauración de las llaves

1. Detener el servicio.
2. Restaurar el contenido del archivo en el volumen montado en `/app/keys`.
3. Verificar que el propietario sea el usuario `app` (lo hace `docker-entrypoint.sh` al arrancar).
4. Arrancar y comprobar que un dato personal antiguo se lee correctamente.

## Verificación (obligatoria antes del go-live)

| # | Paso | Resultado esperado |
| --- | --- | --- |
| 1 | Iniciar sesión | Entra correctamente |
| 2 | Crear una cita de prueba con nombre y teléfono | Se guarda |
| 3 | Reiniciar el servicio en Railway | Vuelve a `ready` |
| 4 | Recargar sin volver a iniciar sesión | **La sesión sigue viva** |
| 5 | Abrir la cita del paso 2 | Nombre y teléfono **se leen correctamente** |
| 6 | Pantalla de salud | Informa `Persistente en /app/keys` |
| 7 | Restaurar un respaldo de llaves sobre un volumen vacío | Los datos antiguos vuelven a leerse |

Si el paso 4 falla, el volumen no está persistiendo. Si el paso 5 falla, las llaves cambiaron y
los datos anteriores ya no son legibles: **detener el go-live**.
