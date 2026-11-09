#!/usr/bin/env python3
"""Prépare une entrée EXE + wad à partir d'un publish et des distributions complètes fournies.

Aucun téléchargement, aucune qualification ni publication. Entrées en lecture seule.
L'assemblage reste distinct de create_pack.py et de sa garde PROD.
"""
from __future__ import annotations

import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

from create_pack import (COMMON_EXECUTABLES, FIXED_BROWSER, FLAVORS, LIGHT, MAX_FILES, MAX_FILE, MAX_PACK,
                         fail, inventory, no_links, reject_light_dropin, require_pe_x64, sha)


def copy_tree(source: Path, destination: Path, light_publish: bool = False) -> None:
    no_links(source)
    if not source.is_dir():
        fail(f"Dossier requis : {source}")
    count = 0
    total = 0
    for parent, directories, files in os.walk(source, followlinks=False):
        if light_publish:
            for name in directories + files:
                reject_light_dropin((Path(parent) / name).relative_to(source).as_posix())
        for name in directories:
            no_links(Path(parent) / name)
        for name in files:
            path = Path(parent) / name
            no_links(path)
            info = path.stat()
            count += 1
            total += info.st_size
            if not path.is_file() or info.st_nlink != 1 or count > MAX_FILES or info.st_size > MAX_FILE or total > MAX_PACK:
                fail("Entrée spéciale, liée ou trop volumineuse.")
            output = destination / path.relative_to(source)
            output.parent.mkdir(parents=True, exist_ok=True)
            before = sha(path)
            with path.open("rb") as reader, output.open("xb") as writer:
                shutil.copyfileobj(reader, writer, 1024 * 1024)
            if sha(output) != before or sha(path) != before:
                fail("L'entrée a changé pendant sa copie.")


def copy_publish(source: Path, destination: Path, flavor: str) -> None:
    """Les symboles et notices XML du SDK ne sont pas nécessaires à l'exécution."""
    if flavor not in FLAVORS:
        fail("Édition de production inconnue.")
    symbols = {name + ".pdb" for name in ("FILPRO", "Filpro.Core", "Filpro.Bridge", "Filpro.Platform", "Filpro.Simulation")}
    documentation = {"Microsoft.Web.WebView2." + name + ".xml" for name in ("Core", "WinForms", "Wpf")}
    copy_tree(source, destination, light_publish=flavor == LIGHT)
    for entry in destination.iterdir():
        if entry.name in {"FILPRO.exe", "wad"}:
            continue
        if not entry.is_file() or entry.name not in symbols | documentation:
            fail("Fichier publish inattendu à la racine : " + entry.name)
        entry.unlink()  # Seulement la copie de travail ; l’entrée publish reste intacte.


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--publish", required=True, type=Path)
    parser.add_argument("--webview2", type=Path, help="Distribution Fixed Version x64 déjà extraite (PROD uniquement)")
    parser.add_argument("--usmt", required=True, type=Path, help="Dossier amd64 complet de l'ADK")
    parser.add_argument("--output", required=True, type=Path, help="Nouveau dossier hors dépôt")
    parser.add_argument("--flavor", default="PROD", choices=FLAVORS)
    parser.add_argument("--source-commit", required=True)
    args = parser.parse_args()
    repository = Path(__file__).resolve().parents[3]
    if args.flavor == LIGHT and args.webview2 is not None:
        fail("--webview2 est interdit pour PROD-LIGHT ; Evergreen est fourni par le poste.")
    if args.flavor != LIGHT and args.webview2 is None:
        fail("--webview2 est requis pour PROD.")
    publish, usmt, output = (path.absolute() for path in (args.publish, args.usmt, args.output))
    webview = args.webview2.absolute() if args.webview2 is not None else None
    inputs = [publish, usmt] + ([webview] if webview is not None else [])
    for path in inputs + [output]:
        if ".." in path.parts:
            fail("Traversée refusée.")
        no_links(path)
    if output.exists() or output.is_relative_to(repository) or any(output.is_relative_to(p) or p.is_relative_to(output) for p in inputs):
        fail("Sortie absente, hors dépôt et distincte des entrées requise.")
    # Source exacte : les fichiers non suivis/modifiés ne sont pas incorporés sous un SHA trompeur.
    def git(*command: str) -> bytes:
        return subprocess.check_output(["git", "-C", str(repository), *command])
    if not (repository / ".git").exists():
        fail("La fabrication exige un clone du dépôt Git, positionné sur le commit ou le tag voulu.")
    def verify_revision() -> None:
        if git("rev-parse", "HEAD").decode().strip() != args.source_commit or git("status", "--porcelain", "--untracked-files=all", "--", "v2/").strip():
            fail("Le commit source doit être HEAD et les sources v2 doivent être propres.")
    verify_revision()
    names = [name for name in git("ls-files", "-z", "--", "v2/").decode().split("\0") if name]
    if not 0 < len(names) <= MAX_FILES:
        fail("Inventaire source vide ou trop volumineux.")
    for name in names:
        source_file = repository / name
        no_links(source_file)
        if sha(source_file) != hashlib.sha256(git("show", args.source_commit + ":" + name)).hexdigest():
            fail("Le contenu des sources ne correspond pas au commit Git.")
    if webview is not None:
        for relative in ("msedgewebview2.exe", "msedge.dll", "icudtl.dat", "resources.pak", "v8_context_snapshot.bin"):
            if not (webview / relative).is_file():
                fail("Distribution WebView2 fixe incomplète : " + relative)
        if not (webview / "locales").is_dir() or not any((webview / "locales").glob("*.pak")):
            fail("Ressources linguistiques WebView2 absentes.")
    for relative in ("scanstate.exe", "loadstate.exe", "usmtutils.exe", "MigApp.xml", "MigDocs.xml", "MigUser.xml", "unbcl.dll"):
        if not (usmt / relative).is_file():
            fail("Distribution USMT incomplète : " + relative)
    output.parent.mkdir(parents=True, exist_ok=True)
    stage = Path(tempfile.mkdtemp(prefix=".filpro-input-", dir=output.parent))
    try:
        copy_publish(publish, stage, args.flavor)
        if webview is not None:
            copy_tree(webview, stage / "wad/runtime/webview2")
        copy_tree(usmt, stage / "wad/USMT")
        inventory(stage, args.flavor)
        for executable in COMMON_EXECUTABLES + ((FIXED_BROWSER,) if webview is not None else ()):
            require_pe_x64(stage / executable)
        verify_revision()
        if output.exists():
            fail("Sortie concurrente apparue.")
        stage.rename(output)
        print(f"Entrée {args.flavor} préparée : {output}")
        print("À qualifier avant create_pack.py PROD ; aucun test ni publication effectué.")
    finally:
        if stage.exists():
            shutil.rmtree(stage)


if __name__ == "__main__":
    try:
        main()
    except (ValueError, OSError, subprocess.CalledProcessError) as error:
        raise SystemExit(f"REFUS : {error}") from None
