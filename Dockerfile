FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

ARG PROJECT_PATH
ARG APP_DLL

COPY Directory.Build.props ./
COPY Beats.sln ./
COPY src ./src

RUN dotnet publish "$PROJECT_PATH" \
    --configuration Release \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

ARG APP_DLL
ENV APP_DLL=$APP_DLL
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_ENVIRONMENT=Production

COPY --from=build /app/publish ./

ENTRYPOINT ["sh", "-c", "dotnet /app/$APP_DLL"]
