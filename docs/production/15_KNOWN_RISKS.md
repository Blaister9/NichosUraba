# 15 — Riesgos conocidos

Ordenados por lo que costaría que ocurrieran. Ninguno es desconocido: todos están aquí para que
la decisión de convivir con ellos sea consciente.

## 1. Pérdida del volumen de llaves — impacto crítico

Si se pierde `/app/keys`, los datos personales cifrados (alias, teléfonos, notas de clientes)
quedan **ilegibles de forma permanente**. Restaurar PostgreSQL no los recupera.

**Mitigación**: respaldo del volumen documentado en `06_DATA_PROTECTION.md`, verificación
obligatoria antes del go-live, y nunca descartar respaldos antiguos de llaves.

**Residual**: mientras el respaldo de llaves sea manual, depende de que alguien lo ejecute.

## 2. Anillo de llaves sin cifrar en reposo — impacto alto

Hoy `/app/keys` se persiste sin cifrado adicional. Quien obtenga una copia del volumen puede
descifrar los datos personales de todos los clientes.

**Mitigación**: configurar `DataProtection__CertificateBase64`. El código ya lo soporta;
`06_DATA_PROTECTION.md` explica el procedimiento. **Está pendiente de ejecutar.**

## 3. Respaldos manuales — impacto alto

`ops/backup-postgres.ps1` está probado y funciona, pero **alguien tiene que ejecutarlo**. Un olvido
de dos semanas es una pérdida de dos semanas.

**Mitigación**: programar la tarea. Mientras tanto, el calendario de `05_BACKUP_RESTORE.md` y el
respaldo obligatorio antes de cada despliegue con migraciones.

## 4. Un solo administrador de plataforma — impacto alto

Production nace con un `PlatformAdmin`. Si esa persona pierde el acceso y no hay otra, no queda
quién invite ni recupere cuentas. `ProductionBootstrap` es de una sola ejecución: **no repone el
acceso una segunda vez**.

**Mitigaciones disponibles**: crear un segundo `PlatformAdmin` por invitación en cuanto el
primero esté operativo, y conservar acceso al panel de Railway, desde donde siempre se puede
operar sobre la base.

## 5. Sin alertas automáticas por métrica — impacto medio

Railway Hobby no ofrece alertas con umbral por CPU, memoria o 5xx. Un problema puede pasar
inadvertido hasta que alguien mire.

**Mitigación**: la rutina de revisión manual de `07_MONITORING.md` (diaria durante las 48 h
posteriores a cada incorporación, semanal en régimen).

## 6. Aplicación y PostgreSQL en regiones distintas — impacto medio

US West y US East: unos **73 ms por consulta**. Una pantalla que hace veinte consultas paga un
segundo y medio sólo en viajes de red.

**Mitigación aplicada**: pruebas de regresión que afirman cuántas sentencias cuesta cada pantalla,
lo que impide que un N+1 entre sin ser visto.

**Recomendación futura**: alinear ambos recursos en la misma región. **No se hizo aquí**: mover
una base productiva exige ventana de indisponibilidad y es una decisión aparte. Hacerlo antes de
incorporar los cinco negocios es más barato que después.

## 7. Retención sin automatizar — impacto medio

La política de `09_LEGAL_CONFIGURATION.md` está definida pero su ejecución es manual. Con el
tiempo se acumulan datos personales más allá de su plazo, lo que es un incumplimiento.

**Mitigación**: revisión trimestral hasta que exista una tarea programada.

## 8. Sin segundo factor — impacto medio

El acceso administrativo depende de una contraseña. Hay bloqueo por intentos (5 en 15 minutos) y
longitud mínima de 10, pero no hay segundo factor.

**Mitigación**: contraseñas largas y únicas, gestionadas con un gestor de contraseñas.

## 9. `style-src 'unsafe-inline'` en la CSP — impacto bajo

Blazor emite estilos en línea y quitarlo rompe el render. Está acotado a estilos: `script-src` no
lo permite, que es donde importaría.

## 10. Sembrado de Demo — riesgo controlado, se documenta por historia

El enriquecimiento de la demostración **ya dejó el despliegue en 502 dos veces**. Hoy:

- no puede ejecutarse en Production (retorno temprano por ambiente, más `StartupGuard`);
- está envuelto en captura de excepciones **en sus dos rutas**, no sólo en una;
- las migraciones ya no dependen de él.

Se considera cerrado, y aparece aquí para que nadie lo reabra sin saber lo que costó.

## 11. Restauración probada en ensayo, no en Production — impacto bajo

La restauración se ejecutó de verdad, pero contra una base de ensayo. La primera restauración
sobre la instancia productiva real seguirá siendo la primera.

**Mitigación**: el ensayo mensual de `05_BACKUP_RESTORE.md`.

## 12. Primer arranque productivo sin ensayo end-to-end — impacto bajo

La creación del primer `PlatformAdmin` está cubierta por pruebas automatizadas contra PostgreSQL
real, pero el arranque completo de un ambiente Production nuevo, con sus variables reales, sólo
ocurrirá una vez: en el go-live.

**Mitigación**: `12_GO_LIVE.md` lo trata como paso verificado, con criterio de detención claro si
el administrador no puede iniciar sesión.
