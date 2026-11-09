#!/usr/bin/env python3
"""Assemble un nouveau pack normal EXE + wad ; aucun remplacement de pack existant.

PROD exige une qualification Windows de ces octets exacts. Ce programme ne fabrique
ni reçu de validation ni signature d'éditeur et ne publie aucun asset GitHub.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import shutil
import struct
import tempfile
import zipfile

MANIFEST = "wad/pack-manifest.json"
DEFAULTS = "wad/config/deployment-defaults.json"
LIGHT = "PROD-LIGHT"
FLAVORS = ("PROD", LIGHT)
FIXED_BROWSER = "wad/runtime/webview2/msedgewebview2.exe"
COMMON_EXECUTABLES = ("FILPRO.exe", "wad/USMT/scanstate.exe", "wad/USMT/loadstate.exe", "wad/USMT/usmtutils.exe")
COMMON_REQUIRED = {*COMMON_EXECUTABLES, "wad/web/index.html", "wad/USMT/MigApp.xml",
                   "wad/support/DEBLOQUER_TOUT.cmd", "wad/support/Debloquer-Pack.ps1"}
REQUIRED = COMMON_REQUIRED | {FIXED_BROWSER}
MAX_FILES = 20_000
MAX_FILE = 2 * 1024**3
MAX_PACK = 8 * 1024**3
SEMVER = re.compile(r"(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?\Z")
SHARED = {"theme", "motion", "navigationCollapsed", "autoSelectCurrentTask", "notifyOnCompletion", "recommendedSelection", "migApp", "vss", "continueOnError", "encrypted", "conflictPolicy"}
CHECKS = {"startup", "backupRestoreMultipleProfiles", "filesRegistryIntegrityConflicts", "hiveUnloadGuaranteed", "cancelAndFailurePreserveData", "demoIsolation", "updateRollbackRecovery"}


def fail(message: str) -> None:
    raise ValueError(message)


def sha(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def no_links(path: Path) -> None:
    for current in (path, *path.parents):
        if current.is_symlink() or (hasattr(current, "is_junction") and current.is_junction()):
            fail(f"Lien interdit : {current.name}")
        if current.exists():
            attributes = getattr(current.stat(follow_symlinks=False), "st_file_attributes", 0)
            if attributes & (0x400 | 0x1000 | 0x40000 | 0x400000):
                fail(f"Fichier spécial ou à la demande interdit : {current.name}")
        if current.name.casefold() in {"onedrive", "dropbox", "google drive", "googledrive", "iclouddrive", "icloud drive"} or current.name.casefold().startswith("onedrive - "):
            fail("Aucun stockage cloud/OneDrive pour assembler le pack.")
    for variable in ("OneDrive", "OneDriveConsumer", "OneDriveCommercial"):
        configured = os.environ.get(variable)
        if configured and path.is_relative_to(Path(configured).absolute()):
            fail("Dossier OneDrive exclu.")


def relative_path(value: str) -> None:
    if len(value) > 220 or "\\" in value or (value != "FILPRO.exe" and not value.startswith("wad/")):
        fail(f"Chemin de pack refusé : {value}")
    for part in value.split("/"):
        if not part or part in {".", ".."} or part.endswith((" ", ".")) or re.search(r'[\x00-\x1f<>:"/\\|?*]', part) or re.fullmatch(r"CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9]", part.split(".")[0], re.I):
            fail(f"Segment de chemin refusé : {value}")


def unique_object(pairs: list[tuple[str, object]]) -> dict:
    result: dict = {}
    for key, value in pairs:
        if key in result:
            fail(f"Propriété JSON en double : {key}")
        result[key] = value
    return result


def read_json(path: Path, limit: int) -> dict:
    no_links(path)
    if path.stat().st_nlink != 1:
        fail("Alias de fichier (hardlink) interdit.")
    if not 0 < path.stat().st_size <= limit:
        fail("Taille JSON refusée.")
    value = json.loads(path.read_text(encoding="utf-8"), object_pairs_hook=unique_object)
    if not isinstance(value, dict):
        fail("Objet JSON attendu.")
    return value


def validate_defaults(path: Path) -> None:
    data = read_json(path, 64 * 1024)
    if set(data) != {"schemaVersion", "application", "kind", "settings"} or type(data["schemaVersion"]) is not int or data["schemaVersion"] != 1 or data["application"] != "FILPRO" or data["kind"] != "shared-configuration":
        fail("Identité des défauts invalide.")
    settings = data["settings"]
    if not isinstance(settings, dict) or not set(settings) <= SHARED:
        fail("Réglage partageable interdit (aucun chemin, compte ou secret).")
    enums = {"theme": {"system", "light", "dark", "contrast"}, "motion": {"system", "full", "reduced"}, "conflictPolicy": {"keepBoth", "keepCurrent", "useSaved"}}
    for key, value in settings.items():
        if key in enums:
            if not isinstance(value, str) or value not in enums[key]:
                fail("Valeur de réglage inconnue.")
        elif type(value) is not bool:
            fail("Booléen attendu.")


def reject_light_dropin(relative: str) -> None:
    parts = relative.casefold().split("/")
    if (parts[:2] == ["wad", "runtime"] or
        parts[-1] in {"msedgewebview2.exe", "msedge.exe", "msedge.dll", "msedge_elf.dll", "webview2loader.dll",
                      "icudtl.dat", "resources.pak", "v8_context_snapshot.bin"} or
        parts[-1].startswith(("microsoftedgewebview2", "microsoft.webview2.fixedversionruntime")) and
        parts[-1].endswith((".exe", ".cab", ".msi", ".zip"))):
        fail(f"Contenu incompatible avec PROD-LIGHT (Evergreen installé sur Windows) : {relative}")


def inventory(root: Path, flavor: str = "PROD") -> list[dict]:
    if flavor not in FLAVORS:
        fail("Édition du pack inconnue.")
    no_links(root)
    if {entry.name for entry in root.iterdir()} != {"FILPRO.exe", "wad"} or not (root / "wad").is_dir():
        fail("La racine doit contenir exclusivement FILPRO.exe et le dossier wad.")
    result = []
    names: set[str] = set()
    total = 0
    for parent, directories, files in os.walk(root, followlinks=False):
        for name in directories + files:
            path = Path(parent) / name
            no_links(path)
            relative = path.relative_to(root).as_posix()
            if relative.casefold().split("/")[:2] == ["wad", "dev"]:
                fail("Les sources et symboles DEV ne font pas partie des archives de production.")
            if flavor == LIGHT:
                reject_light_dropin(relative)
            if relative != "wad":
                relative_path(relative)
            if relative.casefold() in names:
                fail("Chemins équivalents sous Windows.")
            names.add(relative.casefold())
        for name in files:
            path = Path(parent) / name
            relative = path.relative_to(root).as_posix()
            if path.stat().st_nlink != 1:
                fail("Alias de fichier (hardlink) interdit dans l'assemblage.")
            if relative == MANIFEST:
                fail("Le dossier d'entrée doit être neuf et ne pas contenir de manifeste recalculé.")
            if relative == DEFAULTS:
                validate_defaults(path)
                continue
            size = path.stat().st_size
            total += size
            if size > MAX_FILE or total > MAX_PACK or len(result) >= MAX_FILES:
                fail("Limite de taille du pack dépassée.")
            result.append({"path": relative, "size": size, "sha256": sha(path)})
    required = COMMON_REQUIRED if flavor == LIGHT else REQUIRED
    if not required <= {file["path"] for file in result}:
        fail("Fichier requis manquant pour " + flavor + ".")
    expected_directories = {"wad", "wad/config"}
    for item in result + ([{"path": DEFAULTS}] if (root / DEFAULTS).exists() else []):
        expected_directories.update(str(parent).replace("\\", "/") for parent in Path(item["path"]).parents if str(parent) != ".")
    for parent, directories, _ in os.walk(root, followlinks=False):
        for name in directories:
            if (Path(parent) / name).relative_to(root).as_posix() not in expected_directories:
                fail("Dossier vide non prévu dans l'inventaire.")
    return sorted(result, key=lambda item: item["path"])


def require_pe_x64(path: Path) -> None:
    with path.open("rb") as stream:
        header = stream.read(64)
        if len(header) < 64 or header[:2] != b"MZ":
            fail("Exécutable Windows PE manquant ou fictif.")
        offset = struct.unpack_from("<I", header, 60)[0]
        if offset > path.stat().st_size - 6:
            fail("En-tête PE tronqué.")
        stream.seek(offset)
        pe = stream.read(6)
        if pe[:4] != b"PE\0\0" or pe[4:] != b"\x64\x86":
            fail("Exécutable Windows x64 requis.")


def qualify(receipt: Path | None, version: str, commit: str, files: list[dict],
            flavor: str = "PROD") -> str:
    if receipt is None:
        fail(f"{flavor} interdit sans --windows-qualification portant sur ces octets exacts.")
    proof = read_json(receipt, 8 * 1024 * 1024)
    fields = {"schemaVersion", "application", "version", "sourceCommit", "platform", "status", "fullWindowsQualification", "checks", "files", "evidence"}
    if flavor == LIGHT:
        fields.add("flavor")
        if proof.get("flavor") != LIGHT:
            fail("Un reçu Windows propre à PROD-LIGHT est requis.")
    if set(proof) != fields or type(proof["schemaVersion"]) is not int or proof["schemaVersion"] != 1 or proof["application"] != "FILPRO" or proof["version"] != version or proof["sourceCommit"] != commit or proof["platform"] != "windows-x64" or proof["status"] != "passed" or proof["fullWindowsQualification"] is not True:
        fail("Le reçu n'est pas une qualification Windows complète du commit/version demandé.")
    checks = proof["checks"]
    if not isinstance(checks, dict) or set(checks) != CHECKS | {"encryption"} or any(checks[name] is not True for name in CHECKS) or checks["encryption"] is not True:
        fail("Les scénarios Windows critiques, chiffrement compris, doivent être réellement réussis.")
    if proof["files"] != files:
        fail("Les octets du pack diffèrent de ceux qualifiés sous Windows.")
    if not isinstance(proof["evidence"], list) or not proof["evidence"]:
        fail("Aucune preuve Windows jointe.")
    for entry in proof["evidence"]:
        if not isinstance(entry, dict) or set(entry) != {"path", "sha256"} or not isinstance(entry["path"], str):
            fail("Référence de preuve invalide.")
        name = entry["path"]
        if not name or name.startswith(("/", "\\")) or "\\" in name or any(part in {"", ".", ".."} or ":" in part for part in name.split("/")):
            fail("Chemin de preuve hors du dossier de reçu.")
        path = receipt.parent / name
        no_links(path)
        if not path.is_file() or sha(path) != entry["sha256"]:
            fail("Preuve Windows manquante ou modifiée.")
    return sha(receipt)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True, help="Dossier neuf déjà assemblé EXE + wad, jamais packs V1")
    parser.add_argument("--output", type=Path, required=True, help="Nouveau dossier hors dépôt ; doit être absent")
    parser.add_argument("--version", required=True)
    parser.add_argument("--source-commit", required=True)
    parser.add_argument("--flavor", choices=FLAVORS, default="PROD")
    parser.add_argument("--windows-qualification", type=Path)
    args = parser.parse_args()
    version = SEMVER.fullmatch(args.version)
    if not version or any(part.isdigit() and len(part) > 1 and part.startswith("0") for part in (version[4] or "").split(".")) or not re.fullmatch("[0-9a-f]{40}", args.source_commit):
        fail("Version SemVer ou commit invalide.")
    if version[4]:
        fail("Une préversion ne peut pas devenir un pack PROD stable.")
    if any(".." in path.parts for path in (args.input, args.output)) or any(int(version[index]) > 2_147_483_647 for index in (1, 2, 3)):
        fail("Traversée de répertoire ou composante de version excessive.")
    source = args.input.absolute()
    output = args.output.absolute()
    repository = Path(__file__).resolve().parents[3]
    no_links(source)
    no_links(output)
    source = source.resolve()
    output = output.resolve()
    if output.exists() or output.is_relative_to(repository) or source.is_relative_to(output) or output.is_relative_to(source):
        fail("La sortie doit être absente, hors du dépôt et distincte de l'entrée. Aucun pack V1 remplacé.")
    if source.is_relative_to(repository / "packs") or source.is_relative_to(repository / "wad"):
        fail("Les packs et ressources V1 sont en lecture seule et ne sont pas des entrées V2.")
    files = inventory(source, args.flavor)
    for executable in COMMON_EXECUTABLES + (() if args.flavor == LIGHT else (FIXED_BROWSER,)):
        require_pe_x64(source / executable)
    defaults_hash = sha(source / DEFAULTS) if (source / DEFAULTS).exists() else None
    proof_hash = qualify(args.windows_qualification, args.version, args.source_commit, files, args.flavor)
    output.parent.mkdir(parents=True, exist_ok=True)
    temporary = Path(tempfile.mkdtemp(prefix=".filpro-pack-", dir=output.parent))
    try:
        package_name = f"FILPRO-{args.version}-win-x64-{args.flavor}"
        stage = temporary / package_name
        shutil.copytree(source, stage, symlinks=False)
        if inventory(stage, args.flavor) != files or (sha(stage / DEFAULTS) if (stage / DEFAULTS).exists() else None) != defaults_hash or inventory(source, args.flavor) != files:
            fail("L'entrée a changé pendant la copie.")
        manifest = {"schemaVersion": 1, "application": "FILPRO", "version": args.version, "architecture": "win-x64", "flavor": args.flavor, "sourceCommit": args.source_commit, "files": files}
        (stage / MANIFEST).write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
        archive = temporary / f"{package_name}.zip"
        with zipfile.ZipFile(archive, "x", compression=zipfile.ZIP_DEFLATED, compresslevel=6) as zip_output:
            for path in sorted(stage.rglob("*")):
                if path.is_file():
                    zip_output.write(path, path.relative_to(stage).as_posix())
        expected_hashes = {entry["path"]: entry["sha256"] for entry in files}
        expected_hashes[MANIFEST] = sha(stage / MANIFEST)
        if defaults_hash is not None:
            expected_hashes[DEFAULTS] = defaults_hash
        with zipfile.ZipFile(archive, "r") as zip_input:
            if set(zip_input.namelist()) != set(expected_hashes) or len(zip_input.namelist()) != len(expected_hashes):
                fail("Inventaire du ZIP final incohérent.")
            for name, expected in expected_hashes.items():
                digest = hashlib.sha256()
                with zip_input.open(name) as stream:
                    for chunk in iter(lambda: stream.read(1024 * 1024), b""):
                        digest.update(chunk)
                if digest.hexdigest() != expected:
                    fail("Octets du ZIP final différents de l'assemblage vérifié.")
        (temporary / "SHA256SUMS").write_text(f"{sha(archive)}  {archive.name}\n", encoding="ascii")
        (temporary / "build-record.json").write_text(json.dumps({"schemaVersion": 1, "flavor": args.flavor, "sourceCommit": args.source_commit, "manifestSha256": sha(stage / MANIFEST), "defaultsSha256": defaults_hash, "windowsQualificationSha256": proof_hash, "publisherSignature": False, "published": False}, indent=2) + "\n", encoding="utf-8")
        # Rename atomique du dossier de résultat neuf, aucun overwrite.
        if output.exists():
            fail("Une sortie concurrente est apparue ; aucun remplacement.")
        temporary.rename(output)
        print(f"Pack {args.flavor} créé : {output / archive.name}")
        print("Intégrité SHA-256 ; aucune signature d'éditeur ni publication effectuée.")
    finally:
        if temporary.exists():
            shutil.rmtree(temporary)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, json.JSONDecodeError, UnicodeError) as error:
        raise SystemExit(f"REFUS : {error}") from None
