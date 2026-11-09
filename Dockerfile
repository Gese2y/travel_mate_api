FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY src/AbroApi/AbroApi.csproj src/AbroApi/
RUN dotnet restore src/AbroApi/AbroApi.csproj
COPY src/AbroApi/ src/AbroApi/
RUN dotnet publish src/AbroApi/AbroApi.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
# Render sets PORT; fall back to 8080 locally.
ENV ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080}
CMD ["sh", "-c", "ASPNETCORE_URLS=http://0.0.0.0:${PORT:-8080} dotnet AbroApi.dll"]
