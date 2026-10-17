# Builds samples/LrsHost into a small runtime image. Run from the repository root:
#   docker build -f samples/LrsHost/Dockerfile -t xapi-lrs .
#   docker run -p 8080:8080 -e LrsHost__Key=... -e LrsHost__Secret=... -v xapi-lrs-data:/data xapi-lrs
# The LRS is then at http://localhost:8080/xapi. See "Running the sample LRS in Docker" in docs/lrs.md for PostgreSQL.

FROM mcr.microsoft.com/dotnet/sdk:10.0.402@sha256:0d99ebf1ccad15037b5c98c9fe8979d6b8dbac23812feae2277a0b277b5c5ffa AS build
ARG VERSION=0.0.0
WORKDIR /src
COPY global.json nuget.config Directory.Build.props Directory.Build.targets Directory.Packages.props .editorconfig ./
COPY eng/ eng/
COPY src/ src/
COPY samples/LrsHost/ samples/LrsHost/
# The build context has no .git, so MinVer cannot compute the version; it comes from the VERSION argument
RUN dotnet publish samples/LrsHost -c Release -o /app -p:MinVerSkip=true -p:Version="$VERSION"

FROM mcr.microsoft.com/dotnet/aspnet:10.0.13@sha256:d5633778c754e89a386882eddf8dbee6942447a8a4b8267c19cf1e1158d0b0ac
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
