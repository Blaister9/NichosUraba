<#
.SYNOPSIS
    Comprobación de humo de UrabáConecta. Estrictamente de sólo lectura.

.DESCRIPTION
    Se ejecuta después de cada despliegue productivo. Sólo hace peticiones GET a rutas públicas:
    no crea citas, ni turnos, ni pedidos, ni cuentas, y no necesita credenciales. Es seguro
    lanzarlo contra Production tantas veces como haga falta.

    Comprueba además dos cosas que no se ven mirando la página: que la respuesta trae las
    cabeceras de seguridad esperadas y que no se está sirviendo un rastro de pila.

.PARAMETER BaseUrl
    Origen del despliegue, por ejemplo https://urabaconecta.up.railway.app

.EXAMPLE
    ./ops/smoke-production.ps1 -BaseUrl https://urabaconecta.up.railway.app
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$BaseUrl,
    [int]$TimeoutSeconds = 30
)

$ErrorActionPreference = 'Stop'
$BaseUrl = $BaseUrl.TrimEnd('/')
$fallos = New-Object System.Collections.Generic.List[string]
$resultados = New-Object System.Collections.Generic.List[object]

# Windows PowerShell 5.1 negocia TLS 1.0 por omisión y el despliegue sólo acepta 1.2 en adelante.
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

<#
Invocación compatible con Windows PowerShell 5.1 y con PowerShell 7. En 5.1 no existe
-SkipHttpErrorCheck y cualquier respuesta que no sea 2xx lanza excepción, así que el código y
las cabeceras se recuperan del objeto de respuesta que viaja dentro del error.
#>
function Invoke-Sonda {
    param([string]$Url)
    try {
        $r = Invoke-WebRequest -Uri $Url -Method Get -TimeoutSec $TimeoutSeconds `
                               -UseBasicParsing -MaximumRedirection 0 -ErrorAction Stop
        return [pscustomobject]@{
            Codigo = [int]$r.StatusCode; Contenido = "$($r.Content)"; Cabeceras = $r.Headers; Error = $null
        }
    }
    catch {
        $respuesta = $_.Exception.Response
        if ($respuesta -and $respuesta.StatusCode) {
            $codigo = [int]$respuesta.StatusCode
            $cabeceras = @{}
            # Una redirección es una respuesta válida para esta comprobación, no un fallo.
            foreach ($clave in $respuesta.Headers.AllKeys) { $cabeceras[$clave] = $respuesta.Headers[$clave] }
            $cuerpo = ''
            try {
                $flujo = $respuesta.GetResponseStream()
                $lector = New-Object System.IO.StreamReader($flujo)
                $cuerpo = $lector.ReadToEnd()
                $lector.Dispose()
            } catch { }
            return [pscustomobject]@{ Codigo = $codigo; Contenido = $cuerpo; Cabeceras = $cabeceras; Error = $null }
        }
        return [pscustomobject]@{ Codigo = 0; Contenido = ''; Cabeceras = @{}; Error = $_.Exception.Message }
    }
}

function Test-Ruta {
    param(
        [string]$Ruta,
        [string]$Descripcion,
        [int[]]$Esperado = @(200),
        [string]$DebeContener
    )
    $r = Invoke-Sonda -Url "$BaseUrl$Ruta"
    if ($r.Error) {
        $resultados.Add([pscustomobject]@{
            Ruta = $Ruta; Descripcion = $Descripcion; Codigo = 0; Estado = 'FALLA'; Detalle = $r.Error
        })
        $fallos.Add("$Ruta -> $($r.Error)")
        return $null
    }

    $ok = $Esperado -contains $r.Codigo
    $detalle = ''
    if (-not $ok) { $detalle = "se esperaba $($Esperado -join '/')" }
    if ($ok -and $DebeContener -and ($r.Contenido -notmatch [regex]::Escape($DebeContener))) {
        $ok = $false
        $detalle = "no contiene '$DebeContener'"
    }
    # Ningún entorno productivo debe devolver un rastro de pila en el cuerpo.
    if ($r.Contenido -match 'Microsoft\.EntityFrameworkCore|at UrabaConecta\.|Npgsql\.|StackTrace') {
        $ok = $false
        $detalle = 'la respuesta expone detalles internos'
    }
    $resultados.Add([pscustomobject]@{
        Ruta = $Ruta; Descripcion = $Descripcion; Codigo = $r.Codigo
        Estado = $(if ($ok) { 'OK' } else { 'FALLA' }); Detalle = $detalle
    })
    if (-not $ok) { $fallos.Add("$Ruta -> $($r.Codigo) $detalle") }
    return $r
}

Write-Host "Comprobación de humo contra $BaseUrl" -ForegroundColor Cyan
Write-Host ''

Test-Ruta -Ruta '/health/live'  -Descripcion 'Proceso vivo'                 | Out-Null
Test-Ruta -Ruta '/health/ready' -Descripcion 'Listo (base y migraciones)'   | Out-Null
$inicio = Test-Ruta -Ruta '/' -Descripcion 'Portada y directorio'
Test-Ruta -Ruta '/Account/Login' -Descripcion 'Página de inicio de sesión' -DebeContener 'password' | Out-Null
Test-Ruta -Ruta '/api/v1/public/businesses' -Descripcion 'API pública del directorio' | Out-Null
Test-Ruta -Ruta '/api/v1/public/legal' -Descripcion 'Datos legales publicados' | Out-Null

foreach ($doc in @('politica-de-datos', 'aviso-de-privacidad', 'terminos', 'retencion', 'reclamos')) {
    Test-Ruta -Ruta "/legal/$doc" -Descripcion "Documento legal: $doc" | Out-Null
}

# La consola administrativa no debe ser accesible sin sesión: 401 o redirección al inicio de
# sesión son correctos; un 200 con contenido sería una falla de autorización.
Test-Ruta -Ruta '/api/v1/admin/businesses' -Descripcion 'Consola privada exige sesión' -Esperado @(401, 302) | Out-Null

Write-Host ''
$resultados | Format-Table -AutoSize

# Cabeceras de seguridad sobre la portada.
if ($inicio) {
    Write-Host 'Cabeceras de seguridad en /' -ForegroundColor Cyan
    $esperadas = @{
        'Content-Security-Policy'   = $null
        'X-Content-Type-Options'    = 'nosniff'
        'X-Frame-Options'           = 'DENY'
        'Referrer-Policy'           = $null
    }
    if ($BaseUrl.StartsWith('https://')) { $esperadas['Strict-Transport-Security'] = $null }

    foreach ($nombre in @($esperadas.Keys)) {
        $valor = $inicio.Cabeceras[$nombre]
        if (-not $valor) {
            Write-Host "  FALTA  $nombre" -ForegroundColor Red
            $fallos.Add("Falta la cabecera $nombre")
        }
        elseif ($esperadas[$nombre] -and "$valor" -ne $esperadas[$nombre]) {
            Write-Host "  DIFIERE $nombre = $valor" -ForegroundColor Red
            $fallos.Add("$nombre = $valor")
        }
        else {
            Write-Host "  OK      $nombre" -ForegroundColor Green
        }
    }
}

Write-Host ''
if ($fallos.Count -eq 0) {
    Write-Host 'Comprobación de humo correcta. No se creó ningún dato.' -ForegroundColor Green
    exit 0
}
Write-Host "Comprobación de humo con $($fallos.Count) falla(s):" -ForegroundColor Red
$fallos | ForEach-Object { Write-Host "  - $_" -ForegroundColor Red }
exit 1
