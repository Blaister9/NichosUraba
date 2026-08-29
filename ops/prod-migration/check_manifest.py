"""Contrasta el manifest de media contra el listado vivo del bucket del piloto.

Confirma que cada objeto a copiar existe y que su tamaño coincide con el de la
fila en base. No descarga ni copia nada.
"""
import csv
import os
import sys

BASE = r"C:/Users/santi/AppData/Local/Temp/claude/C--Users-santi-Documents-NichosUraba/2b076f74-2bfe-4500-b3a6-a0784ea51d2e/scratchpad"
MANIFEST = os.path.join(BASE, "migration", "pkg", "media_manifest.csv")
LISTING = os.path.join(BASE, sys.argv[1] if len(sys.argv) > 1 else "r2_objects_cutover.csv")

r2 = {}
with open(LISTING, encoding="utf-8") as f:
    for row in csv.DictReader(f):
        r2[row["key"]] = int(row["size"])

rows = []
with open(MANIFEST, encoding="utf-8") as f:
    for row in csv.DictReader(f):
        if row.get("storage_key"):
            rows.append(row)

missing, mismatched, ok = [], [], 0
total_bytes = 0
per_biz = {}
for row in rows:
    key, size = row["storage_key"], int(row["byte_size"])
    slug = row["business_slug"]
    per_biz.setdefault(slug, [0, 0])
    if key not in r2:
        missing.append(key)
    elif r2[key] != size:
        mismatched.append((key, size, r2[key]))
    else:
        ok += 1
        total_bytes += size
        per_biz[slug][0] += 1
        per_biz[slug][1] += size

print(f"manifest: {len(rows)} objetos")
print(f"presentes y con tamano coincidente: {ok}")
print(f"ausentes en R2: {len(missing)}")
for k in missing:
    print("   FALTA>", k)
print(f"tamano discrepante: {len(mismatched)}")
for k, a, b in mismatched:
    print(f"   DIFF> {k} db={a} r2={b}")
print()
for slug, (n, b) in sorted(per_biz.items()):
    print(f"  {slug:<14} {n:>3} objetos  {b:>9} bytes")
print(f"  {'TOTAL':<14} {ok:>3} objetos  {total_bytes:>9} bytes")

# Nada del manifest puede apuntar a un objeto marcado como borrado ni fuera de los
# dos prefijos autorizados.
PREFIXES = ("businesses/266e8c06dbc84f4b8937d32f69fb87cf/",
            "businesses/9dc7d8ea033341469e509cf124ac9f0c/")
stray = [r["storage_key"] for r in rows if not r["storage_key"].startswith(PREFIXES)]
print(f"\nclaves fuera de los dos prefijos autorizados: {len(stray)}")
for k in stray:
    print("   FUERA>", k)

print("\nRESULTADO:", "MANIFEST VALIDO" if not (missing or mismatched or stray) else "*** MANIFEST INVALIDO ***")
