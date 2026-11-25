using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly Context _context;
    private readonly IConfiguration _configuration;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IDataProtector _estadoProtector;
    private readonly IDataProtector _ticketProtector;
    private readonly IDistributedCache _cache;

    public AuthController(
        Context context,
        IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        IDataProtectionProvider dataProtection,
        IDistributedCache cache)
    {
        _context = context;
        _configuration = configuration;
        _httpClientFactory = httpClientFactory;
        _estadoProtector = dataProtection.CreateProtector("RedSocial.OAuthState.v1");
        _ticketProtector = dataProtection.CreateProtector("RedSocial.LoginTicket.v1");
        _cache = cache;
    }

    [HttpGet("GoogleLogin")]
    public IActionResult GoogleLogin([FromQuery] string? returnUrl)
    {
        if (!GoogleConfigurado())
        {
            return VolverAlLogin(
                "Google todavía no está configurado. Agrega Google:ClientId y Google:ClientSecret.");
        }

        var propiedades = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(GoogleResponse), "Auth")
        };

        var destino = NormalizarReturnUrl(returnUrl);

        if (destino != null)
        {
            propiedades.Items["returnUrl"] = destino;
        }

        return Challenge(propiedades, GoogleDefaults.AuthenticationScheme);
    }

    [HttpGet("GoogleResponse")]
    public async Task<IActionResult> GoogleResponse()
    {
        var resultado = await HttpContext.AuthenticateAsync("External");

        if (!resultado.Succeeded || resultado.Principal == null)
        {
            return VolverAlLogin("No se pudo completar el inicio de sesión con Google.");
        }

        var identidad = resultado.Principal;

        var googleId = identidad.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var email = identidad.FindFirst(ClaimTypes.Email)?.Value;
        var nombre = identidad.FindFirst(ClaimTypes.Name)?.Value;

        if (string.IsNullOrWhiteSpace(googleId) ||
            string.IsNullOrWhiteSpace(email))
        {
            return VolverAlLogin("Google no proporcionó los datos necesarios.");
        }

        var usuario = await ObtenerOCrearUsuario(
            "Google", googleId, nombre, email);

        var returnUrl =
            resultado.Properties?.Items.TryGetValue("returnUrl", out var destino) == true
                ? NormalizarReturnUrl(destino)
                : null;

        await IniciarSesionCookie(usuario, "Google");
        await HttpContext.SignOutAsync("External");

        return RedirigirDespuesLogin(returnUrl);
    }
    // ================= GITHUB =================
    [HttpGet("GithubLogin")]
    public IActionResult GithubLogin([FromQuery] string? returnUrl)
    {
        var clientId = Setting("GitHub:ClientId", "GITHUB_CLIENT_ID");
        var clientSecret = Setting("GitHub:ClientSecret", "GITHUB_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "GitHub todavía no está configurado. Agrega GitHub:ClientId y GitHub:ClientSecret.");
        }

        var redirectUri = ProviderRedirectUri("GitHub", "/api/auth/callback");
        var state = CrearState("github", returnUrl);

        var url = "https://github.com/login/oauth/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  "&scope=read:user%20user:email" +
                  $"&state={Uri.EscapeDataString(state)}";

        return Redirect(url);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> GithubCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"GitHub rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        if (!ValidarState("github", state))
        {
            return VolverAlLogin(
                "La sesión de autorización de GitHub expiró. Intenta de nuevo.");
        }
        var destino = TomarReturnUrl("github");
        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("GitHub no envió el código de autorización.");
        }

        var clientId = Setting("GitHub:ClientId", "GITHUB_CLIENT_ID");
        var clientSecret = Setting("GitHub:ClientSecret", "GITHUB_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("GitHub todavía no está configurado.");
        }

        var redirectUri = ProviderRedirectUri("GitHub", "/api/auth/callback");
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        var respuestaToken = await client.PostAsync(
            "https://github.com/login/oauth/access_token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            }));

        if (!respuestaToken.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el token de GitHub.");
        }

        using var documentoToken = JsonDocument.Parse(
            await respuestaToken.Content.ReadAsStringAsync());

        if (!documentoToken.RootElement.TryGetProperty("access_token", out var tokenProp))
        {
            var detalle = documentoToken.RootElement.TryGetProperty("error_description", out var d)
                ? d.GetString()
                : "GitHub no proporcionó un token.";

            return VolverAlLogin(detalle ?? "GitHub no proporcionó un token.");
        }

        var token = tokenProp.GetString();

        if (string.IsNullOrWhiteSpace(token))
        {
            return VolverAlLogin("GitHub no proporcionó un token.");
        }

        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RedSocialApp");
        client.DefaultRequestHeaders.Accept.Clear();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

        var respuestaUsuario = await client.GetAsync("https://api.github.com/user");

        if (!respuestaUsuario.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de GitHub.");
        }

        using var documento = JsonDocument.Parse(
            await respuestaUsuario.Content.ReadAsStringAsync());

        var root = documento.RootElement;

        if (!root.TryGetProperty("id", out var idProp))
        {
            return VolverAlLogin("GitHub no envió el identificador del usuario.");
        }

        var githubId = idProp.GetInt64().ToString();
        var username = root.TryGetProperty("login", out var loginProp)
            ? loginProp.GetString()
            : null;
        var nombre = root.TryGetProperty("name", out var nombreProp)
            ? nombreProp.GetString()
            : null;

        var email = root.TryGetProperty("email", out var emailProp)
            ? emailProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(email))
        {
            email = await ObtenerEmailGitHub(client);
        }

        var usuario = await ObtenerOCrearUsuario(
            "GitHub",
            githubId,
            nombre ?? username,
            email);

        return await FinalizarLogin(usuario, "GitHub", destino);
    }

    // ================= DROPBOX =================
    [HttpGet("dropbox/login")]
    public IActionResult DropboxLogin([FromQuery] string? returnUrl)
    {
        var clientId = Setting("Dropbox:ClientId", "DROPBOX_CLIENT_ID");
        var clientSecret = Setting("Dropbox:ClientSecret", "DROPBOX_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "Dropbox todavía no está configurado. Agrega Dropbox:ClientId y Dropbox:ClientSecret.");
        }

        var redirectUri = ProviderRedirectUri("Dropbox", "/auth/dropbox/callback");
        var state = CrearState("dropbox", returnUrl);

        var url = "https://www.dropbox.com/oauth2/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  "&response_type=code" +
                  "&token_access_type=online" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&state={Uri.EscapeDataString(state)}";

        return Redirect(url);
    }

    [HttpGet("dropbox/callback")]
    public Task<IActionResult> DropboxCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarDropbox(code, state, error, errorDescription);
    }

    // Ruta registrada en la app de Dropbox: /auth/dropbox/callback
    [HttpGet("/auth/dropbox/callback")]
    public Task<IActionResult> DropboxCallbackLegacy(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarDropbox(code, state, error, errorDescription);
    }

    private async Task<IActionResult> ProcesarDropbox(
        string? code,
        string? state,
        string? error,
        string? errorDescription)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"Dropbox rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        if (!ValidarState("dropbox", state))
        {
            return VolverAlLogin(
                "La sesión de autorización de Dropbox expiró. Intenta de nuevo.");
        }
        var destino = TomarReturnUrl("dropbox");
        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("Dropbox no envió el código de autorización.");
        }

        var clientId = Setting("Dropbox:ClientId", "DROPBOX_CLIENT_ID");
        var clientSecret = Setting("Dropbox:ClientSecret", "DROPBOX_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("Dropbox todavía no está configurado.");
        }

        var redirectUri = ProviderRedirectUri("Dropbox", "/auth/dropbox/callback");
        var client = _httpClientFactory.CreateClient();

        var tokenRes = await client.PostAsync(
            "https://api.dropboxapi.com/oauth2/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = redirectUri
            }));

        if (!tokenRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el token de Dropbox.");
        }

        using var tokenDoc = JsonDocument.Parse(
            await tokenRes.Content.ReadAsStringAsync());

        var accessToken = tokenDoc.RootElement
            .TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return VolverAlLogin("Dropbox no proporcionó un token.");
        }

        var userReq = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.dropboxapi.com/2/users/get_current_account");
        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userRes = await client.SendAsync(userReq);

        if (!userRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de Dropbox.");
        }

        using var userDoc = JsonDocument.Parse(
            await userRes.Content.ReadAsStringAsync());

        var id = userDoc.RootElement
            .TryGetProperty("account_id", out var idProp)
            ? idProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(id))
        {
            return VolverAlLogin("Dropbox no envió el identificador del usuario.");
        }

        var name = userDoc.RootElement.TryGetProperty("name", out var nameProp) &&
                   nameProp.TryGetProperty("display_name", out var displayProp)
            ? displayProp.GetString()
            : null;

        var email = userDoc.RootElement.TryGetProperty("email", out var emailProp)
            ? emailProp.GetString()
            : null;

        var usuario = await ObtenerOCrearUsuario("Dropbox", id, name, email);
        return await FinalizarLogin(usuario, "Dropbox", destino);
    }

    // ================= FIGMA =================
    [HttpGet("figma/login")]
    public IActionResult FigmaLogin([FromQuery] string? returnUrl)
    {
        var clientId = Setting("Figma:ClientId", "FIGMA_CLIENT_ID");
        var clientSecret = Setting("Figma:ClientSecret", "FIGMA_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "Figma todavía no está configurado. Agrega Figma:ClientId y Figma:ClientSecret.");
        }

        var redirectUri = ProviderRedirectUri("Figma", "/auth/figma/callback");
        var state = CrearState("figma", returnUrl);

        var url = "https://www.figma.com/oauth" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  "&scope=current_user:read" +
                  $"&state={Uri.EscapeDataString(state)}" +
                  "&response_type=code";

        return Redirect(url);
    }

    [HttpGet("figma/callback")]
    public Task<IActionResult> FigmaCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarFigma(code, state, error, errorDescription);
    }

    // Ruta registrada en la app de Figma: /auth/figma/callback
    [HttpGet("/auth/figma/callback")]
    public Task<IActionResult> FigmaCallbackLegacy(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarFigma(code, state, error, errorDescription);
    }

    private async Task<IActionResult> ProcesarFigma(
        string? code,
        string? state,
        string? error,
        string? errorDescription)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"Figma rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        if (!ValidarState("figma", state))
        {
            return VolverAlLogin(
                "La sesión de autorización de Figma expiró. Intenta de nuevo.");
        }
        var destino = TomarReturnUrl("figma");
        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("Figma no envió el código de autorización.");
        }

        var clientId = Setting("Figma:ClientId", "FIGMA_CLIENT_ID");
        var clientSecret = Setting("Figma:ClientSecret", "FIGMA_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("Figma todavía no está configurado.");
        }

        var redirectUri = ProviderRedirectUri("Figma", "/auth/figma/callback");
        var client = _httpClientFactory.CreateClient();

        var tokenReq = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.figma.com/v1/oauth/token");

        var basicAuth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["redirect_uri"] = redirectUri,
            ["code"] = code,
            ["grant_type"] = "authorization_code"
        });

        var tokenRes = await client.SendAsync(tokenReq);

        if (!tokenRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el token de Figma.");
        }

        using var tokenDoc = JsonDocument.Parse(
            await tokenRes.Content.ReadAsStringAsync());

        var accessToken = tokenDoc.RootElement
            .TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return VolverAlLogin("Figma no proporcionó un token.");
        }

        var userReq = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.figma.com/v1/me");
        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userRes = await client.SendAsync(userReq);

        if (!userRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de Figma.");
        }

        using var userDoc = JsonDocument.Parse(
            await userRes.Content.ReadAsStringAsync());

        var id = userDoc.RootElement.TryGetProperty("id", out var idProp)
            ? (idProp.ValueKind == JsonValueKind.String ? idProp.GetString() : idProp.ToString())
            : null;

        if (string.IsNullOrWhiteSpace(id))
        {
            return VolverAlLogin("Figma no envió el identificador del usuario.");
        }

        var name = userDoc.RootElement.TryGetProperty("handle", out var handleProp) &&
                   handleProp.ValueKind == JsonValueKind.String
            ? handleProp.GetString()
            : null;

        var email = userDoc.RootElement.TryGetProperty("email", out var emailProp) &&
                    emailProp.ValueKind == JsonValueKind.String
            ? emailProp.GetString()
            : null;

        var usuario = await ObtenerOCrearUsuario("Figma", id, name, email);
        return await FinalizarLogin(usuario, "Figma", destino);
    }

    // ================= X (TWITTER) =================
    [HttpGet("twitter/login")]
    public IActionResult TwitterLogin([FromQuery] string? returnUrl)
    {
        var clientId = Setting("Twitter:ClientId", "TWITTER_CLIENT_ID");
        var clientSecret = Setting("Twitter:ClientSecret", "TWITTER_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "X (Twitter) todavía no está configurado. Agrega Twitter:ClientId y Twitter:ClientSecret.");
        }

        var redirectUri = ProviderRedirectUri("Twitter", "/auth/twitter/callback");
        var state = CrearState("twitter", returnUrl);

        // X exige PKCE (S256) para OAuth 2.0. El verifier se guarda en sesión y
        // se vuelve a enviar al canjear el código por el token.
        var codeVerifier = CrearCodeVerifier();
        HttpContext.Session.SetString("oauth_pkce_twitter", codeVerifier);
        var codeChallenge = CrearCodeChallenge(codeVerifier);

        var url = "https://twitter.com/i/oauth2/authorize" +
                  "?response_type=code" +
                  $"&client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  "&scope=users.read%20tweet.read" +
                  $"&state={Uri.EscapeDataString(state)}" +
                  $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
                  "&code_challenge_method=S256";

        return Redirect(url);
    }

    [HttpGet("twitter/callback")]
    public Task<IActionResult> TwitterCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarTwitter(code, state, error, errorDescription);
    }

    // Ruta que se registra en el portal de X: /auth/twitter/callback
    [HttpGet("/auth/twitter/callback")]
    public Task<IActionResult> TwitterCallbackLegacy(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarTwitter(code, state, error, errorDescription);
    }

    private async Task<IActionResult> ProcesarTwitter(
        string? code,
        string? state,
        string? error,
        string? errorDescription)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"X (Twitter) rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        if (!ValidarState("twitter", state))
        {
            return VolverAlLogin(
                "La sesión de autorización de X (Twitter) expiró. Intenta de nuevo.");
        }
        var destino = TomarReturnUrl("twitter");
        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("X (Twitter) no envió el código de autorización.");
        }

        var codeVerifier = HttpContext.Session.GetString("oauth_pkce_twitter");
        HttpContext.Session.Remove("oauth_pkce_twitter");

        if (string.IsNullOrWhiteSpace(codeVerifier))
        {
            return VolverAlLogin(
                "La sesión de autorización de X (Twitter) expiró. Intenta de nuevo.");
        }

        var clientId = Setting("Twitter:ClientId", "TWITTER_CLIENT_ID");
        var clientSecret = Setting("Twitter:ClientSecret", "TWITTER_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("X (Twitter) todavía no está configurado.");
        }

        var redirectUri = ProviderRedirectUri("Twitter", "/auth/twitter/callback");
        var client = _httpClientFactory.CreateClient();

        var tokenReq = new HttpRequestMessage(
            HttpMethod.Post,
            "https://api.twitter.com/2/oauth2/token");

        var basicAuth = Convert.ToBase64String(
            Encoding.UTF8.GetBytes($"{clientId}:{clientSecret}"));

        tokenReq.Headers.Authorization = new AuthenticationHeaderValue("Basic", basicAuth);
        tokenReq.Content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri,
            ["code_verifier"] = codeVerifier
        });

        var tokenRes = await client.SendAsync(tokenReq);

        if (!tokenRes.IsSuccessStatusCode)
        {
            return VolverAlLogin(
                "No se pudo obtener el token de X (Twitter). " + await DetalleError(tokenRes));
        }

        using var tokenDoc = JsonDocument.Parse(
            await tokenRes.Content.ReadAsStringAsync());

        var accessToken = tokenDoc.RootElement
            .TryGetProperty("access_token", out var tokenProp)
            ? tokenProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return VolverAlLogin("X (Twitter) no proporcionó un token.");
        }

        var userReq = new HttpRequestMessage(
            HttpMethod.Get,
            "https://api.twitter.com/2/users/me");

        userReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        var userRes = await client.SendAsync(userReq);

        if (!userRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de X (Twitter).");
        }

        using var userDoc = JsonDocument.Parse(
            await userRes.Content.ReadAsStringAsync());

        if (!userDoc.RootElement.TryGetProperty("data", out var data) ||
            data.ValueKind != JsonValueKind.Object)
        {
            return VolverAlLogin("X (Twitter) no envió los datos del usuario.");
        }

        var id = data.TryGetProperty("id", out var idProp) &&
                 idProp.ValueKind == JsonValueKind.String
            ? idProp.GetString()
            : null;

        if (string.IsNullOrWhiteSpace(id))
        {
            return VolverAlLogin("X (Twitter) no envió el identificador del usuario.");
        }

        var name = data.TryGetProperty("name", out var nameProp) &&
                   nameProp.ValueKind == JsonValueKind.String
            ? nameProp.GetString()
            : null;

        var username = data.TryGetProperty("username", out var usernameProp) &&
                       usernameProp.ValueKind == JsonValueKind.String
            ? usernameProp.GetString()
            : null;

        var usuario = await ObtenerOCrearUsuario(
            "X",
            id,
            name ?? username,
            null);

        return await FinalizarLogin(usuario, "X", destino);
    }

    // ================= TIKTOK =================
    [HttpGet("tiktok/login")]
    public IActionResult TikTokLogin([FromQuery] string? returnUrl)
    {
        var clientKey = Setting("TikTok:ClientKey", "TIKTOK_CLIENT_KEY");
        var clientSecret = Setting("TikTok:ClientSecret", "TIKTOK_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientKey) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "TikTok todavía no está configurado. Solicita las credenciales del Sandbox a Gerson.");
        }

        var redirectUri = NormalizarCallbackTikTok(
            Setting("TikTok:RedirectUri", "TIKTOK_REDIRECT_URI"));

        if (string.IsNullOrWhiteSpace(redirectUri) ||
            !redirectUri.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return VolverAlLogin(
                "TikTok requiere un callback HTTPS público. Configura TIKTOK_REDIRECT_URI.");
        }

        var state = CrearStateFirmado(returnUrl);

        var url = "https://www.tiktok.com/v2/auth/authorize/" +
                  $"?client_key={Uri.EscapeDataString(clientKey)}" +
                  "&response_type=code" +
                  "&scope=user.info.basic" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  $"&state={Uri.EscapeDataString(state)}";

        return Redirect(url);
    }

    [HttpGet("tiktok/callback")]
    public Task<IActionResult> TikTokCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? scopes,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery] string? ticket)
    {
        return ProcesarTikTok(code, state, error, errorDescription, ticket);
    }

    // Ruta pública que se registra en TikTok Developers (Login Kit, Web).
    [HttpGet("/auth/tiktok/callback")]
    public Task<IActionResult> TikTokCallbackPublic(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? scopes,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription,
        [FromQuery] string? ticket)
    {
        return ProcesarTikTok(code, state, error, errorDescription, ticket);
    }

    private async Task<IActionResult> ProcesarTikTok(
        string? code,
        string? state,
        string? error,
        string? errorDescription,
        string? ticket)
    {
        // Segunda vuelta: el callback llegó por el túnel HTTPS (ngrok) y nos reenvió
        // aquí con un ticket firmado para crear la cookie en el host del backend.
        if (!string.IsNullOrWhiteSpace(ticket))
        {
            return await CanjearTicketTikTok(ticket);
        }

        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"TikTok rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        // El state de TikTok va firmado: la cookie de sesión vive en localhost y el
        // callback llega por el dominio HTTPS público, donde esa cookie no existe.
        if (!LeerStateFirmado(state, out var destino))
        {
            return VolverAlLogin(
                "La sesión de autorización de TikTok expiró. Intenta de nuevo.");
        }
        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("TikTok no envió el código de autorización.");
        }

        var clientKey = Setting("TikTok:ClientKey", "TIKTOK_CLIENT_KEY");
        var clientSecret = Setting("TikTok:ClientSecret", "TIKTOK_CLIENT_SECRET");
        var redirectUri = NormalizarCallbackTikTok(
            Setting("TikTok:RedirectUri", "TIKTOK_REDIRECT_URI"));

        if (string.IsNullOrWhiteSpace(clientKey) ||
            string.IsNullOrWhiteSpace(clientSecret) ||
            string.IsNullOrWhiteSpace(redirectUri))
        {
            return VolverAlLogin("TikTok todavía no está configurado.");
        }

        var client = _httpClientFactory.CreateClient();

        var tokenRes = await client.PostAsync(
            "https://open.tiktokapis.com/v2/oauth/token/",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_key"] = clientKey,
                ["client_secret"] = clientSecret,
                ["code"] = code,
                ["grant_type"] = "authorization_code",
                ["redirect_uri"] = redirectUri
            }));

        var tokenCuerpo = await tokenRes.Content.ReadAsStringAsync();

        static string? TextoTikTok(JsonElement raiz, string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) &&
            valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;

        static string DetalleErrorTikTok(string cuerpo)
        {
            try
            {
                using var doc = JsonDocument.Parse(cuerpo);
                var partes = new List<string>();

                foreach (var nombre in new[] { "error", "error_description", "log_id" })
                {
                    var valor = TextoTikTok(doc.RootElement, nombre);
                    if (!string.IsNullOrWhiteSpace(valor))
                        partes.Add(valor);
                }

                return string.Join(" - ", partes);
            }
            catch
            {
                return string.Empty;
            }
        }

        if (!tokenRes.IsSuccessStatusCode)
        {
            var detalle = DetalleErrorTikTok(tokenCuerpo);
            if (detalle.Length > 300)
                detalle = detalle[..300];

            return VolverAlLogin(string.IsNullOrWhiteSpace(detalle)
                ? "No se pudo obtener el token de TikTok."
                : $"No se pudo obtener el token de TikTok: {detalle}");
        }

        using var tokenDoc = JsonDocument.Parse(tokenCuerpo);

        var accessToken = TextoTikTok(tokenDoc.RootElement, "access_token");
        var openId = TextoTikTok(tokenDoc.RootElement, "open_id");

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            var detalle = DetalleErrorTikTok(tokenCuerpo);
            if (detalle.Length > 300)
                detalle = detalle[..300];

            return VolverAlLogin(string.IsNullOrWhiteSpace(detalle)
                ? "TikTok no proporcionó un token de acceso."
                : $"TikTok no proporcionó un token de acceso: {detalle}");
        }

        if (string.IsNullOrWhiteSpace(openId))
        {
            return VolverAlLogin("TikTok no proporcionó el identificador del usuario.");
        }

        var userReq = new HttpRequestMessage(
            HttpMethod.Get,
            "https://open.tiktokapis.com/v2/user/info/?fields=open_id,display_name");

        userReq.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var userRes = await client.SendAsync(userReq);

        if (!userRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de TikTok.");
        }

        using var userDoc = JsonDocument.Parse(
            await userRes.Content.ReadAsStringAsync());

        string? displayName = null;

        if (userDoc.RootElement.TryGetProperty("data", out var data) &&
            data.ValueKind == JsonValueKind.Object &&
            data.TryGetProperty("user", out var user) &&
            user.ValueKind == JsonValueKind.Object &&
            user.TryGetProperty("display_name", out var nombreProp) &&
            nombreProp.ValueKind == JsonValueKind.String)
        {
            displayName = nombreProp.GetString();
        }

        // TikTok con user.info.basic no entrega correo: se pasa null a propósito.
        var usuario = await ObtenerOCrearUsuario(
            "TikTok",
            openId,
            displayName ?? openId,
            null);

        await IniciarSesionCookie(usuario, "TikTok");
        return IrAlPerfil();
    }

    // ================= DISCORD =================
    [HttpGet("discord/login")]
    public IActionResult DiscordLogin()
    {
        var clientId = Setting("Discord:ClientId", "DISCORD_CLIENT_ID");
        var clientSecret = Setting("Discord:ClientSecret", "DISCORD_CLIENT_SECRET");

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "Discord todavía no está configurado. Configura Discord:ClientId y Discord:ClientSecret.");
        }

        var redirectUri = ProviderRedirectUri("Discord", "/api/Auth/discord/callback");
        var state = CrearState("discord", null);

        var url = "https://discord.com/oauth2/authorize" +
                  $"?client_id={Uri.EscapeDataString(clientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  "&response_type=code" +
                  "&scope=identify%20email" +
                  $"&state={Uri.EscapeDataString(state)}";

        return Redirect(url);
    }

    [HttpGet("discord/callback")]
    [HttpGet("/auth/discord/callback")]
    public Task<IActionResult> DiscordCallback(
        [FromQuery] string? code,
        [FromQuery] string? state,
        [FromQuery] string? error,
        [FromQuery(Name = "error_description")] string? errorDescription)
    {
        return ProcesarDiscord(code, state, error, errorDescription);
    }

    private async Task<IActionResult> ProcesarDiscord(
        string? code,
        string? state,
        string? error,
        string? errorDescription)
    {
        if (!string.IsNullOrWhiteSpace(error))
        {
            return VolverAlLogin(
                $"Discord rechazó el inicio de sesión: {errorDescription ?? error}");
        }

        if (!ValidarState("discord", state))
        {
            return VolverAlLogin(
                "La sesión de autorización de Discord expiró. Intenta de nuevo.");
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            return VolverAlLogin("Discord no envió el código de autorización.");
        }

        var clientId = Setting("Discord:ClientId", "DISCORD_CLIENT_ID");
        var clientSecret = Setting("Discord:ClientSecret", "DISCORD_CLIENT_SECRET");
        var redirectUri = ProviderRedirectUri("Discord", "/api/Auth/discord/callback");

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("Discord todavía no está configurado.");
        }

        var client = _httpClientFactory.CreateClient();

        var tokenRequest = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri
        });

        var tokenRes = await client.PostAsync(
            "https://discord.com/api/oauth2/token",
            tokenRequest);

        if (!tokenRes.IsSuccessStatusCode)
        {
            var detalle = $"HTTP {(int)tokenRes.StatusCode}";
            var cuerpo = await tokenRes.Content.ReadAsStringAsync();

            try
            {
                using var docError = JsonDocument.Parse(cuerpo);
                var raiz = docError.RootElement;
                var partes = new List<string>();

                if (raiz.TryGetProperty("error", out var errorJson))
                    partes.Add(errorJson.ToString());
                if (raiz.TryGetProperty("error_description", out var descJson))
                    partes.Add(descJson.ToString());
                if (raiz.TryGetProperty("message", out var msgJson))
                    partes.Add(msgJson.ToString());

                detalle += partes.Count > 0
                    ? " - " + string.Join(" - ", partes)
                    : " - respuesta sin detalle";
            }
            catch
            {
                detalle += " - respuesta no JSON";
            }

            if (detalle.Length > 300)
                detalle = detalle[..300];

            return VolverAlLogin(
                $"No se pudo obtener el token de Discord: {detalle}");
        }

        using var tokenDoc = JsonDocument.Parse(
            await tokenRes.Content.ReadAsStringAsync());

        var accessToken =
            tokenDoc.RootElement.TryGetProperty("access_token", out var tokenProp) &&
            tokenProp.ValueKind == JsonValueKind.String
                ? tokenProp.GetString()
                : null;

        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return VolverAlLogin("Discord no proporcionó un token de acceso.");
        }

        var userReq = new HttpRequestMessage(
            HttpMethod.Get,
            "https://discord.com/api/users/@me");
        userReq.Headers.Authorization =
            new AuthenticationHeaderValue("Bearer", accessToken);

        var userRes = await client.SendAsync(userReq);
        if (!userRes.IsSuccessStatusCode)
        {
            return VolverAlLogin("No se pudo obtener el perfil de Discord.");
        }

        using var userDoc = JsonDocument.Parse(
            await userRes.Content.ReadAsStringAsync());

        var id = userDoc.RootElement.TryGetProperty("id", out var idProp)
            ? idProp.ToString()
            : "";

        if (string.IsNullOrWhiteSpace(id))
        {
            return VolverAlLogin("Discord no proporcionó el identificador del usuario.");
        }

        var nombre = "UsuarioDiscord";
        if (userDoc.RootElement.TryGetProperty("global_name", out var globalName) &&
            globalName.ValueKind == JsonValueKind.String &&
            !string.IsNullOrWhiteSpace(globalName.GetString()))
        {
            nombre = globalName.GetString()!;
        }
        else if (userDoc.RootElement.TryGetProperty("username", out var username) &&
                 username.ValueKind == JsonValueKind.String &&
                 !string.IsNullOrWhiteSpace(username.GetString()))
        {
            nombre = username.GetString()!;
        }

        var email = userDoc.RootElement.TryGetProperty("email", out var emailProp) &&
                    emailProp.ValueKind == JsonValueKind.String
            ? emailProp.GetString() ?? ""
            : "";

        var usuario = await ObtenerOCrearUsuario("Discord", id, nombre, email);
        await IniciarSesionCookie(usuario, "Discord");
        return IrAlPerfil();
    }

    // ================= SLACK (OPENID CONNECT) =================
    private const string SlackAuthStateCookie = "SlackAuthState";
    private const string SlackAuthorizationEndpoint = "https://slack.com/openid/connect/authorize";
    private const string SlackTokenEndpoint = "https://slack.com/api/openid.connect.token";
    private const string SlackUserInfoEndpoint = "https://slack.com/api/openid.connect.userInfo";
    private const string SlackKeysEndpoint = "https://slack.com/openid/connect/keys";
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, long> SlackStatesEnUso = new();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, (string State, string Code, string Error, long Expira)> SlackContinuaciones = new();

    private string SlackRedirectUri()
    {
        var configurada = Setting("Slack:RedirectUri", "SLACK_REDIRECT_URI");

        return !string.IsNullOrWhiteSpace(configurada)
            ? configurada
            : ProviderRedirectUri("Slack", "/api/Auth/slack/callback");
    }

    private static byte[] FirmaStateSlack(string clientSecret, string payload) =>
        HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(clientSecret),
            Encoding.UTF8.GetBytes(payload));

    private static string HuellaStateSlack(string state) =>
        WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(state)));

    private static void LimpiarStatesSlackExpirados()
    {
        var ahora = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        foreach (var entrada in SlackStatesEnUso)
        {
            if (entrada.Value < ahora)
                SlackStatesEnUso.TryRemove(entrada.Key, out _);
        }

        foreach (var entrada in SlackContinuaciones)
        {
            if (entrada.Value.Expira < ahora)
                SlackContinuaciones.TryRemove(entrada.Key, out _);
        }
    }

    [HttpGet("slack/login")]
    public IActionResult SlackLogin()
    {
        var clientId = Setting("Slack:ClientId", "SLACK_CLIENT_ID");
        var clientSecret = Setting("Slack:ClientSecret", "SLACK_CLIENT_SECRET");
        var redirectUri = SlackRedirectUri();

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin(
                "Slack todavía no está configurado. Configura Slack:ClientId y Slack:ClientSecret.");
        }

        // State + nonce + token del navegador + expiración + redirect_uri, firmados con HMAC.
        var stateAleatorio = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var nonce = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var tokenNavegador = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var expiraUnix = DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds();
        var expira = expiraUnix.ToString(CultureInfo.InvariantCulture);
        var payload = string.Join("|", stateAleatorio, nonce, tokenNavegador, expira, redirectUri);
        var stateFirmado =
            WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(payload)) +
            "." + WebEncoders.Base64UrlEncode(FirmaStateSlack(clientSecret, payload));

        LimpiarStatesSlackExpirados();
        SlackStatesEnUso[HuellaStateSlack(stateFirmado)] = expiraUnix;

        Response.Cookies.Append(SlackAuthStateCookie, tokenNavegador, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/Auth/slack",
            MaxAge = TimeSpan.FromMinutes(10)
        });

        var url = QueryHelpers.AddQueryString(SlackAuthorizationEndpoint, "response_type", "code");
        url = QueryHelpers.AddQueryString(url, "scope", "openid profile email");
        url = QueryHelpers.AddQueryString(url, "response_mode", "query");
        url = QueryHelpers.AddQueryString(url, "client_id", clientId);
        url = QueryHelpers.AddQueryString(url, "state", stateFirmado);
        url = QueryHelpers.AddQueryString(url, "nonce", nonce);
        url = QueryHelpers.AddQueryString(url, "redirect_uri", redirectUri);

        return Redirect(url);
    }

    [HttpGet("slack/callback")]
    [HttpPost("slack/callback")]
    public async Task<IActionResult> SlackCallback()
    {
        static JsonDocument? ParsearJsonSlack(string cuerpo)
        {
            try { return JsonDocument.Parse(cuerpo); }
            catch { return null; }
        }

        static string? TextoSlack(JsonElement raiz, string nombre) =>
            raiz.TryGetProperty(nombre, out var valor) && valor.ValueKind == JsonValueKind.String
                ? valor.GetString()
                : null;

        static bool RespuestaOkSlack(JsonDocument documento) =>
            documento.RootElement.TryGetProperty("ok", out var ok) &&
            ok.ValueKind == JsonValueKind.True;

        var clientId = Setting("Slack:ClientId", "SLACK_CLIENT_ID");
        var clientSecret = Setting("Slack:ClientSecret", "SLACK_CLIENT_SECRET");
        var redirectUri = SlackRedirectUri();

        if (string.IsNullOrWhiteSpace(clientId) ||
            string.IsNullOrWhiteSpace(clientSecret))
        {
            return VolverAlLogin("Slack todavía no está configurado.");
        }

        static string ValorSlack(HttpRequest solicitud, string nombre) =>
            solicitud.HasFormContentType
                ? solicitud.Form[nombre].ToString()
                : solicitud.Query[nombre].ToString();

        var state = ValorSlack(Request, "state");
        var code = ValorSlack(Request, "code");
        var error = ValorSlack(Request, "error");
        var cookieToken = Request.Cookies[SlackAuthStateCookie];

        if (!string.IsNullOrWhiteSpace(error))
        {
            BorrarStateSlack();
            return VolverAlLogin(
                $"Slack rechazó el inicio de sesión: {error}");
        }

        // Un POST cross-site (form_post) no adjunta la cookie SameSite=Lax.
        // Se vuelve al mismo callback por GET, donde el navegador sí la envía.
        LimpiarStatesSlackExpirados();
        if (Request.Method == "POST" && string.IsNullOrWhiteSpace(cookieToken))
        {
            if (string.IsNullOrWhiteSpace(state) ||
                !SlackStatesEnUso.ContainsKey(HuellaStateSlack(state)))
            {
                BorrarStateSlack();
                return Unauthorized(new { mensaje = "Solicitud de Slack no válida." });
            }

            var continuacion = WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
            SlackContinuaciones[continuacion] = (
                state,
                code,
                error,
                DateTimeOffset.UtcNow.AddMinutes(1).ToUnixTimeSeconds());

            return RedirectToAction(nameof(SlackCallback), new { continuacion });
        }

        var continuacionRecibida = Request.Query["continuacion"].ToString();
        if (!string.IsNullOrWhiteSpace(continuacionRecibida))
        {
            if (!SlackContinuaciones.TryRemove(continuacionRecibida, out var pendiente) ||
                pendiente.Expira < DateTimeOffset.UtcNow.ToUnixTimeSeconds())
            {
                BorrarStateSlack();
                return Unauthorized(new { mensaje = "Solicitud de Slack no válida." });
            }

            state = pendiente.State;
            code = pendiente.Code;
            error = pendiente.Error;
        }

        if (string.IsNullOrWhiteSpace(state) ||
            string.IsNullOrWhiteSpace(code) ||
            string.IsNullOrWhiteSpace(cookieToken))
        {
            BorrarStateSlack();
            return Unauthorized(new { mensaje = "Solicitud de Slack no válida." });
        }

        var partes = state.Split('.');
        if (partes.Length != 2)
        {
            BorrarStateSlack();
            return Unauthorized(new { mensaje = "Estado de Slack no válido." });
        }

        string payload;
        byte[] firmaRecibida;
        try
        {
            payload = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(partes[0]));
            firmaRecibida = WebEncoders.Base64UrlDecode(partes[1]);
        }
        catch
        {
            BorrarStateSlack();
            return Unauthorized(new { mensaje = "Estado de Slack no válido." });
        }

        var campos = payload.Split('|', 5);
        var firmaEsperada = FirmaStateSlack(clientSecret, payload);
        if (campos.Length != 5 ||
            !long.TryParse(campos[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var expira) ||
            expira < DateTimeOffset.UtcNow.ToUnixTimeSeconds() ||
            !string.Equals(campos[4], redirectUri, StringComparison.Ordinal) ||
            !CryptographicOperations.FixedTimeEquals(firmaEsperada, firmaRecibida) ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(campos[2]),
                Encoding.UTF8.GetBytes(cookieToken)))
        {
            BorrarStateSlack();
            return Unauthorized(new { mensaje = "Estado de Slack no válido." });
        }

        var nonceEsperado = campos[1];

        LimpiarStatesSlackExpirados();
        if (!SlackStatesEnUso.TryRemove(HuellaStateSlack(state), out _))
        {
            BorrarStateSlack();
            return Unauthorized(new { mensaje = "Estado de Slack no válido." });
        }

        // El state se consume una sola vez, incluso si fallan las llamadas posteriores.
        BorrarStateSlack();

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));

        using var contenidoToken = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = clientId,
            ["client_secret"] = clientSecret,
            ["code"] = code,
            ["grant_type"] = "authorization_code",
            ["redirect_uri"] = redirectUri
        });

        using var respuestaToken = await client.PostAsync(SlackTokenEndpoint, contenidoToken);
        if (!respuestaToken.IsSuccessStatusCode)
        {
            return StatusCode(502, new { mensaje = "No se pudo completar la autenticación con Slack." });
        }

        using var tokenDoc = ParsearJsonSlack(await respuestaToken.Content.ReadAsStringAsync());
        if (tokenDoc is null || !RespuestaOkSlack(tokenDoc))
        {
            return StatusCode(502, new { mensaje = "No se pudo completar la autenticación con Slack." });
        }

        var accessToken = TextoSlack(tokenDoc.RootElement, "access_token");
        var idToken = TextoSlack(tokenDoc.RootElement, "id_token");

        if (string.IsNullOrWhiteSpace(accessToken) || string.IsNullOrWhiteSpace(idToken))
        {
            return StatusCode(502, new { mensaje = "No se pudo completar la autenticación con Slack." });
        }

        string? idTokenSub = null;
        string? idTokenNombre = null;
        string? idTokenEmail = null;

        try
        {
            using var respuestaLlaves = await client.GetAsync(SlackKeysEndpoint);
            if (!respuestaLlaves.IsSuccessStatusCode)
            {
                return StatusCode(502, new { mensaje = "No se pudo validar la identidad de Slack." });
            }

            using var llavesDoc = ParsearJsonSlack(await respuestaLlaves.Content.ReadAsStringAsync());
            if (llavesDoc is null)
            {
                return StatusCode(502, new { mensaje = "No se pudo validar la identidad de Slack." });
            }

            var parametrosValidacion = new Microsoft.IdentityModel.Tokens.TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = "https://slack.com",
                ValidateAudience = true,
                ValidAudience = clientId,
                ValidateLifetime = true,
                RequireExpirationTime = true,
                RequireSignedTokens = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKeys =
                    new Microsoft.IdentityModel.Tokens.JsonWebKeySet(llavesDoc.RootElement.GetRawText())
                        .GetSigningKeys(),
                ValidAlgorithms = new[] { "RS256" },
                ClockSkew = TimeSpan.FromMinutes(2)
            };

            var validador = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler
            {
                MapInboundClaims = false
            };
            var principal = validador.ValidateToken(idToken, parametrosValidacion, out _);

            var nonceRecibido = principal.FindFirst("nonce")?.Value;
            if (string.IsNullOrWhiteSpace(nonceRecibido) ||
                !CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(nonceEsperado),
                    Encoding.UTF8.GetBytes(nonceRecibido)))
            {
                return Unauthorized(new { mensaje = "Identidad de Slack no válida." });
            }

            idTokenSub = principal.FindFirst("sub")?.Value;
            idTokenNombre = principal.FindFirst("name")?.Value;
            idTokenEmail = principal.FindFirst("email")?.Value;
        }
        catch (Exception)
        {
            return Unauthorized(new { mensaje = "Identidad de Slack no válida." });
        }

        using var solicitudPerfil = new HttpRequestMessage(HttpMethod.Post, SlackUserInfoEndpoint);
        solicitudPerfil.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        solicitudPerfil.Content = new StringContent(
            string.Empty,
            Encoding.UTF8,
            "application/x-www-form-urlencoded");

        using var respuestaPerfil = await client.SendAsync(solicitudPerfil);
        if (!respuestaPerfil.IsSuccessStatusCode)
        {
            return StatusCode(502, new { mensaje = "No se pudo obtener la identidad de Slack." });
        }

        using var perfilDoc = ParsearJsonSlack(await respuestaPerfil.Content.ReadAsStringAsync());
        if (perfilDoc is null || !RespuestaOkSlack(perfilDoc))
        {
            return StatusCode(502, new { mensaje = "No se pudo obtener la identidad de Slack." });
        }

        var raiz = perfilDoc.RootElement;
        var sub = TextoSlack(raiz, "sub") ??
                  TextoSlack(raiz, "https://slack.com/user_id") ??
                  idTokenSub;

        if (string.IsNullOrWhiteSpace(sub))
        {
            return StatusCode(502, new { mensaje = "No se pudo obtener la identidad de Slack." });
        }

        var nombre = TextoSlack(raiz, "name");
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = TextoSlack(raiz, "given_name");
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = idTokenNombre;
        if (string.IsNullOrWhiteSpace(nombre))
            nombre = $"Usuario Slack {sub}";

        var email = TextoSlack(raiz, "email") ?? idTokenEmail ?? string.Empty;

        var usuario = await ObtenerOCrearUsuario("Slack", $"slack_{sub}", nombre, email);
        await IniciarSesionCookie(usuario, "Slack");
        return IrAlPerfil();
    }

    private void BorrarStateSlack() =>
        Response.Cookies.Delete(SlackAuthStateCookie, new CookieOptions
        {
            HttpOnly = true,
            Secure = Request.IsHttps,
            SameSite = SameSiteMode.Lax,
            Path = "/api/Auth/slack"
        });

    // ================= IDENTIDAD Y LOGOUT =================

    [HttpGet("me")]
    [TokenAuthorize]
    public IActionResult Me()
    {
        if (User.Identity?.IsAuthenticated != true)
        {
            return Unauthorized(new
            {
                autenticado = false,
                mensaje = "Token no válido."
            });
        }

        return Ok(new
        {
            autenticado = true,

            id = User.FindFirst("sub")?.Value,

            nombre = User.FindFirst("name")?.Value,

            email = User.FindFirst("email")?.Value,

            proveedor = User.FindFirst("provider")?.Value ?? "OpenIddict"
        });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { mensaje = "Sesión cerrada correctamente." });
    }

    // ================= HELPERS =================
    private bool GoogleConfigurado()
    {
        var clientId = Setting("Google:ClientId", "GOOGLE_CLIENT_ID");
        var clientSecret = Setting("Google:ClientSecret", "GOOGLE_CLIENT_SECRET");
        return !string.IsNullOrWhiteSpace(clientId) &&
               !string.IsNullOrWhiteSpace(clientSecret);
    }

    private string? Setting(string key, string environmentVariable)
    {
        var value = _configuration[key];

        return !string.IsNullOrWhiteSpace(value)
            ? value
            : Environment.GetEnvironmentVariable(environmentVariable);
    }

    // TikTok valida la redirect_uri registrada con barra final. La normalizamos
    // aquí para que el intercambio de código use exactamente el mismo valor.
    private static string? NormalizarCallbackTikTok(string? redirectUri)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            return null;

        return redirectUri.EndsWith("/", StringComparison.Ordinal)
            ? redirectUri
            : redirectUri + "/";
    }

    private string ProviderRedirectUri(string provider, string defaultPath)
    {
        var configured = _configuration[$"{provider}:RedirectUri"];

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var backendUrl = _configuration["BackendUrl"];

        if (string.IsNullOrWhiteSpace(backendUrl))
        {
            throw new InvalidOperationException(
                "BackendUrl no está configurado.");
        }

        return $"{backendUrl.TrimEnd('/')}{defaultPath}";
    }

    private string CrearState(string provider, string? returnUrl)
    {
        var state = Guid.NewGuid().ToString("N");

        HttpContext.Session.SetString(
            $"oauth_state_{provider}",
            state);

        var destino = NormalizarReturnUrl(returnUrl);

        if (destino != null)
        {
            HttpContext.Session.SetString(
                $"oauth_return_{provider}",
                destino);
        }
        else
        {
            HttpContext.Session.Remove($"oauth_return_{provider}");
        }

        return state;
    }

    private bool ValidarState(string provider, string? state)
    {
        var expected = HttpContext.Session.GetString(
            $"oauth_state_{provider}");

        HttpContext.Session.Remove($"oauth_state_{provider}");

        return !string.IsNullOrWhiteSpace(expected) &&
               string.Equals(expected, state, StringComparison.Ordinal);
    }

    private string? TomarReturnUrl(string provider)
    {
        var destino = HttpContext.Session.GetString(
            $"oauth_return_{provider}");

        HttpContext.Session.Remove($"oauth_return_{provider}");

        return NormalizarReturnUrl(destino);
    }

    // ---- state firmado (sin sesión) y tickets de traspaso, usados por TikTok ----
    private string CrearStateFirmado(string? returnUrl)
    {
        var carga = JsonSerializer.Serialize(new EstadoOAuth(
            Guid.NewGuid().ToString("N"),
            NormalizarReturnUrl(returnUrl),
            DateTimeOffset.UtcNow.AddMinutes(10).ToUnixTimeSeconds()));

        return _estadoProtector.Protect(carga);
    }

    private bool LeerStateFirmado(string? state, out string? returnUrl)
    {
        returnUrl = null;

        if (string.IsNullOrWhiteSpace(state))
            return false;

        try
        {
            var carga = JsonSerializer.Deserialize<EstadoOAuth>(_estadoProtector.Unprotect(state));

            if (carga == null ||
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() > carga.Exp)
            {
                return false;
            }

            returnUrl = NormalizarReturnUrl(carga.Ret);
            return true;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private string CrearTicketTikTok(int usuarioId, string? returnUrl)
    {
        var carga = JsonSerializer.Serialize(new TicketLogin(
            Guid.NewGuid().ToString("N"),
            usuarioId,
            NormalizarReturnUrl(returnUrl),
            DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds()));

        return _ticketProtector.Protect(carga);
    }

    private async Task<IActionResult> CanjearTicketTikTok(string ticket)
    {
        TicketLogin? carga;

        try
        {
            carga = JsonSerializer.Deserialize<TicketLogin>(_ticketProtector.Unprotect(ticket));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
        {
            return VolverAlLogin("El ticket de inicio de sesión de TikTok no es válido.");
        }

        if (carga == null || DateTimeOffset.UtcNow.ToUnixTimeSeconds() > carga.Exp)
        {
            return VolverAlLogin("El ticket de inicio de sesión de TikTok expiró. Intenta de nuevo.");
        }

        // Un solo uso.
        var clave = $"ticket:{carga.Nonce}";

        if (await _cache.GetStringAsync(clave) != null)
        {
            return VolverAlLogin("El ticket de inicio de sesión de TikTok ya fue usado.");
        }

        await _cache.SetStringAsync(clave, "1", new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5)
        });

        var usuario = await _context.Usuarios.FindAsync(carga.UsuarioId);

        if (usuario == null)
        {
            return VolverAlLogin("El usuario del ticket no existe.");
        }

        return await FinalizarLogin(usuario, "TikTok", NormalizarReturnUrl(carga.Ret));
    }

    private static async Task<string> DetalleError(HttpResponseMessage respuesta)
    {
        var cuerpo = await respuesta.Content.ReadAsStringAsync();

        return cuerpo.Length > 200 ? cuerpo[..200] : cuerpo;
    }

    private sealed record EstadoOAuth(string Nonce, string? Ret, long Exp);

    private sealed record TicketLogin(string Nonce, int UsuarioId, string? Ret, long Exp);

    private string? NormalizarReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
            return null;

        return Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : null;
    }

    private static string CrearCodeVerifier()
    {
        Span<byte> bytes = stackalloc byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Base64UrlEncode(bytes);
    }

    private static string CrearCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private IActionResult VolverAlLogin(string mensaje)
    {
        return Redirect($"/login.html?error={Uri.EscapeDataString(mensaje)}");
    }
    private async Task<IActionResult> FinalizarLogin(
    Usuario usuario,
    string proveedor,
    string? returnUrl)
    {
        await IniciarSesionCookie(usuario, proveedor);

        return RedirigirDespuesLogin(returnUrl);
    }

    private IActionResult RedirigirDespuesLogin(string? returnUrl)
    {
        var destino = NormalizarReturnUrl(returnUrl);

        if (destino != null)
        {
            return Redirect(destino);
        }

        return IrAlPerfil();
    }
    private IActionResult IrAlPerfil()
    {
        var frontendUrl = _configuration["FrontendUrl"];

        if (!string.IsNullOrWhiteSpace(frontendUrl))
        {
            return Redirect($"{frontendUrl.TrimEnd('/')}/Bienvenida/bienvenida.html");
        }

        return Redirect("/Bienvenida/bienvenida.html");
    }

    private async Task<Usuario> ObtenerOCrearUsuario(
        string proveedor,
        string proveedorId,
        string? nombre,
        string? email)
    {
        nombre = string.IsNullOrWhiteSpace(nombre)
            ? $"Usuario {proveedor}"
            : nombre.Trim();

        email = email?.Trim() ?? "";

        var cuenta = await _context.CuentasExternas
            .FirstOrDefaultAsync(c =>
                c.Proveedor == proveedor &&
                c.ProveedorId == proveedorId);

        Usuario? usuario = null;

        if (cuenta != null)
        {
            usuario = await _context.Usuarios.FindAsync(cuenta.UsuarioId);
        }

        if (usuario == null && !string.IsNullOrWhiteSpace(email))
        {
            usuario = await _context.Usuarios
                .FirstOrDefaultAsync(u => u.email.ToLower() == email.ToLower());
        }

        if (usuario == null)
        {
            usuario = new Usuario
            {
                User = nombre,
                email = email,
                Biografia = $"Autenticado con {proveedor}",
                Fecha = DateTime.UtcNow.ToString("O"),
                Estado = "Activo"
            };

            _context.Usuarios.Add(usuario);
            await _context.SaveChangesAsync();
        }
        else
        {
            usuario.User = nombre;

            // X (Twitter) no entrega correo con el alcance users.read. No se debe
            // borrar el correo que el usuario ya tenía guardado.
            if (!string.IsNullOrWhiteSpace(email))
            {
                usuario.email = email;
            }

            if (string.IsNullOrWhiteSpace(usuario.Biografia))
            {
                usuario.Biografia = $"Autenticado con {proveedor}";
            }

            await _context.SaveChangesAsync();
        }

        if (cuenta == null)
        {
            _context.CuentasExternas.Add(new CuentaExterna
            {
                ProveedorId = proveedorId,
                Proveedor = proveedor,
                UsuarioId = usuario.Id
            });

            await _context.SaveChangesAsync();
        }
        else if (cuenta.UsuarioId != usuario.Id)
        {
            cuenta.UsuarioId = usuario.Id;
            await _context.SaveChangesAsync();
        }

        return usuario;
    }

    private async Task<string?> ObtenerEmailGitHub(HttpClient client)
    {
        var respuesta = await client.GetAsync("https://api.github.com/user/emails");

        if (!respuesta.IsSuccessStatusCode)
        {
            return null;
        }

        using var documento = JsonDocument.Parse(
            await respuesta.Content.ReadAsStringAsync());

        if (documento.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        foreach (var correo in documento.RootElement.EnumerateArray())
        {
            var primario = correo.TryGetProperty("primary", out var primarioProp) &&
                           primarioProp.ValueKind == JsonValueKind.True;

            var verificado = correo.TryGetProperty("verified", out var verificadoProp) &&
                             verificadoProp.ValueKind == JsonValueKind.True;

            if (primario && verificado &&
                correo.TryGetProperty("email", out var emailProp))
            {
                return emailProp.GetString();
            }
        }

        foreach (var correo in documento.RootElement.EnumerateArray())
        {
            var verificado = correo.TryGetProperty("verified", out var verificadoProp) &&
                             verificadoProp.ValueKind == JsonValueKind.True;

            if (verificado && correo.TryGetProperty("email", out var emailProp))
            {
                return emailProp.GetString();
            }
        }

        return null;
    }

    private async Task IniciarSesionCookie(Usuario usuario, string proveedor)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, usuario.Id.ToString()),
            new(ClaimTypes.Name, usuario.User),
            new(ClaimTypes.Email, usuario.email ?? ""),
            new("proveedor", proveedor)
        };

        var identity = new ClaimsIdentity(
            claims,
            CookieAuthenticationDefaults.AuthenticationScheme);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity));
    }
}
