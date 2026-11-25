# TikTok Login Kit — guía de ejecución

Esta guía describe cómo ejecutar el inicio de sesión con TikTok en un entorno local.
El proyecto conserva todos los proveedores existentes (Google, GitHub, Dropbox, Figma,
Discord, Slack, X y TikTok).

## 1. Requisitos

- .NET 10 y PostgreSQL en ejecución.
- Cuenta de ngrok y el agente oficial instalado.
- Una app en TikTok Developers con un Sandbox, por ejemplo `RedSocialJulieth-Pruebas`.
- Login Kit agregado al Sandbox, con plataforma **Web**.
- Tu cuenta TikTok agregada como **target user** y autorizada en el Sandbox.

## 2. Configuración privada

El proyecto ya tiene `UserSecretsId` en `Tarea4.csproj`, así que no es necesario
`dotnet user-secrets init`. Desde el directorio del proyecto:

```bash
cd "Tarea4-Red Social grupo777"

dotnet user-secrets set "TikTok:ClientKey" "<client-key-del-sandbox>"
dotnet user-secrets set "TikTok:ClientSecret" "<client-secret-del-sandbox>"
dotnet user-secrets set "TikTok:RedirectUri" "https://<tu-url-https-ngrok>/auth/tiktok/callback/"
```

También puedes usar variables de entorno en la terminal que inicia la API:

```bash
export TIKTOK_CLIENT_KEY="<client-key-del-sandbox>"
export TIKTOK_CLIENT_SECRET="<client-secret-del-sandbox>"
export TIKTOK_REDIRECT_URI="https://<tu-url-https-ngrok>/auth/tiktok/callback/"
```

Si tu PostgreSQL no usa la cadena del repositorio, configúrala de forma privada:

```bash
dotnet user-secrets set "ConnectionStrings:Context" "<cadena-de-conexion-local>"
# o, como variable de entorno:
export ConnectionStrings__Context="<cadena-de-conexion-local>"
```

No escribas valores reales en `appsettings*.json`, en `.env` ni en el chat.
`.env` está ignorado por Git; `.env.example` contiene solo los nombres de las variables.

## 3. Iniciar la API y ngrok

Terminal 1 — API:

```bash
cd "Tarea4-Red Social grupo777"
dotnet restore
dotnet build
dotnet run --no-launch-profile --urls http://localhost:5294
```

Terminal 2 — ngrok:

```bash
ngrok http 5294 --inspect=false
```

Copia la URL HTTPS que muestra ngrok, por ejemplo
`https://<tu-url-https-ngrok>`. Ella cambia cada vez que se reinicia el túnel
si no contratas un dominio fijo.

## 4. Registrar la configuración en TikTok Developers

En **Sandbox → RedSocialJulieth-Pruebas** completa:

| Campo del portal | Valor |
| --- | --- |
| Categoría | Social |
| Descripción | `Red Social 777: aplicación académica con inicio de sesión externo y publicaciones con categorías, etiquetas e imágenes.` |
| Icono | `docs/assets/red-social-777-icon.png` (PNG 1024 × 1024) |
| Web / Desktop URL | `https://<tu-url-https-ngrok>/` |
| Terms of Service URL | `https://<tu-url-https-ngrok>/legal/tos.html` |
| Privacy Policy URL | `https://<tu-url-https-ngrok>/legal/privacy.html` |
| Login Kit → Web → Redirect URI | `https://<tu-url-https-ngrok>/auth/tiktok/callback/` |

Pulsa **Save** y después **Apply changes**.

La URI configurada en la API (`TikTok:RedirectUri` o `TIKTOK_REDIRECT_URI`) debe ser
**idéntica** a la registrada en el portal, incluida la barra final `/`. El código
normaliza la barra final automáticamente, pero el registro del portal debe contener
la misma cadena. Cada compañero necesita su propia URL HTTPS y debe registrar su
propio callback; no reutilices la URL de otra persona.

## 5. Probar

1. Abre `http://localhost:5294/login.html`.
2. Pulsa **Continuar con TikTok**.
3. Autoriza con tu cuenta de prueba del Sandbox.
4. Comprueba `GET http://localhost:5294/api/Auth/me`; debe devolver
   `autenticado: true` y `proveedor: TikTok`.
5. Cierra sesión desde la interfaz y vuelve a comprobar `/api/Auth/me`.

La documentación interactiva está en `http://localhost:5294/scalar/`.

Si cambia la URL de ngrok, actualiza **tres** sitios antes de volver a probar:
la variable de entorno/User Secret `TikTok:RedirectUri`, el registro en TikTok
Developers y, si aplica, las URL del sitio y las páginas legales.

## 6. Con Docker Compose (nginx en el 5290)

Con `docker compose -f contructor.yml up -d --build` el backend queda detrás de nginx
(puerto **5290**, varias réplicas). El túnel debe apuntar a nginx, no a una réplica:

```bash
ngrok http 5290 --inspect=false
```

Completa en el `.env` de la raíz (no hace falta tocar los ymls):

```env
TIKTOK_CLIENT_KEY=<client-key-del-sandbox>
TIKTOK_CLIENT_SECRET=<client-secret-del-sandbox>
TIKTOK_REDIRECT_URI=https://<tu-url-https-ngrok>/auth/tiktok/callback/
```

Aplica el cambio con `docker compose -f contructor.yml up -d` y registra en TikTok
Developers la misma URL (sitio, Términos `/legal/tos.html`, Privacidad `/legal/privacy.html`
y Redirect URI). El handoff por ticket funciona con varias réplicas porque comparten
Redis y las claves de Data Protection.

Con `dotnet run` el puerto es el 5290 (`launchSettings.json`), así que el túnel también
es `ngrok http 5290`; las mismas variables se leen del `.env`.
