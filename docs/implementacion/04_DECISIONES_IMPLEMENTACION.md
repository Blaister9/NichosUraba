# Decisiones de implementación

1. **Monolito modular.** `Domain` no depende de infraestructura; `Application` contiene casos de uso; `Infrastructure` implementa persistencia y seguridad; `Web` compone el host; `Web.Client` contiene componentes Interactive Auto.
2. **Interactive Auto sin duplicar reglas.** Los componentes consumen `IUrabaConectaApi`. En servidor, la fachada llama casos de uso directamente; en WebAssembly, usa la API HTTP versionada.
3. **Aislamiento en varias capas.** Las rutas privadas exigen autenticación/rol, el caso de uso resuelve membresía persistida y cada consulta/actualización filtra explícitamente `BusinessId`. Las FKs compuestas impiden asociaciones cruzadas.
4. **Concurrencia en PostgreSQL.** `btree_gist` y `EXCLUDE` bloquean rangos solapados para citas `Pending` o `Confirmed` del mismo trabajador. La creación usa transacción serializable y traduce la violación a `409 SLOT_UNAVAILABLE`.
5. **Tiempo explícito.** Los instantes se guardan como `timestamp with time zone` en UTC. Fechas y horarios se calculan con `America/Bogota`.
6. **Código público.** Se generan 16 bytes aleatorios, se codifican Base64URL y solo se almacena HMAC-SHA256. La clave procede de configuración externa.
7. **Datos personales.** Alias, teléfono y observación se protegen con ASP.NET Core Data Protection; solo se guarda aparte los últimos cuatro dígitos para seguimiento enmascarado.
8. **Identidad oficial.** Se conservó Identity de la plantilla, con claves `Guid`, cookies, roles y bloqueo. El registro público se deshabilitó.
9. **Datos demo idempotentes.** El seed corre solo en Development y crea categorías, municipios, negocios, membresías, personal, servicios y horario.
10. **Sin infraestructura anticipada.** No se añadieron colas, caché, mensajería, pagos, PWA ni módulos de futuros verticales.
