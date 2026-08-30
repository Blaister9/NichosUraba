#!/bin/sh
# Copia de media entre buckets R2, pensada para correr DENTRO del contenedor de
# prod-real, que no tiene Python: sólo curl y openssl.
#
#   ORIGEN  (piloto)     MigrationSourceR2__{Endpoint,Bucket,AccessKey,SecretKey}
#   DESTINO (Production) ObjectStorage__{ServiceUrl,Bucket,AccessKey,SecretKey,Region}
#
# Las credenciales del destino son variables selladas del servicio: nunca salen del
# contenedor. Las del origen son un token temporal de sólo lectura. Ninguna de las dos
# se imprime.
#
# Modos:
#   --check     comprueba que ambos buckets responden. No escribe.
#   --dry-run   recorre el manifest y dice qué copiaría. No escribe.
#   --confirm   copia de verdad.
#
# Uso: sh copy_media.sh --dry-run [/tmp/media_manifest.csv]
set -eu

MODE="${1:---check}"
MANIFEST="${2:-/tmp/media_manifest.csv}"
EMPTY_SHA='e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
WORK="${TMPDIR:-/tmp}/uc_media_$$"
mkdir -p "$WORK"
trap 'rm -rf "$WORK"' EXIT INT TERM

SRC_ENDPOINT="${MigrationSourceR2__Endpoint:-}"
SRC_BUCKET="${MigrationSourceR2__Bucket:-}"
SRC_ACCESS="${MigrationSourceR2__AccessKey:-}"
SRC_SECRET="${MigrationSourceR2__SecretKey:-}"
SRC_REGION="${MigrationSourceR2__Region:-auto}"

DST_ENDPOINT="${ObjectStorage__ServiceUrl:-}"
DST_BUCKET="${ObjectStorage__Bucket:-}"
DST_ACCESS="${ObjectStorage__AccessKey:-}"
DST_SECRET="${ObjectStorage__SecretKey:-}"
DST_REGION="${ObjectStorage__Region:-auto}"

hex() { awk '{print $NF}'; }
sha256_file() { sha256sum "$1" | awk '{print $1}'; }
sha256_str() { printf '%s' "$1" | openssl dgst -sha256 | hex; }

