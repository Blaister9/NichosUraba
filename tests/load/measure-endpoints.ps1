<#
.SYNOPSIS
    Arnés de medición HTTP reproducible para las pantallas públicas de UrabáConecta.

.DESCRIPTION
    Mide TTFB y tiempo total por separado. La distinción importa: el TTFB es lo que tarda el
    servidor en componer la pantalla —que es donde vive el coste de las consultas— y el total
    añade la descarga del HTML, que depende del tamaño de la respuesta y no del número de idas
    y vueltas a la base.

    La primera petición de cada ruta se mide y se reporta aparte como "cold-ish": la caché
    pública vive entre 30 y 120 segundos, así que una muestra recién llegada no es comparable
    con las que la encuentran caliente. Las demás se resumen como warm.

    No hay Start-Sleep entre peticiones porque la caché expira sola y espaciarlas sólo alargaría
    la medición sin cambiar lo que mide.
#>
param(
    [string]$BaseUrl = "https://nichosuraba-production.up.railway.app",
    [int]$Samples = 40,
    [string]$Label = "baseline",
    [string]$OutputDirectory = "artifacts/performance",
    [string[]]$Only = @()
)
$ErrorActionPreference = "Stop"

$rutas = @(
    @{ Name = "home";            Path = "/" }
    @{ Name = "turnos-brio";     Path = "/negocios/brio-nativo-barberia-demo/turnos" }
    @{ Name = "citas-laura";     Path = "/negocios/laura-usuga/citas" }
    @{ Name = "pedidos-lumina";  Path = "/negocios/lumina-coral-beauty-demo/pedidos" }
    @{ Name = "explorar";        Path = "/explorar" }
    @{ Name = "ficha-brio";      Path = "/negocios/brio-nativo-barberia-demo" }
    @{ Name = "api-directorio";  Path = "/api/v1/public/businesses" }
)
if ($Only.Count -gt 0) { $rutas = $rutas | Where-Object { $Only -contains $_.Name } }

function Get-Percentil {
    param([double[]]$Ordenado, [double]$Fraccion)
    if ($Ordenado.Count -eq 0) { return 0 }
    $indice = [math]::Ceiling($Ordenado.Count * $Fraccion) - 1
    return $Ordenado[[math]::Max(0, [math]::Min($Ordenado.Count - 1, $indice))]
}

function Measure-Ruta {
    param([string]$Url, [int]$Cuantas)
    $muestras = [Collections.Generic.List[psobject]]::new()
    for ($i = 0; $i -lt $Cuantas; $i++) {
        # curl reporta el TTFB nativamente; medirlo desde PowerShell obligaría a leer el cuerpo
        # en streaming y el propio intérprete añadiría ruido del orden de la magnitud medida.
        $salida = & curl.exe -s -o NUL --max-time 60 `
            -w "%{http_code} %{time_starttransfer} %{time_total} %{size_download}" $Url
        $partes = $salida -split '\s+'
        $muestras.Add([pscustomobject]@{
            Status = [int]$partes[0]
            TtfbMs = [math]::Round([double]$partes[1] * 1000, 1)
            TotalMs = [math]::Round([double]$partes[2] * 1000, 1)
            Bytes = [int]$partes[3]
        })
    }
    return $muestras
}

function Resumir {
    param([psobject[]]$Muestras, [string]$Nombre, [string]$Url, [psobject]$Cold)
    $ttfb = @($Muestras | ForEach-Object TtfbMs | Sort-Object)
    $total = @($Muestras | ForEach-Object TotalMs | Sort-Object)
    $errores = @($Muestras | Where-Object { $_.Status -ge 400 -or $_.Status -eq 0 })
    [pscustomobject]@{
        escenario      = $Nombre
        url            = $Url
        muestras       = $Muestras.Count
        codigos        = ($Muestras | Group-Object Status | ForEach-Object { "$($_.Name)x$($_.Count)" }) -join ","
        errores        = $errores.Count
        tasa_error     = [math]::Round($errores.Count / [math]::Max(1, $Muestras.Count), 4)
        bytes_mediana  = (@($Muestras | ForEach-Object Bytes | Sort-Object))[[int]($Muestras.Count / 2)]
        cold_ttfb_ms   = $Cold.TtfbMs
        cold_total_ms  = $Cold.TotalMs
        ttfb_min_ms    = $ttfb[0]
        ttfb_p50_ms    = Get-Percentil $ttfb 0.50
        ttfb_p90_ms    = Get-Percentil $ttfb 0.90
        ttfb_p95_ms    = Get-Percentil $ttfb 0.95
        ttfb_p99_ms    = Get-Percentil $ttfb 0.99
        ttfb_max_ms    = $ttfb[-1]
        total_p50_ms   = Get-Percentil $total 0.50
        total_p90_ms   = Get-Percentil $total 0.90
        total_p95_ms   = Get-Percentil $total 0.95
        total_max_ms   = $total[-1]
    }
}

$resultados = foreach ($ruta in $rutas) {
    $url = "$BaseUrl$($ruta.Path)"
    Write-Host "midiendo $($ruta.Name) -> $url"
    $frio = (Measure-Ruta -Url $url -Cuantas 1)[0]
    $calientes = Measure-Ruta -Url $url -Cuantas $Samples
    Resumir -Muestras $calientes -Nombre $ruta.Name -Url $url -Cold $frio
}

New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$destino = Join-Path $OutputDirectory "$Label.json"
[pscustomobject]@{
    etiqueta = $Label
    base_url = $BaseUrl
    medido_utc = (Get-Date).ToUniversalTime().ToString("o")
    commit = (& git rev-parse --short HEAD)
    muestras_por_ruta = $Samples
    resultados = $resultados
} | ConvertTo-Json -Depth 6 | Set-Content -Encoding utf8 $destino

$resultados | Format-Table escenario, muestras, codigos, cold_ttfb_ms, ttfb_min_ms, ttfb_p50_ms, ttfb_p90_ms, ttfb_p95_ms, ttfb_max_ms, total_p50_ms, total_p95_ms, bytes_mediana
Write-Host "guardado en $destino"
