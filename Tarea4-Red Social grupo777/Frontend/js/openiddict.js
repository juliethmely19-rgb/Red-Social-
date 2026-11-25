
const API_URL = (window.APP_CONFIG && window.APP_CONFIG.API_URL) || "http://localhost:5290";

const CLIENT_ID = "redsocial-frontend";

// callback.html está junto a index.html. Se calcula desde la URL actual para que
// funcione con Live Server (127.0.0.1 o localhost) y con el contenedor Docker.
const REDIRECT_URI = new URL("callback.html", window.location.href).href;

function base64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = "";

    bytes.forEach(byte => {
        binary += String.fromCharCode(byte);
    });

    return btoa(binary)
        .replace(/\+/g, "-")
        .replace(/\//g, "_")
        .replace(/=/g, "");
}

async function iniciarOpenIddict() {
    try {
        const bytes = crypto.getRandomValues(new Uint8Array(32));
        const codeVerifier = base64Url(bytes);

        const hash = await crypto.subtle.digest(
            "SHA-256",
            new TextEncoder().encode(codeVerifier)
        );

        const codeChallenge = base64Url(hash);

        const stateBytes = crypto.getRandomValues(new Uint8Array(32));
        const state = base64Url(stateBytes);

        sessionStorage.setItem("oidc_code_verifier", codeVerifier);
        sessionStorage.setItem("oidc_state", state);

        const parametros = new URLSearchParams({
            client_id: CLIENT_ID,
            redirect_uri: REDIRECT_URI,
            response_type: "code",
            scope: "openid profile email",
            // Obliga a mostrar la pantalla de login para ELEGIR el proveedor,
            // aunque exista una sesión anterior (por ejemplo, Google).
            prompt: "login",
            state: state,
            code_challenge: codeChallenge,
            code_challenge_method: "S256"
        });

        window.location.href =
            `${API_URL}/connect/authorize?${parametros.toString()}`;

    } catch (error) {
        console.error("Error al iniciar OpenIddict:", error);
        alert("No se pudo iniciar la autenticación.");
    }
}
