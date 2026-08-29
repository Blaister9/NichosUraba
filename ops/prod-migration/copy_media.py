"""Copia los objetos del manifest del bucket del piloto al bucket productivo.

PREPARADO, NO EJECUTADO. Requiere --confirm para escribir.

Origen  : ObjectStorage__*        (inyectadas por `railway run --service NichosUraba -e production`)
Destino : DEST_ObjectStorage__*   (exportadas a mano para el bucket productivo nuevo)

Conserva la StorageKey exacta: la aplicación compone la URL pública en ejecución
(PublicUrl = PublicBaseUrl + '/' + key), así que ninguna fila de base cambia.
Idempotente: si el objeto ya existe en destino con el mismo tamaño, lo salta.
Las credenciales nunca se imprimen.
"""
import csv
import datetime
import hashlib
import hmac
import os
import sys
import urllib.parse
import urllib.request

MANIFEST = os.path.join(os.path.dirname(os.path.abspath(__file__)), "pkg", "media_manifest.csv")
CONFIRM = "--confirm" in sys.argv
EMPTY_SHA = hashlib.sha256(b"").hexdigest()


def cfg(prefix):
    c = {
        "access": os.environ.get(f"{prefix}ObjectStorage__AccessKey", ""),
        "secret": os.environ.get(f"{prefix}ObjectStorage__SecretKey", ""),
        "bucket": os.environ.get(f"{prefix}ObjectStorage__Bucket", ""),
        "endpoint": os.environ.get(f"{prefix}ObjectStorage__ServiceUrl", "").rstrip("/"),
        "region": os.environ.get(f"{prefix}ObjectStorage__Region", "auto"),
    }
    missing = [k for k, v in c.items() if not v]
    if missing:
        sys.exit(f"Falta configuracion {prefix or '(origen)'}: {', '.join(missing)}")
    return c


def _sign(key, msg):
    return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()


def request(c, method, key, body=None, content_type=None):
    host = urllib.parse.urlparse(c["endpoint"]).netloc
    payload = body if body is not None else b""
    payload_sha = hashlib.sha256(payload).hexdigest()
    now = datetime.datetime.now(datetime.timezone.utc)
    amzdate = now.strftime("%Y%m%dT%H%M%SZ")
    datestamp = now.strftime("%Y%m%d")
    uri = "/" + urllib.parse.quote(c["bucket"]) + "/" + urllib.parse.quote(key)
    headers = {"host": host, "x-amz-content-sha256": payload_sha, "x-amz-date": amzdate}
    if content_type:
        headers["content-type"] = content_type
    signed = ";".join(sorted(headers))
    canonical_headers = "".join(f"{k}:{headers[k]}\n" for k in sorted(headers))
    canonical = "\n".join([method, uri, "", canonical_headers, signed, payload_sha])
    scope = f"{datestamp}/{c['region']}/s3/aws4_request"
    sts = "\n".join(["AWS4-HMAC-SHA256", amzdate, scope,
                     hashlib.sha256(canonical.encode()).hexdigest()])
    k = _sign(("AWS4" + c["secret"]).encode(), datestamp)
    for part in (c["region"], "s3", "aws4_request"):
        k = _sign(k, part)
    sig = hmac.new(k, sts.encode(), hashlib.sha256).hexdigest()
    headers["Authorization"] = (f"AWS4-HMAC-SHA256 Credential={c['access']}/{scope}, "
                                f"SignedHeaders={signed}, Signature={sig}")
    req = urllib.request.Request(c["endpoint"] + uri, data=body, method=method)
    for hk, hv in headers.items():
        req.add_header(hk, hv)
    return urllib.request.urlopen(req, timeout=120)


src, dst = cfg(""), cfg("DEST_")
if src["bucket"] == dst["bucket"] and src["endpoint"] == dst["endpoint"]:
    sys.exit("Origen y destino son el mismo bucket. Abortado.")

rows = [r for r in csv.DictReader(open(MANIFEST, encoding="utf-8")) if r.get("storage_key")]
print(f"manifest: {len(rows)} objetos   destino: {dst['bucket']}")
if not CONFIRM:
    print("MODO SIMULACION (sin --confirm): no se escribe nada.\n")

copied = skipped = failed = 0
for r in rows:
    key, size = r["storage_key"], int(r["byte_size"])
    try:
        head = request(dst, "HEAD", key)
        if int(head.headers.get("Content-Length", -1)) == size:
            print(f"  = ya presente  {key}")
            skipped += 1
            continue
    except Exception:
        pass
    if not CONFIRM:
        print(f"  + copiaria    {key}  ({size} B)")
        copied += 1
        continue
    try:
        body = request(src, "GET", key).read()
        if len(body) != size:
            raise ValueError(f"tamano origen {len(body)} != manifest {size}")
        request(dst, "PUT", key, body=body, content_type=r["content_type"])
        print(f"  + copiado     {key}  ({size} B)")
        copied += 1
    except Exception as exc:  # noqa: BLE001
        print(f"  ! ERROR       {key}: {type(exc).__name__}: {exc}")
        failed += 1

print(f"\ncopiados={copied} ya_presentes={skipped} fallidos={failed}")
sys.exit(1 if failed else 0)
