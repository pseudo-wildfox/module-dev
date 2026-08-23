FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY cli/Cli.csproj cli/
COPY contracts/ModuleDev.Contracts.csproj contracts/

RUN dotnet restore cli/Cli.csproj

COPY cli/ cli/
COPY contracts/ contracts/

RUN dotnet publish cli/Cli.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

COPY contracts ./contracts

ENTRYPOINT ["dotnet", "Cli.dll"]