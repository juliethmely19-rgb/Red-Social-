using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Scalar.AspNetCore;
using OpenIddict.Abstractions;
using System.Security.Cryptography.X509Certificates;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;
using System.Text.Json;

// `dotnet run` lee el mismo .env que Docker Compose (en Docker no hay .env: no hace nada).
DotEnv.Load();

var builder = WebApplication.CreateBuilder(args);

// Variables de entorno del .env -> claves de configuración de la aplicación.
foreach (var (variable, clave) in new[]
{
    ("BACKEND_URL", "BackendUrl"),
    ("FRONTEND_URL", "FrontendUrl"),
    ("CORS_ALLOWED_ORIGINS", "Cors:AllowedOrigins"),
    ("OPENIDDICT_CERT_PASSWORD", "OpenIddict:CertificatePassword")
})
{
    var valor = Environment.GetEnvironmentVariable(variable);

    if (!string.IsNullOrWhiteSpace(valor))
    {
        builder.Configuration[clave] = valor;
    }
}

// Con `dotnet run` la base se arma con POSTGRES_* del .env apuntando a localhost
// (el puerto publicado por el contenedor de PostgreSQL). En Docker, Compose define
// ConnectionStrings__Context y esto no se ejecuta.
if (builder.Environment.IsDevelopment() &&
    string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ConnectionStrings__Context")))
{
    builder.Configuration["ConnectionStrings:Context"] =
        $"Host={DotEnv.Get("POSTGRES_HOST", "localhost")};" +
        $"Port={DotEnv.Get("POSTGRES_HOST_PORT", "5432")};" +
        $"Database={DotEnv.Get("POSTGRES_DB", "RedsocialDB")};" +
        $"Username={DotEnv.Get("POSTGRES_USER", "postgres")};" +
        $"Password={DotEnv.Get("POSTGRES_PASSWORD", "1234")}";
}

var esMigracion = args.Contains("--migrar");

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddDbContext<Context>(options =>
{
    options.UseNpgsql(
        builder.Configuration.GetConnectionString("Context"));
    options.UseOpenIddict();
});
builder.Services.AddHttpClient();
builder.Services.AddOpenApi(options =>
{
    // Declara el esquema Bearer: Scalar muestra el campo "Token" y lo envía en
    // la cabecera Authorization de cada petición.
    options.AddDocumentTransformer((document, context, cancellationToken) =>
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
        document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
        {
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            Description = "Access token emitido por OpenIddict tras iniciar sesión."
        };

        document.Security ??= new List<OpenApiSecurityRequirement>();
        document.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecuritySchemeReference("Bearer", document)] = new List<string>()
        });

        return Task.CompletedTask;
    });
});
builder.Services.AddControllers();
// Configuración de OpenIddict
builder.Services.AddOpenIddict().AddCore(options =>
    {
        options.UseEntityFrameworkCore()
               .UseDbContext<Context>();
    }).AddServer(options =>
{
    // Endpoints del servidor de identidad
    options.SetAuthorizationEndpointUris("/connect/authorize");
    options.SetTokenEndpointUris("/connect/token");


    // Flujo Authorization Code + PKCE
    options.AllowAuthorizationCodeFlow();
    options.RequireProofKeyForCodeExchange();

    // Scopes permitidos
    options.RegisterScopes(
        OpenIddictConstants.Scopes.OpenId,
        OpenIddictConstants.Scopes.Profile,
        OpenIddictConstants.Scopes.Email
    );

    // Certificados: se usan los .pfx de la carpeta certificados/ (montada en
    // /certificados dentro de Docker). Todas las réplicas del backend deben usar
    // los mismos certificados para validar los tokens de las demás.
    var signingPath = CertificadosOpenIddict.Resolver(
        builder.Configuration["OpenIddict:SigningCertificate:Path"],
        builder.Environment.ContentRootPath,
        "certificados/signing.pfx");

    var encryptionPath = CertificadosOpenIddict.Resolver(
        builder.Configuration["OpenIddict:EncryptionCertificate:Path"],
        builder.Environment.ContentRootPath,
        "certificados/encryption.pfx");

    var certPassword = builder.Configuration["OpenIddict:CertificatePassword"];

    if (signingPath != null && encryptionPath != null)
    {
        options.AddSigningCertificate(
            X509CertificateLoader.LoadPkcs12FromFile(signingPath, certPassword));

        options.AddEncryptionCertificate(
            X509CertificateLoader.LoadPkcs12FromFile(encryptionPath, certPassword));
    }
    else if (builder.Environment.IsDevelopment())
    {
        // Sin carpeta certificados/ (por ejemplo, un compañero que no tiene los .pfx).
        options.AddDevelopmentEncryptionCertificate();
        options.AddDevelopmentSigningCertificate();
    }
    else if (esMigracion)
    {
        // El job de migraciones no emite tokens: le basta una clave temporal.
        options.AddEphemeralEncryptionKey();
        options.AddEphemeralSigningKey();
    }
    else
    {
        throw new InvalidOperationException(
            "No se encontraron los certificados de OpenIddict. Monta la carpeta " +
            "certificados/ o configura OpenIddict:SigningCertificate:Path, " +
            "OpenIddict:EncryptionCertificate:Path y OpenIddict:CertificatePassword.");
    }

    // Integración con ASP.NET Core
    var aspNetCore = options.UseAspNetCore()
        .EnableAuthorizationEndpointPassthrough();

    // Detrás de nginx el backend recibe HTTP plano y OpenIddict rechazaría
    // /connect/* ("This server only accepts HTTPS requests"). Solo se exige
    // HTTPS si se activa explícitamente con OpenIddict:RequireHttps=true.
    if (!builder.Configuration.GetValue<bool>("OpenIddict:RequireHttps"))
    {
        aspNetCore.DisableTransportSecurityRequirement();
    }
});
builder.Services.AddOpenIddict()
    .AddValidation(options =>
    {
        // Valida los tokens emitidos por este mismo servidor.
        options.UseLocalServer();

        // Integra la validación con ASP.NET Core.
        options.UseAspNetCore();
    });
