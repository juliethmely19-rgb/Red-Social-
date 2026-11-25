
const API_URL = (window.APP_CONFIG && window.APP_CONFIG.API_URL) || "http://localhost:5290";

const CLIENT_ID = "redsocial-frontend";

// Debe ser idéntico al redirect_uri enviado en openiddict.js.
const REDIRECT_URI = new URL("callback.html", window.location.href).href;

async function procesarCallback() {
    const mensaje = document.getElementById("mensaje");

    try {
        const parametros = new URLSearchParams(window.location.search);

        const code = parametros.get("code");
        const state = parametros.get("state");
        const error = parametros.get("error");

        if (error) {
            throw new Error(
                parametros.get("error_description") || error
            );
        }

        const stateGuardado = sessionStorage.getItem("oidc_state");
        const codeVerifier = sessionStorage.getItem("oidc_code_verifier");

        if (!state || state !== stateGuardado) {
            throw new Error("El parámetro state no coincide.");
        }

        if (!code || !codeVerifier) {
            throw new Error("Falta el código o el verificador PKCE.");
        }

        mensaje.textContent = "Solicitando Access Token...";

        const datos = new URLSearchParams({
            grant_type: "authorization_code",
            client_id: CLIENT_ID,
            code: code,
            redirect_uri: REDIRECT_URI,
            code_verifier: codeVerifier
        });

        const respuesta = await fetch(`${API_URL}/connect/token`, {
            method: "POST",
            headers: {
                "Content-Type": "application/x-www-form-urlencoded"
            },
            body: datos.toString()
        });

        const tokens = await respuesta.json();

        if (!respuesta.ok) {
            throw new Error(
                tokens.error_description || tokens.error ||
                "No se pudo obtener el token."
            );
        }

        if (!tokens.access_token) {
            throw new Error("El servidor no devolvió un Access Token.");
        }

        sessionStorage.setItem("access_token", tokens.access_token);

        if (tokens.id_token) {
            sessionStorage.setItem("id_token", tokens.id_token);
        }

        sessionStorage.removeItem("oidc_state");
        sessionStorage.removeItem("oidc_code_verifier");

        mensaje.textContent = "Autenticación correcta. Redirigiendo...";

        window.location.replace(
            new URL("Bienvenida/bienvenida.html", window.location.href).href
        );

    } catch (error) {
        console.error("Error en OpenIddict:", error);

        mensaje.textContent = error.message;

        sessionStorage.removeItem("oidc_state");
        sessionStorage.removeItem("oidc_code_verifier");
    }
}

procesarCallback();
