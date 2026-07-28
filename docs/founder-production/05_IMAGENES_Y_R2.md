# 05 — Imágenes y Cloudflare R2

## Reglas aplicadas

| Regla | Cómo se aplica |
| --- | --- |
| Solo JPEG, PNG y WebP | El decodificador sólo tiene registrados esos tres formatos |
| SVG rechazado | No está registrado; la carga falla con `UNSUPPORTED_IMAGE` |
| Ejecutables rechazados | Se valida la **firma binaria**, no la extensión ni el content type |
| Máximo 5 MB de archivo original | Se rechaza con 413 antes de decodificar |
| Máximo 1600 px en el lado mayor | Se reescala manteniendo la proporción |
| Compresión | JPEG y WebP al 82 %; PNG con compresión máxima |
| Sin metadatos EXIF | Se descartan EXIF, XMP, ICC e IPTC del archivo y de cada fotograma |
| Nombre generado por el servidor | `businesses/{id}/{tipo}/{guid}.{ext}` |
| Un logo y una portada | Índice único parcial en base de datos; cargar otro reemplaza el anterior |
| Máximo 8 fotografías | `BusinessImage.MaximumGalleryImages`; la novena da 409 |
| Texto alternativo | Máximo 160 caracteres, sin HTML |
| Eliminación lógica | `SoftDelete` conserva la fila; el borrado físico del objeto es un paso posterior |

Cada una de estas reglas tiene una prueba automatizada. La eliminación de EXIF y el reescalado se
verifican descargando el archivo **ya servido** y volviéndolo a decodificar.

## Dónde no se guardan las imágenes

- No en PostgreSQL, ni como base64.
- No en el filesystem efímero del contenedor.
- No dentro de `/app/keys`.
- No en `wwwroot`.
- No en Git.

## Proveedores

| Ambiente | Proveedor | Dónde |
| --- | --- | --- |
| Development | `Local` | Carpeta temporal del sistema |
| Demo | `Local` | Volumen `/app/media` |
| Production | `S3` | Bucket de Cloudflare R2 |

El proveedor local sirve las imágenes por `/media/{clave}` con una comprobación que impide que
una clave manipulada escape de la carpeta raíz. En Production ese endpoint devuelve 404: las
imágenes se sirven desde el dominio público del bucket.

**El arranque de Production falla si el proveedor es `Local`**, porque el disco del contenedor es
efímero.

## Variables requeridas

```
ObjectStorage__Provider
ObjectStorage__ServiceUrl
ObjectStorage__Bucket
ObjectStorage__AccessKey
ObjectStorage__SecretKey
ObjectStorage__PublicBaseUrl
ObjectStorage__Region
```

Los valores se entregan por el panel de la plataforma de despliegue, nunca por Git.

## Configuración de Cloudflare R2 — pasos manuales

Estos pasos **requieren iniciar sesión en Cloudflare** y no se pueden automatizar desde aquí.

1. Entre a <https://dash.cloudflare.com> con la cuenta de la organización.
2. En el menú lateral, **R2 Object Storage**. Si es la primera vez, Cloudflare pedirá activar R2 y
   registrar un método de pago (ver el apartado de costos más abajo).
3. **Create bucket**. Nombre sugerido: `urabaconecta-prod`. Ubicación: automática o
   *Eastern North America*, la más cercana a Colombia.
4. Cree un segundo bucket `urabaconecta-demo` si quiere probar la carga sin tocar producción.
5. En el bucket, pestaña **Settings** → **Public access**:
   - opción A: habilitar el subdominio público `r2.dev` (rápido, sin dominio propio);
   - opción B: conectar un dominio propio, por ejemplo `imagenes.urabaconecta.co` (recomendado
     para producción real).
   El valor resultante es `ObjectStorage__PublicBaseUrl`.
6. Vuelva a **R2** → **Manage API tokens** → **Create API token**:
   - permiso: **Object Read & Write**;
   - alcance: sólo el bucket creado;
   - copie **Access Key ID** y **Secret Access Key**: el secreto se muestra una sola vez.
7. En la misma pantalla aparece el **endpoint S3**, con la forma
   `https://<id-de-cuenta>.r2.cloudflarestorage.com`. Ese es `ObjectStorage__ServiceUrl`.
8. Registre en el proveedor de despliegue:

```
ObjectStorage__Provider=S3
ObjectStorage__ServiceUrl=https://<id-de-cuenta>.r2.cloudflarestorage.com
ObjectStorage__Bucket=urabaconecta-prod
ObjectStorage__AccessKey=<Access Key ID>
ObjectStorage__SecretKey=<Secret Access Key>
ObjectStorage__PublicBaseUrl=https://<subdominio-publico-o-dominio-propio>
ObjectStorage__Region=auto
```

9. Verifique en `/admin/salud`: la fila «Almacenamiento de objetos» debe decir
   `S3 — Disponible`.

## Detalles técnicos de la integración

- `ForcePathStyle = true`: R2 no admite direccionamiento por subdominio de bucket.
- `DisablePayloadSigning = true` y las sumas de verificación en modo `WHEN_REQUIRED`: R2 rechaza
  los encabezados de suma de verificación que el SDK de AWS añade por omisión.
- `AuthenticationRegion = auto`.

## Costos

Cloudflare R2 cobra por almacenamiento y por operaciones, sin cargo por transferencia de salida.
Para el volumen del piloto —cinco negocios, un logo, una portada y hasta ocho fotografías cada
uno, todas por debajo de 1600 px— el consumo está muy por debajo del nivel gratuito mensual.
Aun así, **activar R2 exige registrar un método de pago**: esa decisión es del usuario y no se
toma desde aquí. Consulte la tarifa vigente en <https://developers.cloudflare.com/r2/pricing/>
antes de activar.
