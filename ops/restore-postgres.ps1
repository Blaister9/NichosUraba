<#
.SYNOPSIS
    Restauración de un volcado de UrabáConecta en una base PostgreSQL.

.DESCRIPTION
    Pensado para dos usos: el ensayo periódico de restauración (obligatorio: un respaldo que
    nunca se restauró no está probado) y la recuperación real ante un incidente.

    Por omisión restaura en una base NUEVA y se niega a escribir sobre una base existente. Para
    sobrescribir hay que pedirlo explícitamente con -Overwrite, que es una operación destructiva.

    Las credenciales llegan por variable de entorno, nunca por parámetro ni por archivo:

        $env:URABACONECTA_RESTORE_CONNECTION = "postgresql://usuario:clave@host:puerto/postgres"

    Apunte esa cadena a la base de mantenimiento ('postgres'), no a la base destino: el script
    crea la base destino a partir de ella.

.PARAMETER DumpFile
    Archivo .dump producido por ops/backup-postgres.ps1.

.PARAMETER TargetDatabase
    Nombre de la base donde restaurar. Para un ensayo, use un nombre temporal.

.PARAMETER Overwrite
    Permite restaurar sobre una base que ya existe, eliminándola primero. Destructivo.

.EXAMPLE
    # Ensayo de restauración en una base temporal
    $env:URABACONECTA_RESTORE_CONNECTION = "postgresql://...@host:5432/postgres"
    ./ops/restore-postgres.ps1 -DumpFile D:\respaldos\urabaconecta-production-20260810-0300.dump `
                               -TargetDatabase urabaconecta_ensayo
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string]$DumpFile,
    [Parameter(Mandatory = $true)][string]$TargetDatabase,
    [switch]$Overwrite,
    [int]$Jobs = 4
)

$ErrorActionPreference = 'Stop'

$admin = $env:URABACONECTA_RESTORE_CONNECTION
if ([string]::IsNullOrWhiteSpace($admin)) {
    throw "Falta la variable de entorno URABACONECTA_RESTORE_CONNECTION. No se codifican credenciales en el script."
}
if (-not (Test-Path $DumpFile)) { throw "No existe el archivo $DumpFile." }
if ($TargetDatabase -notmatch '^[a-zA-Z_][a-zA-Z0-9_]*$') {
    throw "El nombre de base '$TargetDatabase' no es un identificador simple válido."
}

foreach ($tool in @('psql', 'pg_restore')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$tool' en el PATH. Instale las herramientas cliente de PostgreSQL 17."
    }
}

# Se verifica el volcado ANTES de tocar la base destino. Descubrir que el archivo estaba corrupto
# después de haber borrado la base es exactamente el incidente que esto evita.
& pg_restore --list $DumpFile > $null 2>&1
if ($LASTEXITCODE -ne 0) { throw "El archivo $DumpFile no es un volcado custom válido." }

# psql no devuelve ninguna línea cuando la consulta no tiene filas, así que el resultado puede
# ser $null: se normaliza antes de compararlo.
$existe = (& psql --dbname=$admin --tuples-only --no-align `
    --command "SELECT 1 FROM pg_database WHERE datname = '$TargetDatabase'" | Out-String).Trim()
if ($LASTEXITCODE -ne 0) { throw "No se pudo consultar el catálogo de bases." }

if ($existe -eq '1') {
    if (-not $Overwrite) {
        throw "La base '$TargetDatabase' ya existe. Use -Overwrite sólo si pretende destruirla."
    }
    Write-Warning "Se eliminará la base existente '$TargetDatabase'."
    $confirmacion = Read-Host "Escriba el nombre de la base para confirmar"
    if ($confirmacion -ne $TargetDatabase) { throw "Confirmación no coincide. No se hizo nada." }
    & psql --dbname=$admin --command "DROP DATABASE ""$TargetDatabase"" WITH (FORCE)"
    if ($LASTEXITCODE -ne 0) { throw "No se pudo eliminar la base '$TargetDatabase'." }
}

& psql --dbname=$admin --command "CREATE DATABASE ""$TargetDatabase"""
if ($LASTEXITCODE -ne 0) { throw "No se pudo crear la base '$TargetDatabase'." }

# La cadena destino se arma reemplazando el último segmento de ruta de la de mantenimiento.
$uri = [System.Uri]$admin
$destino = "$($uri.Scheme)://$($uri.UserInfo)@$($uri.Host):$($uri.Port)/$TargetDatabase"

Write-Host "Restaurando $DumpFile en '$TargetDatabase'..."
& pg_restore --dbname=$destino --no-owner --no-privileges --jobs=$Jobs --exit-on-error $DumpFile
if ($LASTEXITCODE -ne 0) { throw "pg_restore terminó con código $LASTEXITCODE." }

# Comprobación de que la restauración dejó algo utilizable, no una base vacía con éxito aparente.
# La verificación viaja en un archivo .sql, no en --command. PowerShell se come las comillas
# dobles al invocar ejecutables nativos, y "__EFMigrationsHistory" las necesita: sin ellas
# PostgreSQL busca __efmigrationshistory en minúsculas y no la encuentra.
$sqlTemporal = Join-Path ([System.IO.Path]::GetTempPath()) "uraba-verificacion-$PID.sql"
@'
SELECT count(*) FROM information_schema.tables WHERE table_schema = 'public';
SELECT count(*) FROM "__EFMigrationsHistory";
'@ | Set-Content -Path $sqlTemporal -Encoding UTF8

try {
    $salida = & psql --dbname=$destino --tuples-only --no-align --file $sqlTemporal
    if ($LASTEXITCODE -ne 0) { throw "No se pudo verificar la base restaurada." }
}
finally {
    Remove-Item $sqlTemporal -Force -ErrorAction SilentlyContinue
}

$numeros = @($salida | ForEach-Object { "$_".Trim() } | Where-Object { $_ -match '^\d+$' })
if ($numeros.Count -lt 2) { throw "La verificación no devolvió los dos recuentos esperados." }
$tablas = $numeros[0]
$migraciones = $numeros[1]

Write-Host "Restauración terminada: $tablas tablas, $migraciones migraciones registradas."
if ([string]::IsNullOrWhiteSpace($tablas) -or [int]$tablas -lt 1) {
    throw "La base restaurada no tiene tablas."
}
if ([string]::IsNullOrWhiteSpace($migraciones) -or [int]$migraciones -lt 1) {
    throw "La base restaurada no conserva el historial de migraciones de Entity Framework."
}

Write-Host "Verifique además que la aplicación arranca contra esta base antes de darla por buena."