var dataProtectionKeysPath = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    ".aspnet",
    "DataProtection-Keys"
);
Directory.CreateDirectory(dataProtectionKeysPath);
builder.Services.AddDataProtection()
    .SetApplicationName("RedSocial777")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeysPath));

var authentication = builder.Services.AddAuthentication(
    CookieAuthenticationDefaults.AuthenticationScheme
)
.AddCookie(options =>
{
    options.Cookie.Name = "RedSocial.Auth";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.ExpireTimeSpan = TimeSpan.FromHours(2);
    options.SlidingExpiration = true;
    options.Events.OnRedirectToLogin = context =>
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return Task.CompletedTask;
    };
    options.Events.OnRedirectToAccessDenied = context =>
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return Task.CompletedTask;
    };
})
.AddCookie("External");

// Solo se registra Google si hay credenciales. Si faltan, el esquema OAuth
// se inicializa en cada petición y tumba toda la API con un error 500.
var googleClientId = builder.Configuration["Google:ClientId"];
var googleClientSecret = builder.Configuration["Google:ClientSecret"];

if (string.IsNullOrWhiteSpace(googleClientId))
{
    googleClientId = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_ID");
}

if (string.IsNullOrWhiteSpace(googleClientSecret))
{
    googleClientSecret = Environment.GetEnvironmentVariable("GOOGLE_CLIENT_SECRET");
}

if (!string.IsNullOrWhiteSpace(googleClientId) &&
    !string.IsNullOrWhiteSpace(googleClientSecret))
{
    authentication.AddGoogle(options =>
    {
        options.ClientId = googleClientId;
        options.ClientSecret = googleClientSecret;
        options.CallbackPath = builder.Configuration["Google:CallbackPath"] ?? "/signin-google";
        options.SignInScheme = "External";

        options.Events.OnRedirectToAuthorizationEndpoint = context =>
        {
            var url = QueryHelpers.AddQueryString(
                context.RedirectUri,
                "prompt",
                "select_account"
            );

            context.Response.Redirect(url);
            return Task.CompletedTask;
        };
    });
}

builder.Services.AddAuthorization();
var origenesPermitidos = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
{
    "http://localhost:5500",
    "http://127.0.0.1:5500"
};

foreach (var url in new[]
{
    builder.Configuration["FrontendUrl"],
    builder.Configuration["BackendUrl"]
})
{
    if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
    {
        origenesPermitidos.Add(uri.GetLeftPart(UriPartial.Authority));
    }
}

