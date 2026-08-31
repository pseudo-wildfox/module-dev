FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY cli/Cli.csproj cli/
COPY contracts/ModuleDev.Contracts.csproj contracts/
COPY workflow/ModuleDev.Workflow.csproj workflow/

RUN dotnet restore cli/Cli.csproj

COPY cli/ cli/
COPY contracts/ contracts/
COPY workflow/ workflow/

RUN dotnet publish cli/Cli.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

RUN apt-get update \
    && apt-get install -y --no-install-recommends \
       libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=build /app/publish .

COPY contracts ./contracts

ENTRYPOINT ["dotnet", "Cli.dll"]