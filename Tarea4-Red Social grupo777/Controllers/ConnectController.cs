using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

[ApiController]
public class ConnectController : Controller
{
    private readonly Context _context;

    public ConnectController(Context context)
    {
        _context = context;
    }

    // GET|POST: /connect/authorize
    [HttpGet("~/connect/authorize")]
    [HttpPost("~/connect/authorize")]
    [IgnoreAntiforgeryToken]
    public async Task<IActionResult> Authorize()
    {
        // 1. Solicitud de autorización de OpenIddict.
        var request = HttpContext.GetOpenIddictServerRequest();

        if (request == null)
        {
            return BadRequest("No se encontró una solicitud de autorización.");
        }

        // 2. prompt=login: el cliente pide que el usuario ELIJA de nuevo cómo iniciar sesión.
        //    Se descarta la cookie que dejó un login anterior (por ejemplo, Google) y se
        //    envía a /login.html. Al volver, la solicitud ya no lleva prompt=login, así que
        //    no hay bucle: usará la cookie recién creada con el proveedor elegido.
        if (request.HasPromptValue(PromptValues.Login))
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            var parametros = (Request.HasFormContentType
                    ? Request.Form.Select(p => KeyValuePair.Create(p.Key, p.Value))
                    : Request.Query.Select(p => KeyValuePair.Create(p.Key, p.Value)))
                .Where(p => p.Key != Parameters.Prompt)
                .ToList();

            var siguiente = Request.PathBase + Request.Path + QueryString.Create(parametros);

            return Redirect(QueryHelpers.AddQueryString("/login.html", "returnUrl", siguiente));
        }

        // 3. Sin sesión: mandar a elegir proveedor.
        if (User.Identity?.IsAuthenticated != true)
        {
            if (request.HasPromptValue(PromptValues.None))
            {
                return Forbid(
                    authenticationSchemes: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme,
                    properties: new AuthenticationProperties(new Dictionary<string, string?>
                    {
                        [OpenIddictServerAspNetCoreConstants.Properties.Error] = Errors.LoginRequired,
                        [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] =
                            "El usuario no ha iniciado sesión."
                    }));
            }

            var returnUrl = Request.PathBase + Request.Path + Request.QueryString;

            return Redirect(QueryHelpers.AddQueryString("/login.html", "returnUrl", returnUrl));
        }

        // 4. Usuario local identificado por la cookie.
        var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;

        if (!int.TryParse(userId, out var id))
        {
            return Unauthorized("No se pudo identificar al usuario.");
        }

        var usuario = await _context.Usuarios.FirstOrDefaultAsync(u => u.Id == id);

        if (usuario == null)
        {
            return Unauthorized("El usuario no existe.");
        }

        // 5. Identidad que se guardará dentro del access token / id token.
        var identity = new ClaimsIdentity(
            authenticationType: "OpenIddict",
            nameType: Claims.Name,
            roleType: Claims.Role);

        identity.SetClaim(Claims.Subject, usuario.Id.ToString());
        identity.SetClaim(Claims.Name, usuario.User);

        if (!string.IsNullOrWhiteSpace(usuario.email))
        {
            identity.SetClaim(Claims.Email, usuario.email);
        }

        // Proveedor con el que se inició sesión (Google, GitHub, Dropbox, Figma, X, TikTok).
        var proveedor = User.FindFirst("proveedor")?.Value;

        if (!string.IsNullOrWhiteSpace(proveedor))
        {
            identity.SetClaim("provider", proveedor);
        }

        var principal = new ClaimsPrincipal(identity);

        // 6. Scopes concedidos.
        principal.SetScopes(request.GetScopes());

        // 7. Qué claims viajan en cada token.
        principal.SetDestinations(static claim => claim.Type switch
        {
            Claims.Subject or Claims.Name or Claims.Email or "provider" =>
            [
                Destinations.AccessToken,
                Destinations.IdentityToken
            ],

            _ => []
        });

        // 8. OpenIddict genera el código de autorización y redirige al cliente.
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }
}
