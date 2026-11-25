using OpenIddict.Abstractions;

/// <summary>
/// Registra (o actualiza) el cliente público "redsocial-frontend" en OpenIddict.
/// Los redirect_uri se calculan desde FrontendUrl, BackendUrl y Cors:AllowedOrigins,
/// de modo que funcionen con Docker, Live Server o las páginas servidas por el backend.
/// </summary>
public static class OpenIddictSeeder
{
    public const string ClientId = "redsocial-frontend";

    public static async Task SeedAsync(IServiceProvider services, IConfiguration configuration)
    {
        var manager = services.GetRequiredService<IOpenIddictApplicationManager>();

        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = ClientId,
            DisplayName = "Red Social Frontend",

            // Es un cliente público: no utiliza ClientSecret.
            ClientType = OpenIddictConstants.ClientTypes.Public,

            Permissions =
            {
                OpenIddictConstants.Permissions.Endpoints.Authorization,
                OpenIddictConstants.Permissions.Endpoints.Token,

                OpenIddictConstants.Permissions.GrantTypes.AuthorizationCode,
                OpenIddictConstants.Permissions.ResponseTypes.Code,
                OpenIddictConstants.Permissions.Prefixes.Scope + "openid",
                OpenIddictConstants.Permissions.Scopes.Profile,
                OpenIddictConstants.Permissions.Scopes.Email
            },

            Requirements =
            {
                OpenIddictConstants.Requirements.Features.ProofKeyForCodeExchange
            }
        };

        foreach (var uri in RedirectUris(configuration))
        {
            descriptor.RedirectUris.Add(new Uri(uri));
        }

        var application = await manager.FindByClientIdAsync(ClientId);

        if (application == null)
        {
            await manager.CreateAsync(descriptor);
        }
        else
        {
            await manager.UpdateAsync(application, descriptor);
        }
    }

    private static IEnumerable<string> RedirectUris(IConfiguration configuration)
    {
        var origenes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "http://localhost:5500",
            "http://127.0.0.1:5500"
        };

        var uris = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var frontendUrl = configuration["FrontendUrl"];

        if (Uri.TryCreate(frontendUrl, UriKind.Absolute, out var frontend))
        {
            origenes.Add(frontend.GetLeftPart(UriPartial.Authority));

            // Si FrontendUrl incluye una ruta (Live Server abriendo el repositorio),
            // el callback vive dentro de esa ruta.
            uris.Add($"{frontendUrl!.TrimEnd('/')}/callback.html");
        }

        if (Uri.TryCreate(configuration["BackendUrl"], UriKind.Absolute, out var backend))
        {
            origenes.Add(backend.GetLeftPart(UriPartial.Authority));
        }

        var extra = configuration["Cors:AllowedOrigins"] ?? "";

        foreach (var origen in extra.Split(
                     ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (Uri.TryCreate(origen, UriKind.Absolute, out var uri))
            {
                origenes.Add(uri.GetLeftPart(UriPartial.Authority));
            }
        }

        foreach (var origen in origenes)
        {
            // Frontend en la raíz (contenedor Docker, Live Server en la carpeta Frontend).
            uris.Add($"{origen}/callback.html");
            // Live Server abriendo la carpeta del repositorio.
            uris.Add($"{origen}/Tarea4-Red%20Social%20grupo777/Frontend/callback.html");
        }

        return uris;
    }
}
