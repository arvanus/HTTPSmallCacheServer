FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app


FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["HTTPSmallCacheServer.csproj", "./"]
RUN dotnet restore "./HTTPSmallCacheServer.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "HTTPSmallCacheServer.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "HTTPSmallCacheServer.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
ENV PORT=5000
ENV CACHE_PATH=/app/cache
COPY --from=publish /app/publish .
ENTRYPOINT dotnet HTTPSmallCacheServer.dll --urls "http://*:$PORT"
EXPOSE $PORT