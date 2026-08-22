FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY cli/Cli.csproj cli/
RUN dotnet restore cli/Cli.csproj

COPY cli/ cli/
RUN dotnet publish cli/Cli.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Cli.dll"]