#!/usr/bin/env bash
# shellcheck source-path=SCRIPTDIR
# Runs one ADL LRS conformance battery and gates the result the same way conformance.yml does.
set -euo pipefail

usage() {
    cat <<'EOF'
usage: eng/conformance/run-local.sh --target lrsql|local --xapi 1.0.3|2.0.0 [--db sqlite|postgres] [--keep]

  --target lrsql   start the pinned yetanalytics/lrsql image in Docker and test it (reference run)
  --target local   test our LRS, already running at $LRS_URL (gated with the xapi-dotnet floor)
  --xapi           battery to run
  --db             lrsql: database behind it; local: floor.json key only (default: sqlite)
  --keep           leave the lrsql and postgres containers running afterwards

environment:
  LRS_URL          --target local only (default: http://localhost:8080/xapi)
  LRS_KEY          Basic auth key (default: cts_key)
  LRS_SECRET       Basic auth secret (default: cts_secret)
  CTS_PORT         host port for lrsql (default: 8080)
  CTS_CACHE        suite checkout cache (default: <repo>/.cts)

Results (log, JUnit, summary, console, LRS log) go to .cts/results/<target>-<db>-<xapi>/.
EOF
}

die() {
    echo "error: $*" >&2
    exit 2
}

here=$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)
repo=$(cd "$here/../.." && pwd)
# shellcheck source=cts.env
source "$here/cts.env"

target=""
xapi=""
db=sqlite
keep=false
while [[ $# -gt 0 ]]; do
    case $1 in
        --target) target=${2:-}; shift 2 || die "--target needs a value" ;;
        --xapi) xapi=${2:-}; shift 2 || die "--xapi needs a value" ;;
        --db) db=${2:-}; shift 2 || die "--db needs a value" ;;
        --keep) keep=true; shift ;;
        -h | --help) usage; exit 0 ;;
        *) usage >&2; die "unknown argument: $1" ;;
    esac
done

case $target in
    lrsql) floor_target=lrsql ;;
    local) floor_target='xapi-dotnet' ;;
    *) usage >&2; die "--target must be lrsql or local" ;;
esac
[[ $xapi == 1.0.3 || $xapi == 2.0.0 ]] || die "--xapi must be 1.0.3 or 2.0.0"
[[ $db == sqlite || $db == postgres ]] || die "--db must be sqlite or postgres"

for tool in git node npm dotnet curl; do
    command -v "$tool" >/dev/null || die "$tool is not installed"
done
[[ $target == local ]] || command -v docker >/dev/null || die "docker is not installed"

node_major=$(node -p 'process.versions.node.split(".")[0]')
if [[ $node_major != "$CTS_NODE_MAJOR" ]]; then
    echo "warning: Node $node_major found; the lockfile and CI use Node $CTS_NODE_MAJOR" >&2
fi

key=${LRS_KEY:-cts_key}
secret=${LRS_SECRET:-cts_secret}
port=${CTS_PORT:-8080}
if [[ $target == lrsql ]]; then
    url=http://localhost:$port/xapi
