#!/usr/bin/env bash
set -Eeuo pipefail
[[ -n ${GITHUB_RUN_ID:-} ]] || { echo 'This destructive cleanup is only for an isolated GitHub Actions job.' >&2; exit 1; }
export COMPOSE_PROJECT_NAME="rv-ci-${GITHUB_RUN_ID}-${GITHUB_RUN_ATTEMPT}"
original_project=$COMPOSE_PROJECT_NAME
restored_project="${original_project}-restore"
task_dir=$(mktemp -d)
export TLS_DIRECTORY="$task_dir/tls" SMOKE_STATE_DIR="$task_dir/state"
mkdir -p "$TLS_DIRECTORY"
export ADMIN_EMAIL=container-test@example.invalid ADMIN_PASSWORD_FILE="$task_dir/admin-password"
export COMPOSE_FILE=compose.yaml:compose.admin.yaml
export IMAGE_TAG=ci
export SMOKE_CA="$TLS_DIRECTORY/fullchain.pem"
cleanup() {
  docker compose logs --no-color --tail 60 || true
  for project in "$original_project" "$restored_project"; do
    COMPOSE_PROJECT_NAME="$project" docker compose down --volumes --remove-orphans || true
  done
}
trap cleanup EXIT
openssl req -x509 -newkey rsa:2048 -sha256 -nodes -days 1 -subj /CN=localhost \
  -addext subjectAltName=DNS:localhost -keyout "$TLS_DIRECTORY/privkey.pem" -out "$SMOKE_CA" >/dev/null 2>&1
openssl rand -base64 36 | tr -d '\n' > "$ADMIN_PASSWORD_FILE"
printf 'aA7!\n' >> "$ADMIN_PASSWORD_FILE"
# Compose file secrets preserve host permissions; the enclosing temporary directory is private.
chmod 444 "$ADMIN_PASSWORD_FILE"
docker compose config --quiet
docker build --target final -t retailvision:ci .
docker build --target verify -t retailvision:verify .
docker run --rm --read-only --tmpfs /tmp --cap-drop ALL --security-opt no-new-privileges retailvision:verify
docker compose up -d --wait --wait-timeout 150
docker compose exec -T app sh -c 'test "$(id -u)" != 0; test ! -d /probe; test ! -f /app/Fixtures/synthetic.onnx'
docker compose exec -T nginx nginx -t
test "$(curl -s -o /dev/null -w '%{http_code}' http://localhost:8080/)" = 308
test "$(curl -s -o /dev/null -w '%{redirect_url}' http://localhost:8080/)" = https://localhost:8443/
# --wait covers app readiness; retry the short Nginx startup window as well.
curl --retry 10 --retry-connrefused --retry-delay 1 --cacert "$SMOKE_CA" --fail --silent https://localhost:8443/health
python3 deploy/scripts/container-smoke.py prepare
bash deploy/scripts/backup.sh "$task_dir/backups"
archive=$(find "$task_dir/backups" -maxdepth 1 -name '*.tar.gz' -print -quit)
export COMPOSE_FILE=compose.yaml
docker compose up -d --force-recreate --wait --wait-timeout 150
python3 deploy/scripts/container-smoke.py verify
docker compose down
export COMPOSE_PROJECT_NAME=$restored_project
bash deploy/scripts/restore.sh "$archive"
docker compose up -d --wait --wait-timeout 150
curl --retry 10 --retry-connrefused --retry-delay 1 --cacert "$SMOKE_CA" --fail --silent https://localhost:8443/health
python3 deploy/scripts/container-smoke.py verify
docker compose stop app
if bash deploy/scripts/restore.sh "$archive"; then
  echo 'Restore incorrectly accepted a nonempty volume.' >&2
  exit 1
fi
echo 'Container checks passed, including backup/restore and nonempty-volume protection.'
