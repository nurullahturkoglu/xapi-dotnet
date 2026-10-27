# Builds samples/LrsHost into a small runtime image. Releases publish it for linux/amd64 and linux/arm64 as
# ghcr.io/nurullahturkoglu/xapi-lrs (.github/workflows/release.yml). To build it, run from the repository root:
#   docker build -f samples/LrsHost/Dockerfile -t xapi-lrs .
#   docker run -p 8080:8080 -e LrsHost__Key=... -e LrsHost__Secret=... -v xapi-lrs-data:/data xapi-lrs
# The LRS is then at http://localhost:8080/xapi. See "Running the sample LRS in Docker" in docs/lrs.md for PostgreSQL.

# The SDK stage runs on the build machine's platform for every target platform: the output is portable IL, and
# SQLite's native libraries for each platform come with it under runtimes/
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.403@sha256:de4fe90a98845d75910b9769662465c84de1a934665c431279d1382b2bddc02b AS build
ARG VERSION=0.0.0
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY eng/ eng/
COPY src/ src/
COPY samples/LrsHost/ samples/LrsHost/
# The build context has no .git, so MinVer cannot compute the version; it comes from the VERSION argument.
# No apphost: it would be native code for the build platform, and the entrypoint runs the dll with dotnet.
RUN dotnet publish samples/LrsHost -c Release -o /app -p:MinVerSkip=true -p:Version="$VERSION" -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14@sha256:de0ee358cdd672f7017a003bed21e9ac9ed48a27c922a59b18175b6e82fa7fee
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown "$APP_UID" /data
VOLUME /data
# Empty values override the development credential in appsettings.json, so the container refuses to start
# until real ones are given
ENV LrsHost__Key="" \
    LrsHost__Secret="" \
    LrsHost__ConnectionString="Data Source=/data/lrs.db"
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "LrsHost.dll"]
