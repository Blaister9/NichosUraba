"""Reparte la salida de generate.sql en los ficheros del paquete.

No imprime el contenido: sólo el nombre de cada fichero y cuántas sentencias lleva.
20_identity.sql contiene hashes de contraseña y nunca debe volcarse a consola.
"""
import os
import sys

raw_path, out_dir = sys.argv[1], sys.argv[2]
os.makedirs(out_dir, exist_ok=True)

MARK = "-- @@FILE:"
SENSITIVE = {"20_identity.sql"}

current, buffers = None, {}
order = []
with open(raw_path, encoding="utf-8") as fh:
    for line in fh:
        stripped = line.strip()
        if stripped.startswith(MARK):
            current = stripped[len(MARK):].strip()
            if current not in buffers:
                buffers[current] = []
                order.append(current)
            continue
        if current is not None:
            buffers[current].append(line.rstrip("\n"))

print(f"{'fichero':<24} {'lineas':>7} {'sentencias':>11}  nota")
print("-" * 66)
for name in order:
    lines = buffers[name]
    while lines and not lines[-1].strip():
        lines.pop()
    body = "\n".join(lines) + "\n"
    with open(os.path.join(out_dir, name), "w", encoding="utf-8", newline="\n") as fh:
        if name.endswith(".sql"):
            fh.write("-- generado por generate.sql desde el piloto. No editar a mano.\n")
        fh.write(body)
    statements = sum(1 for x in lines if x.strip().endswith(";"))
    note = "SENSIBLE - no imprimir" if name in SENSITIVE else ""
    print(f"{name:<24} {len(lines):>7} {statements:>11}  {note}")
