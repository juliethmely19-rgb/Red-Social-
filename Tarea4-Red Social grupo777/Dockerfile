# ETAPA 1: Compilación y publicación
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copiar el proyecto y restaurar dependencias (capa cacheable)
COPY *.csproj ./
RUN dotnet restore

# Copiar el resto de archivos y publicar.
# Se usa "publish" (no "build") para que se copien wwwroot/ (landing, legal/,
# scalar-config.js) y Frontend/ junto a la aplicación.
COPY . .
RUN dotnet publish Tarea4.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ETAPA 2: Ejecución
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Copiar archivos publicados
COPY --from=build /app/publish .

# Puerto de escucha (las réplicas quedan detrás de nginx, que publica el 5290)
ENV ASPNETCORE_HTTP_PORTS=5290
EXPOSE 5290

# Iniciar la API (con el argumento --migrar actúa como job de migraciones)
ENTRYPOINT ["dotnet", "Tarea4.dll"]
