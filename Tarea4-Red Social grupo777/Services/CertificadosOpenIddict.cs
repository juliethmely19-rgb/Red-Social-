/// <summary>
/// Localiza los certificados .pfx de OpenIddict tanto en Docker
/// (/certificados/*.pfx montado como volumen) como con `dotnet run`
/// (carpeta certificados/ en la raíz del repositorio).
/// </summary>
public static class CertificadosOpenIddict
{
    public static string? Resolver(
        string? rutaConfigurada,
        string contentRoot,
        string rutaPorDefecto)
    {
        if (!string.IsNullOrWhiteSpace(rutaConfigurada))
        {
            var completa = Path.IsPathRooted(rutaConfigurada)
                ? rutaConfigurada
                : Path.GetFullPath(Path.Combine(contentRoot, rutaConfigurada));

            return File.Exists(completa) ? completa : null;
        }

        return DotEnv.FindUpwards(rutaPorDefecto, contentRoot, AppContext.BaseDirectory);
    }
}
