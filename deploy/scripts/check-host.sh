#!/usr/bin/env bash
set -Eeuo pipefail
: "${RETAILVISION_ROOT:?Set project directory}"
: "${RETAILVISION_HEALTH_URL:?Set public HTTPS health URL}"
: "${RETAILVISION_BACKUP_DIR:?Set backup directory}"
: "${RETAILVISION_TLS_CERT:?Set public certificate path}"
[[ "$RETAILVISION_HEALTH_URL" == https://* ]] || { echo 'Health URL must use HTTPS' >&2; exit 1; }
cd "$RETAILVISION_ROOT"
curl --fail --silent --show-error --max-time 20 "$RETAILVISION_HEALTH_URL" >/dev/null
openssl x509 -in "$RETAILVISION_TLS_CERT" -checkend 604800 -noout
docker compose ps --format json | python3 -c 'import sys,json; raw=sys.stdin.read(); rows=json.loads(raw) if raw.lstrip().startswith("[") else [json.loads(l) for l in raw.splitlines() if l.strip()]; assert {r["Service"] for r in rows if r.get("State")=="running"} >= {"app","nginx"}; assert all(r.get("Health") in ("","healthy",None) for r in rows)'
python3 - "$RETAILVISION_BACKUP_DIR" "$(docker info --format '{{.DockerRootDir}}')" <<'PY'
import pathlib,shutil,sys,time
folder=pathlib.Path(sys.argv[1]); archives=list(folder.glob('*.tar.gz'))
if not archives or time.time()-max(p.stat().st_mtime for p in archives)>48*3600:
    raise SystemExit('No backup newer than 48 hours')
for path in (pathlib.Path.cwd(),folder,pathlib.Path(sys.argv[2])):
    total,used,free=shutil.disk_usage(path)
    if free<2*1024**3 or free/total<.1: raise SystemExit('Low disk space: '+str(path))
print('HTTPS, containers, backup freshness and disk space: OK')
PY
