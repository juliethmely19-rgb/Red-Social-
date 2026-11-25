# Red Social

API REST desarrollada con ASP.NET Core .NET 10 y PostgreSQL. Permite gestionar usuarios mediante operaciones CRUD, con persistencia de datos en una base de datos PostgreSQL.

## Configuración (`.env`)

Todo se configura en un único archivo `.env` en la raíz. Lo leen **Docker Compose** y también **`dotnet run`** (el backend lo carga solo al arrancar).

```bash
cp .env.example .env   # y completa los secretos
```

Variables principales: `BACKEND_PORT=5290`, `FRONTEND_PORT=5500`, `BACKEND_REPLICAS=3`, `BACKEND_URL`, `FRONTEND_URL`, `POSTGRES_*`, `OPENIDDICT_CERT_PASSWORD` y las credenciales de cada proveedor (`GOOGLE_*`, `GITHUB_*`, `DROPBOX_*`, `FIGMA_*`, `TWITTER_*`, `TIKTOK_*`). Los comentarios del archivo explican cada una. Las variables ya definidas en la terminal tienen prioridad sobre el `.env`.

> Mantén `BACKEND_PORT=5290`: las URLs de callback registradas en los proveedores OAuth apuntan a ese puerto.

## Ejecutar en local con `dotnet run`

Requisitos: .NET 10 y una base PostgreSQL. La forma más simple es usar la del compose (publica `127.0.0.1:5433`):

```bash
docker compose -f contructor.yml up -d postgres
```

Luego:

```bash
cd "Tarea4-Red Social grupo777"
dotnet run
```

Con `dotnet run` (entorno Development):

- La conexión a PostgreSQL se arma con `POSTGRES_*` del `.env` contra `localhost:5433`.
- Las migraciones se aplican solas y el cliente OpenIddict se registra al arrancar.
- Usa los certificados de `certificados/*.pfx` (con `OPENIDDICT_CERT_PASSWORD`); si no existen, usa certificados de desarrollo.
- Las páginas las sirve el propio backend: <http://localhost:5290/login.html> (también `/login/login.html`, `/Bienvenida/bienvenida.html`, `/callback.html`).
- Para el flujo completo con el frontend en el 5500 abre `Frontend/` con Live Server (raíz = carpeta `Frontend`) o levanta `docker compose -f contructor.yml up -d frontend`.

Si aparece `address already in use` en el 5290 (por ejemplo, porque el compose está levantado), libéralo con `docker compose -f contructor.yml down` o `fuser -k 5290/tcp`.

## Ejecutar con Docker (Construcción)

```bash
docker compose -f contructor.yml up -d --build
docker compose -f contructor.yml ps
```

Servicios:

- **balanceador:** nginx en el puerto **5290** (`nginx/nginx.conf`), reparte entre las réplicas del backend.
- **backend:** `BACKEND_REPLICAS` réplicas (3 por defecto) sin puerto publicado; comparten PostgreSQL, Redis (sesión), las claves de Data Protection (volumen) y los certificados de `certificados/`.
- **frontend:** nginx con los HTML en el puerto **5500**.
- **migraciones:** job que migra la base y registra el cliente OpenIddict antes de levantar el backend.
- **redis** y **postgres**.

Cambiar el número de réplicas sin tocar el archivo:

```bash
docker compose -f contructor.yml up -d --scale backend=5
```

Comprobar el balanceo (la cabecera `X-Servido-Por` cambia de IP):

```bash
for i in 1 2 3 4; do curl -sI http://localhost:5290/openapi/v1.json | grep -i x-servido-por; done
```

Los datos se conservan en el volumen `redsocial-datos-postgres`.

## Consultar la API

Una vez iniciados los servicios, puedes acceder a Scalar desde el navegador:

```text
http://localhost:5290/scalar
```

También puedes consultar la especificación OpenAPI:

```text
http://localhost:5290/openapi/v1.json
```

Si modificas `BACKEND_PORT` en `.env`, utiliza el nuevo puerto en las direcciones anteriores. Para el login con proveedores externos se recomienda mantener `5290`, porque las URLs de callback registradas en GitHub, Dropbox y Figma apuntan a ese puerto.

## Inicio de sesión

La interfaz rediseñada está en:

```text
Tarea4-Red Social grupo777/Frontend/login/login.html
Tarea4-Red Social grupo777/Frontend/login/login.css
Tarea4-Red Social grupo777/Frontend/login/script.js
```

El perfil está en `Tarea4-Red Social grupo777/Frontend/Bienvenida/`.

Puedes abrir el login desde:

