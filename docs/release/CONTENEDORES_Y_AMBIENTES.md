# Contenedores y ambientes

Copie `.env.example` a `.env`, reemplace contraseñas y clave HMAC, y ejecute `docker compose up -d --build`. La aplicación queda en `http://localhost:8088`. La imagen usa compilación multi-stage y el proceso final corre como usuario `app`. PostgreSQL y la aplicación tienen health checks.

`Development` y `Demo` migran y cargan datos demostrativos de forma idempotente. `Production` no debe sembrar ni migrar automáticamente: aplique un bundle/`dotnet ef database update` como paso controlado. Las llaves de Data Protection persisten en volumen.
