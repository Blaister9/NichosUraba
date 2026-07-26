# Rendimiento y carga

Los listados privados de citas y pedidos se limitan a 200 registros para impedir respuestas sin cota. La paginación es deuda técnica.

Prueba reproducible:

```powershell
powershell -File tests/load/run-load-smoke.ps1 -BaseUrl http://localhost:8088 -DurationSeconds 60
```

Escenarios: directorio y perfil con 20 usuarios; menú público con 10. Cada usuario emite aproximadamente una solicitud por segundo. Resultado en `docs/release/results/load-results.json`. Criterio de esta demo local: 0 errores y p95 menor a 1.000 ms. No extrapolar a producción.

Resultado del 25 de julio de 2026 en Windows, Intel Core i5-12400 y 63,7 GB RAM, contra contenedores locales: directorio 1.320 solicitudes, 0 errores, p95 7,63 ms; perfil 1.320, 0 errores, p95 7,66 ms; menú 660, 0 errores, p95 6,01 ms; seguimiento con código válido 660, 0 errores, p95 5,71 ms.

La ráfaga reproducible `powershell -File tests/load/run-write-concurrency.ps1` creó concurrentemente 5 turnos y 5 pedidos: 10/10 respuestas 201.

Limitación: el panel autenticado no quedó sometido a carga sostenida; debe completarse antes de afirmar capacidad productiva.
