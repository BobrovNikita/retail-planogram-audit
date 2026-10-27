"""Package a committed checkout and the reviewed HoloSelecta demo, without local data."""
import argparse
import hashlib
import io
import json
from pathlib import Path, PurePosixPath
import stat
import subprocess
from datetime import datetime, timezone
import zipfile

MODEL_NAME = "RetailVision-HoloSelecta-v2.zip"
MODEL_SIZE = 53_294_010
MODEL_SHA256 = "c7fffc4ece1b7c47027b3340d18bfdb37e7f788f87c9e4e54f46b4d0da8b0480"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--model-bundle", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[1]
    output = args.output.resolve()
    checksum_file = Path(str(output) + ".sha256")
    if output.exists() or checksum_file.exists():
        raise ValueError("Output or its checksum already exists; choose a new name.")
    if not output.parent.is_dir():
        raise ValueError("Output parent directory must already exist.")
    if output.is_relative_to(repo):
        raise ValueError("Write the handoff outside the source repository.")
    model = args.model_bundle.resolve(strict=True)
    if model.stat().st_size != MODEL_SIZE:
        raise ValueError("Unexpected model bundle size.")
    model_bytes = model.read_bytes()
    if hashlib.sha256(model_bytes).hexdigest() != MODEL_SHA256:
        raise ValueError("Model bundle SHA-256 does not match the reviewed v2 archive.")

    def git(*arguments):
        return subprocess.run(["git", "-C", str(repo), *arguments], check=True, capture_output=True).stdout

    if git("status", "--porcelain", "--untracked-files=all").strip():
        raise ValueError("Commit reviewed changes and resolve untracked files before packaging.")
    commit = git("rev-parse", "HEAD").decode("ascii").strip()
    source_bytes = git("archive", "--format=zip", commit)
    entries = {}
    with zipfile.ZipFile(io.BytesIO(source_bytes)) as source:
        for entry in source.infolist():
            if entry.is_dir():
                continue
            path = PurePosixPath(entry.filename)
            if path.is_absolute() or ".." in path.parts or "\\" in entry.filename or stat.S_ISLNK(entry.external_attr >> 16):
                raise ValueError(f"Unsupported archive entry: {entry.filename}")
            name = "RetailVision/" + entry.filename
            if name in entries:
                raise ValueError(f"Duplicate source entry: {name}")
            entries[name] = source.read(entry)
    for required in ("global.json", "docs/delivery/README.md", "docs/delivery/handoff.md", "scripts/start-real-demo.ps1"):
        if "RetailVision/" + required not in entries:
            raise ValueError(f"Missing delivery file in commit: {required}")
    entries[MODEL_NAME] = model_bytes
    delivery = {
        "schemaVersion": 1,
        "createdAtUtc": datetime.now(timezone.utc).isoformat(),
        "sourceCommit": commit,
        "repository": "https://github.com/BobrovNikita/retail-planogram-audit",
        "modelBundle": {"file": MODEL_NAME, "size": MODEL_SIZE, "sha256": MODEL_SHA256},
        "prerequisites": {"dotnetSdk": json.loads(entries["RetailVision/global.json"])["sdk"]["version"],
                          "firstRestoreNeedsNetwork": True, "gpuRequired": False, "pythonRequiredForRuntime": False},
        "includesPrivateDatabase": False,
        "includesAdministratorAccount": False,
        "limitations": ["Experimental eight-SKU model; new independent validation pending.",
                        "Source demo package, not an offline SDK installer or a deployed VPS."],
    }
    entries["DELIVERY.json"] = (json.dumps(delivery, ensure_ascii=False, indent=2) + "\n").encode("utf-8")
    entries["START-HERE.md"] = ("# RetailVision — комплект для демонстрации\n\n"
        f"Исходный коммит: `{commit}`. Полностью распакуйте ZIP в отдельную папку.\n\n"
        "1. Откройте [порядок запуска](RetailVision/docs/delivery/handoff.md).\n"
        "2. Установите нужный .NET SDK, восстановите зависимости и соберите проект.\n"
        "3. Установите приложенный архив модели в новую папку данных и создайте своего администратора.\n"
        "4. Следуйте [сценарию показа](RetailVision/docs/delivery/demo-script.md).\n\n"
        "[Все материалы](RetailVision/docs/delivery/README.md) · "
        "[Готовность по ТЗ](RetailVision/docs/delivery/requirements-matrix.md) · "
        "[Вопросы защиты](RetailVision/docs/delivery/defense-qa.md)\n\n"
        "В архиве нет текущей базы, паролей и пользовательских отчётов. "
        "При первом restore нужна сеть; GPU/Python для запуска модели не нужны. "
        "Модель экспериментальная, восемь SKU; качество на новых полках не подтверждено.\n\n"
        "Точный состав выпуска: DELIVERY.json. Контрольные суммы: SHA256SUMS; "
        "хэш самого ZIP передаётся рядом в .sha256. Этот комплект не содержит Docker-образ.\n").encode("utf-8")
    hashes = {name: hashlib.sha256(data).hexdigest() for name, data in sorted(entries.items())}
    entries["SHA256SUMS"] = "".join(f"{digest}  {name}\n" for name, digest in hashes.items()).encode("utf-8")
    with output.open("xb") as target:
        with zipfile.ZipFile(target, "w", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as package:
            for name, data in sorted(entries.items()):
                package.writestr(name, data, compress_type=zipfile.ZIP_STORED if name == MODEL_NAME else zipfile.ZIP_DEFLATED)
    with zipfile.ZipFile(output) as package:
        if set(package.namelist()) != set(entries) or len(package.namelist()) != len(entries):
            raise ValueError("Package contents differ from the manifest.")
        for name, digest in hashes.items():
            if hashlib.sha256(package.read(name)).hexdigest() != digest:
                raise ValueError(f"Package verification failed: {name}")
        if package.read("SHA256SUMS") != entries["SHA256SUMS"]:
            raise ValueError("Checksum manifest was damaged.")
    with output.open("rb") as source:
        digest = hashlib.file_digest(source, "sha256").hexdigest()
    with checksum_file.open("x", encoding="utf-8", newline="\n") as target:
        target.write(f"{digest}  {output.name}\n")
    print(json.dumps({"output": str(output), "sha256": digest, "sourceCommit": commit,
                      "files": len(entries), "bytes": output.stat().st_size}, ensure_ascii=False, indent=2))


if __name__ == "__main__":
    main()
