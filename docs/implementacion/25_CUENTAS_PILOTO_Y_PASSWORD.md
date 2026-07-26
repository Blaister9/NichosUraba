# Cuentas piloto y contraseña

La creación directa de cuentas piloto está habilitada únicamente en `Development` o `Demo`. En `Production` debe existir un flujo externo aprobado; no se ofrece contraseña permanente desde administración.

La contraseña temporal:

- se genera con aleatoriedad criptográfica;
- no reutiliza la clave demo;
- se devuelve una sola vez en la respuesta de creación;
- no se persiste en texto claro ni se registra;
- marca `MustChangePassword`.

Al iniciar sesión, la cuenta se redirige a `/Account/ChangeTemporaryPassword`. El middleware bloquea panel y API hasta completar el cambio. El cambio usa ASP.NET Identity, elimina la marca, refresca la sesión y registra el evento sin secretos. La clave temporal deja de funcionar.

La entrega de la clave queda a cargo del equipo piloto mediante un canal acordado. No se implementaron correo, SMS ni recuperación externa.
