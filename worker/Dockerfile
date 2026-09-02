FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY worker/Worker.csproj worker/
COPY contracts/ModuleDev.Contracts.csproj contracts/
COPY action-runtime/ModuleDev.ActionRuntime.csproj action-runtime/

RUN dotnet restore worker/Worker.csproj

COPY worker/ worker/
COPY contracts/ contracts/
COPY action-runtime/ action-runtime/

RUN dotnet publish worker/Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Worker.dll"]