# 03 — Proceso de publicación

## Regla

**Cada ambiente se despliega únicamente desde su rama de release.** Production desde
`release/founder-production`; Demo desde `release/pilot-demo`. **Ninguna rama `feat/*` autodespliega
nada.** Esas ramas existen para que lo que se enseña o lo que corre frente a negocios reales sea un
punto conocido y revisado, no la última idea en curso.

La regla se escribió antes de cumplirse. Entre el 2026-07-28 y el 2026-08-11 el servicio Demo estuvo
observando `feat/v5-founder-production`, así que cada `git push` de trabajo publicaba en Demo sin que
nadie lo decidiera. Costó una auditoría entera descubrirlo, porque `release/pilot-demo` seguía 26
commits atrás y parecía —falsamente— que Demo estaba desactualizada. Si alguna vez el SHA vivo no
coincide con la punta de la rama de release, lo primero que hay que mirar es qué rama observa Railway.

## Flujo

```
feat/v6-product-experience        ← rama de trabajo; su push NO despliega nada
   │  desarrollo y pruebas locales
   ▼
dotnet test -c Release            ← toda la suite en verde
   │
   ▼
promoción explícita               ← fast-forward o cherry-pick, decidida a mano
   │
   ▼
release/pilot-demo                ← su push SÍ despliega Demo
   │
   ▼
validación funcional en Demo      ← recorrido real de la función, con datos ficticios
   │
   ▼
merge o cherry-pick a release/founder-production
   │
   ▼
Production                        ← despliegue automático de Railway desde esa rama
   │
   ▼
ops/smoke-production.ps1          ← comprobación de humo, sólo lectura
```

Promover a Demo es un acto deliberado, no un efecto secundario de guardar el trabajo. Si la historia
lo permite, `git fetch . <sha>:release/pilot-demo` avanza la rama sólo si el movimiento es
fast-forward y falla si no lo es, que es exactamente la garantía que se quiere.

## Configuración en Railway

- Servicio Production: rama observada `release/founder-production`.
- Servicio Demo: rama observada `release/pilot-demo`.
- Health check: `/health/ready`.
- Reinicio: `ON_FAILURE`, máximo 3 reintentos. **No usar `ALWAYS`**: oculta fallos de
  configuración detrás de un ciclo de reinicios.
- Una réplica. Sin autoescalado.

## Antes de promover un commit

```bash
dotnet tool restore --tool-manifest dotnet-tools.json
```

```bash
dotnet tool run dotnet-ef migrations has-pending-model-changes --project src/UrabaConecta.Infrastructure --startup-project src/UrabaConecta.Web/UrabaConecta.Web --context AppDbContext
```

Lista de verificación:

1. Suite completa en verde en Release.
2. Sin cambios de modelo pendientes de migración.
3. Si hay migraciones nuevas: **respaldo antes de desplegar** (`ops/backup-postgres.ps1`).
4. `Deployment__Commit` actualizado al SHA que se despliega.
5. Tras el despliegue: `ops/smoke-production.ps1`.

## Prohibiciones

- No hacer push directo a `release/founder-production` sin haber validado en Demo.
- No desplegar Production un viernes por la tarde ni sin respaldo reciente.
- No promover una migración que no se haya aplicado antes sobre una copia restaurada.
- No apuntar Railway a una rama de trabajo «mientras tanto». Es la vía por la que Demo dejó de tener
  una fuente identificable.
- No cambiar la rama observada por Railway antes de que esa rama ya contenga el commit que se está
  sirviendo: apuntar a una rama atrasada hace retroceder el código contra una base ya migrada hacia
  adelante, y la readiness no lo detecta —`GetPendingMigrationsAsync` sólo ve migraciones que faltan
  en la base, nunca una base más nueva que el binario.

## Reversión

Ver `11_ROLLBACK.md`.