# Firma SigV4. Emite el fichero de cabeceras en $WORK/hdrs para pasarlo a curl con -H @.
sign() {
    _method=$1; _endpoint=$2; _bucket=$3; _key=$4; _query=$5
    _access=$6; _secret=$7; _region=$8; _payload_sha=$9; _ctype=${10:-}

    _host=$(printf '%s' "$_endpoint" | sed -e 's|^https\{0,1\}://||' -e 's|/.*$||')
    _amzdate=$(date -u +%Y%m%dT%H%M%SZ)
    _datestamp=$(date -u +%Y%m%d)
    if [ -n "$_key" ]; then _uri="/$_bucket/$_key"; else _uri="/$_bucket"; fi

    if [ -n "$_ctype" ]; then
        _canon_headers="content-type:$_ctype
host:$_host
x-amz-content-sha256:$_payload_sha
x-amz-date:$_amzdate
"
        _signed='content-type;host;x-amz-content-sha256;x-amz-date'
    else
        _canon_headers="host:$_host
x-amz-content-sha256:$_payload_sha
x-amz-date:$_amzdate
"
        _signed='host;x-amz-content-sha256;x-amz-date'
    fi

    _canon="$_method
$_uri
$_query
$_canon_headers
$_signed
$_payload_sha"

    _scope="$_datestamp/$_region/s3/aws4_request"
    _sts="AWS4-HMAC-SHA256
$_amzdate
$_scope
$(sha256_str "$_canon")"

    _k=$(printf '%s' "$_datestamp"    | openssl dgst -sha256 -hmac "AWS4$_secret" | hex)
    _k=$(printf '%s' "$_region"       | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$_k" | hex)
    _k=$(printf '%s' 's3'             | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$_k" | hex)
    _k=$(printf '%s' 'aws4_request'   | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$_k" | hex)
    _sig=$(printf '%s' "$_sts"        | openssl dgst -sha256 -mac HMAC -macopt "hexkey:$_k" | hex)

    : > "$WORK/hdrs"
    printf 'x-amz-content-sha256: %s\n' "$_payload_sha" >> "$WORK/hdrs"
    printf 'x-amz-date: %s\n' "$_amzdate" >> "$WORK/hdrs"
    [ -n "$_ctype" ] && printf 'content-type: %s\n' "$_ctype" >> "$WORK/hdrs"
    printf 'Authorization: AWS4-HMAC-SHA256 Credential=%s/%s, SignedHeaders=%s, Signature=%s\n' \
        "$_access" "$_scope" "$_signed" "$_sig" >> "$WORK/hdrs"

    if [ -n "$_query" ]; then URL="$_endpoint$_uri?$_query"; else URL="$_endpoint$_uri"; fi
}

# Lista un bucket. Deja las claves en $WORK/keys.txt y publica $LIST_CODE / $LIST_COUNT.
# No usa tuberia para no perder las variables en una subshell.
LIST_CODE=''
LIST_COUNT=0
list_bucket() {
    _ep=$1; _bk=$2; _ac=$3; _se=$4; _rg=$5
    sign GET "$_ep" "$_bk" "" "list-type=2&max-keys=1000" "$_ac" "$_se" "$_rg" "$EMPTY_SHA"
    LIST_CODE=$(curl -s -o "$WORK/list.xml" -w '%{http_code}' -H @"$WORK/hdrs" "$URL")
    if [ "$LIST_CODE" = "200" ]; then
        tr '<' '\n' < "$WORK/list.xml" | sed -n 's|^Key>||p' > "$WORK/keys.txt"
    else
        : > "$WORK/keys.txt"
    fi
    LIST_COUNT=$(wc -l < "$WORK/keys.txt" | tr -d ' ')
}

head_object() {
    _ep=$1; _bk=$2; _key=$3; _ac=$4; _se=$5; _rg=$6
    sign HEAD "$_ep" "$_bk" "$_key" "" "$_ac" "$_se" "$_rg" "$EMPTY_SHA"
    curl -s -I -D "$WORK/head.txt" -o /dev/null -w '%{http_code}' -H @"$WORK/hdrs" "$URL"
}

head_size() { awk 'tolower($1)=="content-length:"{gsub(/\r/,"",$2); print $2}' "$WORK/head.txt"; }

require() {
    _missing=''
    [ -n "$SRC_ENDPOINT" ] || _missing="$_missing MigrationSourceR2__Endpoint"
    [ -n "$SRC_BUCKET" ]   || _missing="$_missing MigrationSourceR2__Bucket"
    [ -n "$SRC_ACCESS" ]   || _missing="$_missing MigrationSourceR2__AccessKey"
    [ -n "$SRC_SECRET" ]   || _missing="$_missing MigrationSourceR2__SecretKey"
    [ -n "$DST_ENDPOINT" ] || _missing="$_missing ObjectStorage__ServiceUrl"
    [ -n "$DST_BUCKET" ]   || _missing="$_missing ObjectStorage__Bucket"
    [ -n "$DST_ACCESS" ]   || _missing="$_missing ObjectStorage__AccessKey"
    [ -n "$DST_SECRET" ]   || _missing="$_missing ObjectStorage__SecretKey"
    if [ -n "$_missing" ]; then
        echo "FALTAN VARIABLES:$_missing"
        return 1
    fi
    return 0
}

echo "modo   : $MODE"
echo "origen : ${SRC_BUCKET:-<sin definir>}"
echo "destino: ${DST_BUCKET:-<sin definir>}"
echo

if [ "$MODE" = "--check" ]; then
    rc=0
    if [ -n "$SRC_ACCESS" ] && [ -n "$SRC_BUCKET" ]; then
        list_bucket "$SRC_ENDPOINT" "$SRC_BUCKET" "$SRC_ACCESS" "$SRC_SECRET" "$SRC_REGION"
        echo "origen  HTTP $LIST_CODE  objetos listados: $LIST_COUNT"
        [ "$LIST_CODE" = "200" ] || rc=1
    else
        echo "origen  SIN CREDENCIALES (token temporal aun no inyectado)"; rc=1
    fi
    if [ -n "$DST_ACCESS" ] && [ -n "$DST_BUCKET" ]; then
        list_bucket "$DST_ENDPOINT" "$DST_BUCKET" "$DST_ACCESS" "$DST_SECRET" "$DST_REGION"
        echo "destino HTTP $LIST_CODE  objetos listados: $LIST_COUNT"
        [ "$LIST_CODE" = "200" ] || rc=1
    else
        echo "destino SIN CREDENCIALES"; rc=1
    fi
    echo
    [ "$rc" = "0" ] && echo "CHECK PASS (ninguna escritura)" || echo "CHECK INCOMPLETO (ninguna escritura)"
    exit $rc
fi

require || exit 1
[ -f "$MANIFEST" ] || { echo "No existe el manifest: $MANIFEST"; exit 1; }

total=0; ok=0; missing=0; already=0; copied=0; failed=0; bytes=0
while IFS=',' read -r key size ctype slug kind; do
    [ "$key" = "storage_key" ] && continue
    [ -z "$key" ] && continue
    total=$((total+1))

    scode=$(head_object "$SRC_ENDPOINT" "$SRC_BUCKET" "$key" "$SRC_ACCESS" "$SRC_SECRET" "$SRC_REGION")
    ssize=$(head_size)
    if [ "$scode" != "200" ]; then
        echo "  ! ORIGEN $scode        $key"; missing=$((missing+1)); continue
    fi
    if [ "$ssize" != "$size" ]; then
        echo "  ! TAMANO origen=$ssize manifest=$size  $key"; missing=$((missing+1)); continue
    fi

    dcode=$(head_object "$DST_ENDPOINT" "$DST_BUCKET" "$key" "$DST_ACCESS" "$DST_SECRET" "$DST_REGION")
    dsize=$(head_size)
    if [ "$dcode" = "200" ] && [ "$dsize" = "$size" ]; then
        echo "  = ya presente         $key"; already=$((already+1)); continue
    fi

    if [ "$MODE" = "--dry-run" ]; then
        echo "  + copiaria  ${size}B   $key"; ok=$((ok+1)); bytes=$((bytes+size)); continue
    fi

    sign GET "$SRC_ENDPOINT" "$SRC_BUCKET" "$key" "" "$SRC_ACCESS" "$SRC_SECRET" "$SRC_REGION" "$EMPTY_SHA"
    gcode=$(curl -s -o "$WORK/obj" -w '%{http_code}' -H @"$WORK/hdrs" "$URL")
    if [ "$gcode" != "200" ]; then echo "  ! GET $gcode           $key"; failed=$((failed+1)); continue; fi

    psha=$(sha256_file "$WORK/obj")
    sign PUT "$DST_ENDPOINT" "$DST_BUCKET" "$key" "" "$DST_ACCESS" "$DST_SECRET" "$DST_REGION" "$psha" "$ctype"
    pcode=$(curl -s -o /dev/null -w '%{http_code}' -X PUT --data-binary @"$WORK/obj" -H @"$WORK/hdrs" "$URL")
    if [ "$pcode" = "200" ] || [ "$pcode" = "201" ]; then
        echo "  + copiado   ${size}B   $key"; copied=$((copied+1)); bytes=$((bytes+size))
    else
        echo "  ! PUT $pcode           $key"; failed=$((failed+1))
    fi
    rm -f "$WORK/obj"
done < "$MANIFEST"

echo
echo "manifest=$total  copiables=$ok  ya_presentes=$already  copiados=$copied  problemas_origen=$missing  fallidos=$failed  bytes=$bytes"
if [ "$MODE" = "--dry-run" ]; then echo "DRY-RUN: ninguna escritura ejecutada"; fi
[ "$missing" = "0" ] && [ "$failed" = "0" ] || exit 1
exit 0