foreach (var origen in (builder.Configuration["Cors:AllowedOrigins"] ?? "").Split(
             ',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
{
    origenesPermitidos.Add(origen.TrimEnd('/'));
}

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy.WithOrigins(origenesPermitidos.ToArray())
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
var redisConnection = builder.Configuration["REDIS_CONNECTION"];

if (!string.IsNullOrWhiteSpace(redisConnection))
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnection;
        options.InstanceName = "RedSocial:";
    });
}
else
{
    // Para seguir trabajando localmente sin Redis.
    builder.Services.AddDistributedMemoryCache();
}

builder.Services.AddSession(options =>
{
    options.Cookie.Name = "RedSocial.Session";
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    options.IdleTimeout = TimeSpan.FromMinutes(15);
});
builder.Services.AddOptions<ScalarOptions>().BindConfiguration("Scalar");

if (esMigracion)
{
    // Job "migraciones" de Docker Compose: se ejecuta UNA vez antes de levantar las
    // réplicas del backend (migra la base, registra el cliente OpenIddict y crea la
    // clave de Data Protection compartida) y termina.
    using var migrationApp = builder.Build();

    using var scope = migrationApp.Services.CreateScope();

    var db = scope.ServiceProvider.GetRequiredService<Context>();

    db.Database.Migrate();

    await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, migrationApp.Configuration);

    scope.ServiceProvider
        .GetRequiredService<IDataProtectionProvider>()
        .CreateProtector("RedSocial.Init")
        .Protect("ok");

    return;
}

var app = builder.Build();

// `dotnet run`: aplica las migraciones pendientes automáticamente.
// En Docker las aplica el job "migraciones" (Database__MigrateOnStartup=false).
if (app.Environment.IsDevelopment() &&
    app.Configuration.GetValue("Database:MigrateOnStartup", true))
{
    using var scope = app.Services.CreateScope();
    scope.ServiceProvider.GetRequiredService<Context>().Database.Migrate();
}

// Con varias réplicas el cliente OpenIddict lo registra el job de migraciones
// (OpenIddict__SeedOnStartup=false); con `dotnet run` se registra aquí.
if (app.Configuration.GetValue("OpenIddict:SeedOnStartup", true))
{
    using var scope = app.Services.CreateScope();

    try
    {
        await OpenIddictSeeder.SeedAsync(scope.ServiceProvider, app.Configuration);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex,
            "No se pudo registrar el cliente OpenIddict (¿otra réplica lo está haciendo?).");
    }
}

// Configure the HTTP request pipeline.
// Sirve wwwroot/index.html como página pública en "/".
app.UseDefaultFiles();

// /js/config.js dinámico: cuando las páginas las sirve el backend, la API es el
// mismo origen desde el que se cargó la página (localhost:5290, un túnel HTTPS, etc.).
// Debe ir antes de UseStaticFiles para tener prioridad sobre Frontend/js/config.js.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) &&
        context.Request.Path.Equals("/js/config.js", StringComparison.OrdinalIgnoreCase))
    {
        var origen = $"{context.Request.Scheme}://{context.Request.Host}";

        context.Response.ContentType = "application/javascript; charset=utf-8";
        context.Response.Headers.CacheControl = "no-store";

        await context.Response.WriteAsync(
            $"window.APP_CONFIG = {{ API_URL: {JsonSerializer.Serialize(origen)} }};");

        return;
    }

    await next();
});

// wwwroot (landing, legal/, scalar-config.js)
app.UseStaticFiles();

if (app.Environment.IsDevelopment() || app.Environment.IsProduction())
{
    app.MapOpenApi();
    app.MapScalarApiReference(o => o.AddPreferredSecuritySchemes("Bearer"));
}

// Frontend (login, Bienvenida, js, css, callback.html).
var frontendPath = Path.Combine(app.Environment.ContentRootPath, "Frontend");

if (Directory.Exists(frontendPath))
{
    // Ruta histórica: /login.html (la usan los redirects del backend).
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(Path.Combine(frontendPath, "login")),
        RequestPath = ""
    });

    // Misma estructura que el contenedor del frontend (puerto 5500):
    // /login/login.html, /Bienvenida/bienvenida.html, /callback.html, /js/..., etc.
    // Así las rutas relativas de los HTML funcionan igual servidos por el backend
    // o por el nginx del frontend.
    app.UseStaticFiles(new StaticFileOptions
    {
        FileProvider = new PhysicalFileProvider(frontendPath),
        RequestPath = ""
    });
}

app.UseRouting();
app.UseCors("Frontend");
app.UseSession();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.Run();
