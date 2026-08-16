<#
.SYNOPSIS
    Carga sostenida contra las pantallas públicas, por escalones de usuarios concurrentes.

.DESCRIPTION
    Las pruebas anteriores lanzaban una ráfaga y medían lo que tardaba en pasar. Una ráfaga no
    distingue "el servidor es lento" de "el servidor encoló": con veinte peticiones a la vez y una
    puerta que las serializa, la última espera a las diecinueve anteriores y el promedio lo esconde.
    Aquí cada usuario virtual repite su petición durante toda la ventana, así que lo que se observa
    es el estado estacionario: cuántas peticiones por segundo salen y cómo se estira la cola.

    No es una prueba de ruptura. Los escalones llegan hasta veinte usuarios, que es el objetivo del
    presupuesto, y cada petición espera a la anterior del mismo usuario: no se inunda el servicio.
#>
param(
    [string]$BaseUrl = "https://nichosuraba-production.up.railway.app",
    [string]$Path = "/",
    [int[]]$Steps = @(5, 10, 20),
    [int]$SecondsPerStep = 45,
    [string]$Label = "sostenida",
    [string]$OutputDirectory = "artifacts/performance"
)
$ErrorActionPreference = "Stop"
$url = "$BaseUrl$Path"

function Get-Percentil {
    param([double[]]$Ordenado, [double]$Fraccion)
    if ($Ordenado.Count -eq 0) { return 0 }
    $indice = [math]::Ceiling($Ordenado.Count * $Fraccion) - 1
    return $Ordenado[[math]::Max(0, [math]::Min($Ordenado.Count - 1, $indice))]
}

$resultados = foreach ($usuarios in $Steps) {
    Write-Host "escalón de $usuarios usuarios durante $SecondsPerStep s -> $url"
    $trabajos = 1..$usuarios | ForEach-Object {
        Start-Job -ArgumentList $url, $SecondsPerStep -ScriptBlock {
            param($url, $segundos)
            $muestras = [Collections.Generic.List[psobject]]::new()
            $hasta = [DateTimeOffset]::UtcNow.AddSeconds($segundos)
            while ([DateTimeOffset]::UtcNow -lt $hasta) {
                $salida = & curl.exe -s -o NUL --max-time 60 `
                    -w "%{http_code} %{time_starttransfer} %{time_total}" $url
                $p = $salida -split '\s+'
                $muestras.Add([pscustomobject]@{
                    Status = [int]$p[0]
                    TtfbMs = [double]$p[1] * 1000
                    TotalMs = [double]$p[2] * 1000
                })
            }
            return $muestras.ToArray()
        }
    }
    $muestras = @($trabajos | Receive-Job -Wait)
    $trabajos | Remove-Job

    $ttfb = @($muestras | ForEach-Object TtfbMs | Sort-Object)
    $errores = @($muestras | Where-Object { $_.Status -ge 400 -or $_.Status -eq 0 })
    $servidor = @($muestras | Where-Object { $_.Status -ge 500 })
    [pscustomobject]@{
        usuarios        = $usuarios
        segundos        = $SecondsPerStep
        peticiones      = $muestras.Count
        peticiones_s    = [math]::Round($muestras.Count / $SecondsPerStep, 2)
        codigos         = ($muestras | Group-Object Status | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ","
        errores         = $errores.Count
        errores_5xx     = $servidor.Count
        tasa_error      = [math]::Round($errores.Count / [math]::Max(1, $muestras.Count), 4)
        ttfb_p50_ms     = [math]::Round((Get-Percentil $ttfb 0.50), 1)
        ttfb_p90_ms     = [math]::Round((Get-Percentil $ttfb 0.90), 1)
        ttfb_p95_ms     = [math]::Round((Get-Percentil $ttfb 0.95), 1)
        ttfb_p99_ms     = [math]::Round((Get-Percentil $ttfb 0.99), 1)
        ttfb_max_ms     = [math]::Round($ttfb[-1], 1)
    }
}

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$destino = Join-Path $OutputDirectory "$Label.json"
[pscustomobject]@{
    etiqueta = $Label; url = $url
    medido_utc = (Get-Date).ToUniversalTime().ToString("o")
    commit = (& git rev-parse --short HEAD)
    resultados = $resultados
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding utf8 $destino

$resultados | Format-Table
Write-Host "guardado en $destino"
