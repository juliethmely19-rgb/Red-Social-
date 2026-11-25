/// <summary>
/// Carga el archivo .env de la raíz del repositorio como variables de entorno.
/// Sirve para que `dotnet run` use exactamente el mismo .env que Docker Compose.
/// En Docker no existe .env dentro del contenedor, así que no hace nada.
/// Las variables que ya existen en el entorno tienen prioridad sobre el archivo.
/// </summary>
public static class DotEnv
{
    public static void Load()
    {
        var archivo = FindUpwards(".env");

        if (archivo == null)
        {
            return;
        }

        foreach (var linea in File.ReadAllLines(archivo))
        {
            var texto = linea.Trim();

            if (texto.Length == 0 || texto.StartsWith('#'))
            {
                continue;
            }

            if (texto.StartsWith("export ", StringComparison.Ordinal))
            {
                texto = texto[7..].TrimStart();
            }

            var igual = texto.IndexOf('=');

            if (igual <= 0)
            {
                continue;
            }

            var nombre = texto[..igual].Trim();
            var valor = texto[(igual + 1)..].Trim();

            if (valor.Length >= 2 &&
                ((valor[0] == '"' && valor[^1] == '"') ||
                 (valor[0] == '\'' && valor[^1] == '\'')))
            {
                valor = valor[1..^1];
            }
            else
            {
                var comentario = valor.IndexOf(" #", StringComparison.Ordinal);

                if (comentario >= 0)
                {
                    valor = valor[..comentario].TrimEnd();
                }
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(nombre)))
            {
                Environment.SetEnvironmentVariable(nombre, valor);
            }
        }
    }

    public static string Get(string nombre, string porDefecto)
    {
        var valor = Environment.GetEnvironmentVariable(nombre);

        return string.IsNullOrWhiteSpace(valor) ? porDefecto : valor;
    }

    /// <summary>
    /// Busca un archivo subiendo por los directorios padre (máximo 8 niveles).
    /// </summary>
    public static string? FindUpwards(string rutaRelativa, params string[] inicios)
    {
        var puntos = inicios.Length > 0
            ? inicios
            : new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory };

        foreach (var inicio in puntos)
        {
            var directorio = new DirectoryInfo(inicio);

            for (var nivel = 0; directorio != null && nivel < 8; nivel++)
            {
                var candidato = Path.Combine(directorio.FullName, rutaRelativa);

                if (File.Exists(candidato))
                {
                    return candidato;
                }

                directorio = directorio.Parent;
            }
        }

        return null;
    }
}