else
    url=${LRS_URL:-http://localhost:8080/xapi}
fi

out=$repo/.cts/results/$target-$db-$xapi
rm -rf "$out"
mkdir -p "$out"

sha256() {
    if command -v sha256sum >/dev/null; then sha256sum "$1"; else shasum -a 256 "$1"; fi | cut -d' ' -f1
}

echo "==> CTS $CTS_SHA"
cts=${CTS_CACHE:-$repo/.cts}/$CTS_SHA
if ! git -C "$cts" cat-file -e "$CTS_SHA^{commit}" 2>/dev/null; then
    rm -rf "$cts"
    git init -q "$cts"
    git -C "$cts" fetch -q --depth 1 "$CTS_REPO" "$CTS_SHA"
fi
git -C "$cts" checkout -q --force "$CTS_SHA"
git -C "$cts" clean -q -fd
cp "$here/package.json" "$here/package-lock.json" "$cts/"
for patch in "$here"/patches/*.patch; do
    [[ -e $patch ]] || continue
    echo "    applying $(basename "$patch")"
    git -C "$cts" apply "$patch"
done

lock_hash=$(sha256 "$here/package-lock.json")
if [[ "$(cat "$cts/node_modules/.lock-sha256" 2>/dev/null || true)" != "$lock_hash" ]]; then
    echo "==> npm ci"
    (cd "$cts" && npm ci --no-audit --no-fund --ignore-scripts)
    echo "$lock_hash" >"$cts/node_modules/.lock-sha256"
fi

lrs='xapi-cts-lrsql'
pg='xapi-cts-postgres'
net='xapi-cts'

cleanup() {
    local status=$?
    if [[ $target == lrsql ]]; then
        docker logs "$lrs" >"$out/lrs.log" 2>&1 || true
        if [[ $keep == true ]]; then
            echo "==> kept containers: $lrs${pg_started:+ $pg} ($url)"
        else
            docker rm -f "$lrs" "$pg" >/dev/null 2>&1 || true
            docker network rm "$net" >/dev/null 2>&1 || true
        fi
    fi
    exit "$status"
}

if [[ $target == lrsql ]]; then
    docker rm -f "$lrs" "$pg" >/dev/null 2>&1 || true
    trap cleanup EXIT
    lrsql_env=(
        -e "LRSQL_API_KEY_DEFAULT=$key"
        -e "LRSQL_API_SECRET_DEFAULT=$secret"
        -e LRSQL_ADMIN_USER_DEFAULT=cts_admin
        -e LRSQL_ADMIN_PASS_DEFAULT=cts_admin_pass
    )
    if [[ $db == postgres ]]; then
        echo "==> starting $POSTGRES_IMAGE"
        docker network inspect "$net" >/dev/null 2>&1 || docker network create "$net" >/dev/null
        docker run -d --name "$pg" --network "$net" \
            -e POSTGRES_USER=lrsql -e POSTGRES_PASSWORD=lrsql -e POSTGRES_DB=lrsql \
            "$POSTGRES_IMAGE" >/dev/null
        pg_started=1
        for _ in $(seq 60); do
            docker exec "$pg" pg_isready -q -U lrsql -d lrsql 2>/dev/null && break
            sleep 1
        done
        docker exec "$pg" pg_isready -q -U lrsql -d lrsql || die "postgres did not start"
        lrsql_env+=(
            -e "LRSQL_DB_HOST=$pg" -e LRSQL_DB_PORT=5432 -e LRSQL_DB_NAME=lrsql
            -e LRSQL_DB_USER=lrsql -e LRSQL_DB_PASSWORD=lrsql
        )
        lrsql_cmd=(/lrsql/bin/run_postgres.sh)
        lrsql_net=(--network "$net")
    else
        # No LRSQL_DB_NAME: the docs' db/lrsql.sqlite.db needs a volume at /lrsql/db, a throwaway DB is what we want.
        lrsql_cmd=()
        lrsql_net=()
    fi
    echo "==> starting $LRSQL_IMAGE ($db)"
    docker run -d --name "$lrs" --platform linux/amd64 -p "$port:8080" \
        ${lrsql_net[@]+"${lrsql_net[@]}"} "${lrsql_env[@]}" "$LRSQL_IMAGE" ${lrsql_cmd[@]+"${lrsql_cmd[@]}"} >/dev/null
fi

echo "==> waiting for $url/about"
for _ in $(seq 90); do
    curl -sf "$url/about" >/dev/null && break
    if [[ $target == lrsql && "$(docker inspect -f '{{.State.Running}}' "$lrs" 2>/dev/null)" != true ]]; then
        die "lrsql exited; see $out/lrs.log"
    fi
    sleep 2
done
curl -sf "$url/about" || die "no answer from $url/about"
echo

echo "==> xAPI $xapi battery against $url"
rm -rf "$cts/logs"
start=$SECONDS
set +e
(cd "$cts" && node bin/console_runner.js -e "$url" -a -u "$key" -p "$secret" -x "$xapi") 2>&1 | tee "$out/console.txt"
suite_status=${PIPESTATUS[0]}
set -e
echo "==> suite exit code $suite_status (not used for gating), wall time $((SECONDS - start)) s"

logs=("$cts"/logs/*.log)
[[ -e ${logs[0]} ]] || die "the suite wrote no log; see $out/console.txt"
cp "${logs[0]}" "$out/cts-log.json"

echo "==> gate"
cd "$repo"
dotnet run eng/conformance/CtsGate.cs -- "$out/cts-log.json" \
    --floor-file eng/conformance/floor.json --target "$floor_target" --db "$db" --xapi "$xapi" \
    --allowlist "eng/conformance/known-failures-$xapi.txt" \
    --junit "$out/junit.xml" --summary "$out/summary.md"
