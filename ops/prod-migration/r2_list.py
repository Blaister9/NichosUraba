"""READ-ONLY R2 inventory.

Reads ObjectStorage__* from the environment (injected by `railway run`), performs
SigV4-signed ListObjectsV2 requests, and prints ONLY object keys/sizes/dates.
Credentials are never printed, logged, or written to disk.
"""
import datetime
import hashlib
import hmac
import os
import sys
import urllib.parse
import urllib.request
import xml.etree.ElementTree as ET

ACCESS = os.environ.get("ObjectStorage__AccessKey", "")
SECRET = os.environ.get("ObjectStorage__SecretKey", "")
BUCKET = os.environ.get("ObjectStorage__Bucket", "")
ENDPOINT = os.environ.get("ObjectStorage__ServiceUrl", "").rstrip("/")
REGION = os.environ.get("ObjectStorage__Region", "auto")
SERVICE = "s3"
EMPTY_SHA = hashlib.sha256(b"").hexdigest()

if not (ACCESS and SECRET and BUCKET and ENDPOINT):
    print("MISSING_CONFIG", file=sys.stderr)
    sys.exit(2)


def _sign(key, msg):
    return hmac.new(key, msg.encode("utf-8"), hashlib.sha256).digest()


def signed_get(query):
    host = urllib.parse.urlparse(ENDPOINT).netloc
    now = datetime.datetime.now(datetime.timezone.utc)
    amzdate = now.strftime("%Y%m%dT%H%M%SZ")
    datestamp = now.strftime("%Y%m%d")
    canonical_uri = "/" + urllib.parse.quote(BUCKET)
    canonical_qs = "&".join(
        f"{urllib.parse.quote(k, safe='-_.~')}={urllib.parse.quote(v, safe='-_.~')}"
        for k, v in sorted(query.items())
    )
    canonical_headers = (
        f"host:{host}\n"
        f"x-amz-content-sha256:{EMPTY_SHA}\n"
        f"x-amz-date:{amzdate}\n"
    )
    signed_headers = "host;x-amz-content-sha256;x-amz-date"
    canonical_request = "\n".join(
        ["GET", canonical_uri, canonical_qs, canonical_headers, signed_headers, EMPTY_SHA]
    )
    scope = f"{datestamp}/{REGION}/{SERVICE}/aws4_request"
    string_to_sign = "\n".join(
        [
            "AWS4-HMAC-SHA256",
            amzdate,
            scope,
            hashlib.sha256(canonical_request.encode("utf-8")).hexdigest(),
        ]
    )
    k = _sign(("AWS4" + SECRET).encode("utf-8"), datestamp)
    k = _sign(k, REGION)
    k = _sign(k, SERVICE)
    k = _sign(k, "aws4_request")
    signature = hmac.new(k, string_to_sign.encode("utf-8"), hashlib.sha256).hexdigest()
    auth = (
        f"AWS4-HMAC-SHA256 Credential={ACCESS}/{scope}, "
        f"SignedHeaders={signed_headers}, Signature={signature}"
    )
    url = f"{ENDPOINT}{canonical_uri}?{canonical_qs}"
    req = urllib.request.Request(url, method="GET")
    req.add_header("Host", host)
    req.add_header("x-amz-content-sha256", EMPTY_SHA)
    req.add_header("x-amz-date", amzdate)
    req.add_header("Authorization", auth)
    with urllib.request.urlopen(req, timeout=60) as resp:
        return resp.read()


NS = "{http://s3.amazonaws.com/doc/2006-03-01/}"
token = None
total = 0
total_bytes = 0
print("key,size,last_modified")
while True:
    q = {"list-type": "2", "max-keys": "1000"}
    if token:
        q["continuation-token"] = token
    try:
        body = signed_get(q)
    except Exception as exc:  # noqa: BLE001
        print(f"ERROR {type(exc).__name__}: {exc}", file=sys.stderr)
        sys.exit(1)
    root = ET.fromstring(body)
    for c in root.findall(f"{NS}Contents"):
        key = c.findtext(f"{NS}Key", "")
        size = c.findtext(f"{NS}Size", "0")
        lm = c.findtext(f"{NS}LastModified", "")
        print(f"{key},{size},{lm}")
        total += 1
        total_bytes += int(size)
    if root.findtext(f"{NS}IsTruncated", "false") == "true":
        token = root.findtext(f"{NS}NextContinuationToken")
    else:
        break
print(f"# TOTAL_OBJECTS={total} TOTAL_BYTES={total_bytes}", file=sys.stderr)
