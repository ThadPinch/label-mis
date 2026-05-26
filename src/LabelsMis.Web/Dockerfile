FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json LabelsMis.sln ./
COPY src/LabelsMis.Domain/ ./src/LabelsMis.Domain/
COPY src/LabelsMis.Infrastructure/ ./src/LabelsMis.Infrastructure/
COPY src/LabelsMis.Web/ ./src/LabelsMis.Web/

RUN dotnet restore src/LabelsMis.Web/LabelsMis.Web.csproj

RUN dotnet publish src/LabelsMis.Web/LabelsMis.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080

COPY --from=build --chown=app:app /app/publish .

USER app

ENTRYPOINT ["dotnet", "LabelsMis.Web.dll"]
