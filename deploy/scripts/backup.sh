#!/usr/bin/env bash
# Run from the repository root; COMPOSE_FILE/COMPOSE_PROJECT_NAME are respected.
set -Eeuo pipefail
umask 077
destination=${1:?Usage: bash deploy/scripts/backup.sh /absolute/backup-directory}
[[ "$destination" = /* ]] || { echo 'Backup directory must be absolute.' >&2; exit 1; }
[[ -f compose.yaml ]] || { echo 'Run from the repository root.' >&2; exit 1; }
[[ -n $(docker compose ps --status running -q app) ]] || { echo 'Application must be running before backup.' >&2; exit 1; }
mkdir -p -- "$destination"
partial=$(mktemp "$destination/retailvision-$(date -u +%Y%m%dT%H%M%SZ)-XXXXXX.tar.gz.partial")
archive=${partial%.partial}
restart() { docker compose start app >/dev/null; }
trap restart EXIT
docker compose stop -t 45 app
docker compose run --rm --no-deps -T --entrypoint tar app -C /data -czf - . > "$partial"
gzip -t "$partial"
mv -- "$partial" "$archive"
(cd "$destination" && sha256sum "$(basename "$archive")") > "$archive.sha256"
restart
trap - EXIT
printf 'Backup created: %s\n' "$archive"
