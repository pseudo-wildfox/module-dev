FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY gateway/Gateway.csproj gateway/
RUN dotnet restore gateway/Gateway.csproj

COPY gateway/ gateway/
RUN dotnet publish gateway/Gateway.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Gateway.dll"]