# Seguridad

Controles presentes: Identity, autorización por membresía y permiso, antiforgery, rate limit de escrituras públicas, hash HMAC de códigos, cifrado de datos personales, CSP, HSTS fuera de Development, encabezados `nosniff` y `DENY`.

Los secretos se reciben por ambiente y `.env` está ignorado. No usar las credenciales demo ni `Demo` en producción. Pendiente antes de producción: gestor de secretos, rotación ensayada, TLS en proxy, análisis SAST/dependencias en CI, retención y borrado de datos, observabilidad y pentest.
