<#
.SYNOPSIS
    Respaldo de la base PostgreSQL productiva de UrabáConecta.

.DESCRIPTION
    Railway Hobby no ofrece respaldos administrados con retención ni restauración puntual, así
    que el respaldo es responsabilidad de la operación. Este script produce un volcado en formato
    custom (comprimido y restaurable por partes), lo verifica leyendo su índice y aplica la
    retención acordada.

    El script NO contiene credenciales. La cadena de conexión se entrega por variable de entorno:

        $env:URABACONECTA_BACKUP_CONNECTION = "postgresql://usuario:clave@host:puerto/base"

    Esa variable se copia del panel de Railway en el momento de ejecutar y se descarta al cerrar
    la consola. No la escriba en un archivo del repositorio.

.PARAMETER Destination
    Carpeta donde se deja el archivo. Debe ser privada y estar fuera del repositorio.
    Por omisión, la carpeta 'backups' del perfil del usuario.

.PARAMETER RetentionDays
    Días de retención. Los volcados más antiguos se eliminan al final de una ejecución correcta.

.EXAMPLE
    $env:URABACONECTA_BACKUP_CONNECTION = "postgresql://..."
    ./ops/backup-postgres.ps1 -Destination "D:\respaldos\urabaconecta"
#>
[CmdletBinding()]
param(
    [string]$Destination = (Join-Path $HOME 'backups\urabaconecta'),
    [int]$RetentionDays = 30,
    [string]$Label = 'production'
)

$ErrorActionPreference = 'Stop'

$connection = $env:URABACONECTA_BACKUP_CONNECTION
if ([string]::IsNullOrWhiteSpace($connection)) {
    throw "Falta la variable de entorno URABACONECTA_BACKUP_CONNECTION. No se codifican credenciales en el script."
}

foreach ($tool in @('pg_dump', 'pg_restore')) {
    if (-not (Get-Command $tool -ErrorAction SilentlyContinue)) {
        throw "No se encontró '$tool' en el PATH. Instale las herramientas cliente de PostgreSQL 17."
    }
}

if (-not (Test-Path $Destination)) {
    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
}

$timestamp = (Get-Date).ToUniversalTime().ToString('yyyyMMdd-HHmmss')
$file = Join-Path $Destination "urabaconecta-$Label-$timestamp.dump"

Write-Host "Respaldando en $file"

# --format=custom permite restaurar tablas sueltas y ya viene comprimido; --compress=9 lo lleva
# al máximo porque el cuello de botella es la red, no la CPU de la máquina que respalda.
# No se imprime la cadena de conexión: contiene la contraseña.
& pg_dump --dbname=$connection --format=custom --compress=9 --no-owner --no-privileges --file=$file
if ($LASTEXITCODE -ne 0) {
    if (Test-Path $file) { Remove-Item $file -Force }
    throw "pg_dump terminó con código $LASTEXITCODE. No se conservó un archivo a medias."
}

if (-not (Test-Path $file)) { throw "pg_dump no produjo el archivo esperado." }

$size = (Get-Item $file).Length
if ($size -lt 1024) {
    Remove-Item $file -Force
    throw "El volcado pesa $size bytes: es demasiado pequeño para ser válido. Se descartó."
}

# Verificación real del archivo, no sólo de su tamaño: pg_restore --list falla si el volcado
# quedó truncado o corrupto. Un respaldo que no se verifica no es un respaldo.
$tabla = & pg_restore --list $file 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "El volcado no pasó la verificación de pg_restore --list. Revise $file antes de confiar en él."
}
$objetos = ($tabla | Where-Object { $_ -notmatch '^;' -and $_.Trim() -ne '' }).Count
if ($objetos -lt 1) {
    throw "El volcado no declara objetos restaurables. Revise $file."
}

Write-Host ("Respaldo correcto: {0:N1} MiB, {1} objetos restaurables." -f ($size / 1MB), $objetos)

# Retención. Se aplica sólo tras un respaldo verificado, para no quedarse sin copias por haber
# borrado las viejas en una ejecución que después falló.
$limite = (Get-Date).AddDays(-$RetentionDays)
$viejos = Get-ChildItem -Path $Destination -Filter "urabaconecta-$Label-*.dump" |
    Where-Object { $_.LastWriteTime -lt $limite }
foreach ($v in $viejos) {
    Write-Host "Retención: se elimina $($v.Name)"
    Remove-Item $v.FullName -Force
}

Write-Host "Listo. Copie este archivo a un destino externo (no deje la única copia en esta máquina)."
Write-Output $file
