# 11 — Reversión

## Principio

Revertir código es barato. Revertir una migración de base de datos no lo es. La mayoría de las
reversiones son sólo de código, y por eso el orden de las preguntas importa.

## Decisión

```
¿El despliegue fallido incluía migraciones?
│
├── NO  → Reversión de código. Minutos. Sin pérdida de datos.
│
└── SÍ  → ¿La migración llegó a aplicarse?
          │
          ├── NO  → Reversión de código. La base sigue en el esquema anterior.
          │
          └── SÍ  → ¿Es compatible hacia atrás?
                    │
                    ├── SÍ  → Reversión de código; la base queda adelantada. Aceptable.
                    │
                    └── NO  → Restauración desde respaldo. Hay pérdida de datos
                              desde el respaldo hasta ahora.
```

La pantalla privada de salud informa qué migraciones se aplicaron **en ese arranque**, que es
exactamente el dato que responde a la segunda pregunta.

## A — Reversión de código

1. En Railway, servicio Production, historial de despliegues.
2. Seleccionar el despliegue anterior sano y usar «Redeploy».
3. Alternativa por Git:

```bash
git revert --no-edit <sha-malo>
```

```bash
git push origin release/founder-production
```

4. Esperar a que `/health/ready` responda 200.
5. `./ops/smoke-production.ps1 -BaseUrl https://<dominio>`

**No usar `git push --force` sobre `release/founder-production`.** Un revert deja historia; un
force-push la borra y con ella la posibilidad de entender qué pasó.

## B — Restauración desde respaldo

Sólo cuando una migración incompatible ya se aplicó y no hay camino hacia adelante.

1. **Anunciar la indisponibilidad.** Esta ruta pierde datos.
2. Detener el servicio web, para que nadie escriba durante la operación.
3. Respaldar el estado actual **aunque esté roto**: es la única copia de los datos posteriores al
   último respaldo bueno.

```powershell
./ops/backup-postgres.ps1 -Destination "D:\respaldos" -Label incidente
```

4. Restaurar el respaldo bueno en una base **temporal** y verificarlo:

```powershell
./ops/restore-postgres.ps1 -DumpFile <respaldo-bueno> -TargetDatabase urabaconecta_verificacion
```

5. Sólo si la verificación pasa, restaurar sobre la base productiva con `-Overwrite`.
6. Revertir el código a la versión que corresponde a ese esquema.
7. Comprobar que las llaves de Data Protection **no cambiaron**: si el volumen sigue intacto, los
   datos personales se leen. Si se restauró también el volumen, debe ser el del mismo periodo.
8. Arrancar, verificar `ready`, ejecutar el humo y la aceptación autenticada.
9. Documentar qué ventana de datos se perdió.

## Reversión de migraciones con EF

Para bajar a una migración concreta, con respaldo previo y fuera de horas:

```bash
dotnet tool run dotnet-ef database update <MigracionAnterior> --project src/UrabaConecta.Infrastructure --startup-project src/UrabaConecta.Web/UrabaConecta.Web --context AppDbContext
```

Funciona sólo si la migración generó un `Down` correcto. Una migración que elimina una columna
**no puede devolver sus datos**: el `Down` recrea la columna vacía. Por eso el respaldo previo a
cualquier despliegue con migraciones no es opcional.

## Prevención

- Respaldo verificado antes de todo despliegue con migraciones.
- Aplicar la migración primero sobre una copia restaurada.
- Preferir migraciones aditivas: agregar columna anulable, poblar, y sólo después dejar de usar la
  vieja, en un despliegue posterior.
- Evitar eliminar columnas en el mismo despliegue que deja de usarlas.
