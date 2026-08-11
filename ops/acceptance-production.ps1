<#
.SYNOPSIS
    Aceptación autenticada de UrabáConecta. Manual y de sólo lectura.

.DESCRIPTION
    Complemento opcional de ops/smoke-production.ps1. A diferencia de aquél, éste inicia sesión,
    de modo que comprueba lo que no se ve desde fuera: que el inicio de sesión funciona, que la
    pantalla privada de salud responde y que informa el ambiente, la versión y el commit correctos.

    Sigue siendo de sólo lectura: no crea negocios, ni citas, ni invitaciones, ni cuentas.

    NO se ejecuta automáticamente en un despliegue. Lo lanza una persona, a mano, cuando quiere
    confirmar un go-live o diagnosticar un incidente.

    La contraseña no se pasa por parámetro (quedaría en el historial de la consola). El script la
    pide de forma interactiva, o la toma de:

        $env:URABACONECTA_ACCEPTANCE_PASSWORD

.EXAMPLE
    ./ops/acceptance-production.ps1 -BaseUrl https://urabaconecta.up.railway.app `
                                    -Email admin@sudominio.co
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [Parameter(Mandatory = $true)][string]$Email,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')

# Windows PowerShell 5.1 negocia TLS 1.0 por omisión y el despliegue sólo acepta 1.2 en adelante.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$plano = $env:URABACONECTA_ACCEPTANCE_PASSWORD
if ([string]::IsNullOrWhiteSpace($plano)) {
    $segura = Read-Host -AsSecureString "Contraseña de $Email"
    $plano = [System.Net.NetworkCredential]::new('', $segura).Password
}
if ([string]::IsNullOrWhiteSpace($plano)) { throw 'No se recibió contraseña.' }

$sesion = New-Object Microsoft.PowerShell.Commands.WebRequestSession

Write-Host 'Solicitando la página de inicio de sesión...'
$login = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -WebSession $sesion -TimeoutSec $TimeoutSeconds

# El formulario está protegido por antiforgery: hay que reenviar el token que vino en la página.
$token = [regex]::Match($login.Content,
    'name="__RequestVerificationToken"[^>]*value="([^"]+)"').Groups[1].Value
if (-not $token) {
    $token = [regex]::Match($login.Content,
        'value="([^"]+)"[^>]*name="__RequestVerificationToken"').Groups[1].Value
}
if (-not $token) { throw 'No se encontró el token antiforgery en la página de inicio de sesión.' }

$cuerpo = @{
    '__RequestVerificationToken' = $token
    '_handler'                   = 'login'
    'Input.Email'                = $Email
    'Input.Password'             = $plano
    'Input.RememberMe'           = 'false'
}

Write-Host 'Iniciando sesión...'
$respuesta = Invoke-WebRequest -Uri "$BaseUrl/Account/Login" -Method Post -Body $cuerpo `
    -WebSession $sesion -TimeoutSec $TimeoutSeconds -SkipHttpErrorCheck -MaximumRedirection 5

# La contraseña deja de existir en memoria en cuanto se usó.
$plano = $null
$cuerpo['Input.Password'] = $null
[System.GC]::Collect()

$autenticado = $sesion.Cookies.GetCookies($BaseUrl) | Where-Object { $_.Name -like '*Identity.Application*' }
if (-not $autenticado) {
    if ($respuesta.Content -match 'Cuenta bloqueada|Lockout') {
        throw 'La cuenta está bloqueada por intentos fallidos. Espere el periodo de bloqueo.'
    }
    throw 'No se obtuvo cookie de sesión: el inicio de sesión falló.'
}
Write-Host 'Sesión iniciada.' -ForegroundColor Green

Write-Host 'Consultando la pantalla privada de salud...'
$salud = Invoke-RestMethod -Uri "$BaseUrl/api/v1/admin/health" -WebSession $sesion -TimeoutSec $TimeoutSeconds

[pscustomobject]@{
    Ambiente          = $salud.environment
    Version           = $salud.version
    Commit            = $salud.commit
    Uptime            = $salud.uptime
    BaseDeDatos       = $salud.databaseStatus
    Migraciones       = $salud.migrationStatus
    Almacenamiento    = $salud.objectStorageStatus
    Proveedor         = $salud.objectStorageProvider
    DataProtection    = $salud.dataProtectionStatus
    SembradoDemo      = $salud.demoSeedEnabled
} | Format-List

$problemas = New-Object System.Collections.Generic.List[string]
if ($salud.demoSeedEnabled) { $problemas.Add('El sembrado Demo aparece habilitado.') }
if ($salud.environment -ne 'Production') { $problemas.Add("El ambiente informado es '$($salud.environment)'.") }
if ($salud.objectStorageProvider -ne 'S3') { $problemas.Add("El proveedor de almacenamiento es '$($salud.objectStorageProvider)'.") }
if ($salud.databaseStatus -notmatch 'sin migraciones pendientes') { $problemas.Add("Base de datos: $($salud.databaseStatus)") }
if ($salud.commit -eq 'desconocido') { $problemas.Add('No se registró el commit desplegado.') }
if ($salud.dataProtectionStatus -notmatch 'Persistente') { $problemas.Add("Data Protection: $($salud.dataProtectionStatus)") }

Write-Host ''
if ($problemas.Count -eq 0) {
    Write-Host 'Aceptación correcta. No se creó ningún dato.' -ForegroundColor Green
    exit 0
}
Write-Host "Aceptación con $($problemas.Count) observación(es):" -ForegroundColor Red
$problemas | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
