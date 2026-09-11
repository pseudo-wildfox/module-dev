FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

WORKDIR /src

COPY api/Api.csproj api/
COPY contracts/ModuleDev.Contracts.csproj contracts/
COPY workflow/ModuleDev.Workflow.csproj workflow/
COPY action-runtime/ModuleDev.ActionRuntime.csproj action-runtime/

RUN dotnet restore api/Api.csproj

COPY api/ api/
COPY contracts/ contracts/
COPY workflow/ workflow/
COPY action-runtime/ action-runtime/

RUN dotnet publish api/Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore


FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

WORKDIR /app

COPY --from=build /app/publish .

COPY contracts ./contracts

USER app

ENV ASPNETCORE_URLS=http://+:8080

EXPOSE 8080

ENTRYPOINT ["dotnet", "Api.dll"]