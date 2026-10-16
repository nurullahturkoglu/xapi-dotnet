#!/usr/bin/env bash
# Adds an EF Core migration to both LRS provider projects, or removes the last one.
#   eng/ef-migrations.sh add <Name>
#   eng/ef-migrations.sh remove
# The model lives in src/Xapi.Lrs (XapiLrsDbContext); each provider package carries its own migrations.
set -euo pipefail

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "$here/.." && pwd)
cd "$repo"

action=${1:-}
case $action in
    add) name=${2:?usage: eng/ef-migrations.sh add <Name>} ;;
    remove) name="" ;;
    *) echo "usage: eng/ef-migrations.sh add <Name> | remove" >&2; exit 2 ;;
esac

dotnet tool restore >/dev/null

# Read by the provider csproj files: references Microsoft.EntityFrameworkCore.Design and writes a
# runtimeconfig.json, so dotnet-ef can use the provider library itself as the startup project.
export XapiEfDesign=true

for provider in Sqlite PostgreSql; do
    project=src/Xapi.Lrs.$provider/Xapi.Lrs.$provider.csproj
    echo "==> $provider"
    if [[ $action == add ]]; then
        dotnet ef migrations add "$name" --project "$project" --startup-project "$project" --output-dir Migrations
        # Migrations are found by reflection, so they stay out of the public API; and no BOM, like the rest
        # of the repository
        perl -pi -e 's/^\xEF\xBB\xBF// if $. == 1; s/^    public partial class /    internal partial class /; close ARGV if eof' \
            src/Xapi.Lrs.$provider/Migrations/*.cs
    else
        dotnet ef migrations remove --project "$project" --startup-project "$project"
    fi
done