- Backend/Nginx: <http://localhost:5290/login.html>
- Contenedor del frontend: <http://localhost:5500/login/login.html>

GitHub, Dropbox, Figma y X (Twitter) ya están configurados en `appsettings.Development.json` (ejecución local) y `appsettings.Production.json` (Docker). Google tiene el **Client ID** configurado, pero el **Client Secret** no se guarda en el repositorio porque GitHub bloquea los secretos publicados.

Las URLs de callback registradas en los portales de los proveedores son:

```text
http://localhost:5290/signin-google
http://localhost:5290/auth/twitter/callback
```

### Credenciales de los proveedores

Todas van en el `.env` (no se guardan en `appsettings*.json`). En Docker, `docker compose ... up -d` vuelve a leer el `.env`.

### TikTok

TikTok no acepta `localhost`. Además de `TIKTOK_CLIENT_KEY` y `TIKTOK_CLIENT_SECRET`, define `TIKTOK_REDIRECT_URI` con una URL **HTTPS pública** que llegue al nginx del 5290 (por ejemplo `ngrok http 5290`) y que sea idéntica a la registrada en TikTok Developers, con barra final: `https://<tu-url>/auth/tiktok/callback/`. Detalle en `Tarea4-Red Social grupo777/docs/TIKTOK_SETUP.md`.

## Arrancar desde la imagen local (Despliegue)

Después de construir la imagen `redsocial`, puedes utilizar `despliegue.yml` para levantar los servicios sin volver a construir el backend.

Ejecuta:

```bash
docker compose -f despliegue.yml up -d
```

Este archivo utiliza las imágenes `redsocial` y `redsocial-frontend` previamente construidas con `contructor.yml` (`BACKEND_IMAGE`, `FRONTEND_IMAGE` y `PULL_POLICY` en el `.env`). Sigue necesitando junto al archivo las carpetas `nginx/` y `certificados/`, pero no el código fuente.

Para comprobar los contenedores:

```bash
docker compose -f despliegue.yml ps
```

## Publicar la imagen en Docker Hub

Si deseas utilizar la imagen en otro equipo, primero debes etiquetarla con tu usuario de Docker Hub:

```bash
docker tag redsocial TU_USUARIO/redsocial:1.0
```

Inicia sesión:

```bash
docker login
```

Publica la imagen:

```bash
docker push TU_USUARIO/redsocial:1.0
```

En el `.env`, utiliza la imagen publicada (publica también `redsocial-frontend` de la misma forma):

```env
BACKEND_IMAGE=TU_USUARIO/redsocial:1.0
FRONTEND_IMAGE=TU_USUARIO/redsocial-frontend:1.0
PULL_POLICY=always
```

De esta manera, Docker podrá descargar las imágenes desde Docker Hub al momento del despliegue.

## Detener los servicios

Para detener y eliminar los contenedores y la red del proyecto, ejecuta:

```bash
docker compose -f contructor.yml down
```

Los datos de PostgreSQL se conservan porque están almacenados en un volumen nombrado.

**Importante:** no utilices `down -v` si deseas conservar la información de la base de datos.

## Archivos y credenciales

El proyecto utiliza los siguientes archivos:

- `contructor.yml`: configuración para construir las imágenes y levantar los servicios.
- `nginx/nginx.conf`: balanceador del backend (puerto 5290).
- `certificados/`: `signing.pfx` y `encryption.pfx` de OpenIddict (no se suben a Git).
- `despliegue.yml`: configuración para ejecutar una imagen previamente construida.
- `.env`: contiene las credenciales y variables de entorno utilizadas por Docker Compose.
- `.env.example`: plantilla de las variables necesarias para ejecutar el proyecto.
- `Dockerfile`: instrucciones para construir la imagen del backend.

El archivo `.env` está excluido de Git y Docker mediante `.gitignore` y `.dockerignore`. Únicamente se comparte `.env.example`, para que otros usuarios puedan configurar sus propias credenciales.

## Verificación

Para comprobar el funcionamiento del proyecto:

1. Construir la imagen del backend.
2. Verificar que los contenedores de la API y PostgreSQL estén activos.
3. Acceder a Scalar y consultar los endpoints.
4. Probar las operaciones CRUD de usuarios.
5. Reiniciar los contenedores y comprobar que los datos continúan almacenados en PostgreSQL.
6. Verificar el despliegue utilizando la imagen previamente construida.

##Tablas

Yeymi Miguel Rivera Castillo
Usuarios
Seguidores
Usuarios/Publicaciones
Usuario/sesion
##Metodos de sesion
Yeymi Miguel Rivera Castillo
Google
Github


