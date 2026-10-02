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
ARG INSTALL_FFMPEG=false
ENV APP_DLL=$APP_DLL
ENV DOTNET_ENVIRONMENT=Production
ENV ASPNETCORE_ENVIRONMENT=Production

RUN apt-get update \
    && if [ "$INSTALL_FFMPEG" = "true" ]; then apt-get install -y --no-install-recommends ffmpeg; fi \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

ENTRYPOINT ["sh", "-c", "dotnet /app/$APP_DLL"]
