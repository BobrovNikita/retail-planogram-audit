#!/usr/bin/env bash
# Restore ONLY into an empty volume. Existing data is never overwritten/deleted.
set -Eeuo pipefail
archive=${1:?Usage: bash deploy/scripts/restore.sh /absolute/backup.tar.gz}
[[ "$archive" = /* && -f "$archive" ]] || { echo 'An existing absolute archive path is required.' >&2; exit 1; }
[[ -f compose.yaml ]] || { echo 'Run from the repository root.' >&2; exit 1; }
[[ -z $(docker compose ps --status running -q app) ]] || { echo 'Stop the target application before restoring.' >&2; exit 1; }
(cd "$(dirname "$archive")" && sha256sum --check "$(basename "$archive").sha256")
gzip -t "$archive"
# GNU tar rejects parent/absolute paths. Use only a trusted archive made by backup.sh.
docker compose run --rm --no-deps -T --entrypoint sh app -c '
  test -z "$(ls -A /data)" || { echo "Target volume is not empty; refusing restore." >&2; exit 1; }
  umask 077
  tar -xzf - -C /data --no-same-owner
' < "$archive"
printf 'Restored. Start the matching application version and verify /health and saved reports.\n'
