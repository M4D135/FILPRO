"""Contrôles sur fichiers fictifs ; aucun exécutable fixture n'est lancé."""
import importlib.util
import json
from pathlib import Path
import shutil
import struct
import subprocess
import sys
import tempfile
import unittest
import xml.etree.ElementTree as ElementTree
import zipfile

V2 = Path(__file__).resolve().parents[2]
TOOLS = V2 / "tools/packaging"
spec = importlib.util.spec_from_file_location("filpro_pack", TOOLS / "create_pack.py")
pack = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pack)
sys.path.insert(0, str(TOOLS))
from prepare_input import copy_publish


class PackagingTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="filpro-packaging-test-")
        self.root = Path(self.temp.name).resolve()
        self.source = self.root / "input"
        self.source.mkdir()
        for name in pack.REQUIRED:
            target = self.source / name
            target.parent.mkdir(parents=True, exist_ok=True)
            if name.endswith(".exe"):
                header = bytearray(70)
                header[:2] = b"MZ"
                struct.pack_into("<I", header, 60, 64)
                header[64:70] = b"PE\0\0\x64\x86"
                target.write_bytes(header)
            else:
                target.write_text("FICTIF", encoding="utf-8")

    def tearDown(self):
        self.temp.cleanup()

    def invoke(self, flavor, name="output", receipt=None):
        command = [sys.executable, str(TOOLS / "create_pack.py"), "--input", str(self.source),
                               "--output", str(self.root / name), "--version", "2.0.0", "--source-commit", "a" * 40]
        if flavor is not None:
            command.extend(("--flavor", flavor))
        if receipt:
            command.extend(("--windows-qualification", str(receipt)))
        return subprocess.run(command, text=True, capture_output=True)

    def fixture_proof(self, flavor):
        evidence = self.root / "test-fixture.txt"
        evidence.write_text("Qualification fictive de ce test unitaire, aucun exécutable lancé.")
        proof = {"schemaVersion": 1, "application": "FILPRO", "platform": "windows-x64", "version": "2.0.0",
                 "sourceCommit": "a" * 40, "status": "passed", "fullWindowsQualification": True,
                 "files": pack.inventory(self.source, flavor),
                 "checks": dict.fromkeys(pack.CHECKS | {"encryption"}, True),
                 "evidence": [{"path": evidence.name, "sha256": pack.sha(evidence)}]}
        if flavor == pack.LIGHT:
            proof["flavor"] = pack.LIGHT
        receipt = self.root / f"fixture-{flavor}.json"
        receipt.write_text(json.dumps(proof))
        return receipt

    def test_support_required_even_for_prod(self):
        pack.inventory(self.source)
        (self.source / "wad/support/DEBLOQUER_TOUT.cmd").unlink()
        with self.assertRaises(ValueError):
            pack.inventory(self.source)

    def test_prod_rejects_missing_real_qualification(self):
        result = self.invoke("PROD")
        self.assertNotEqual(0, result.returncode)
        self.assertIn("PROD interdit sans", result.stderr)
        self.assertFalse((self.root / "output").exists())

    def test_dev_is_not_an_output_and_default_production_still_needs_proof(self):
        self.assertIn("invalid choice", self.invoke("DEV").stderr)
        self.assertIn("PROD interdit sans", self.invoke(None).stderr)
        self.assertFalse((self.root / "output").exists())
        result = self.invoke(None, receipt=self.fixture_proof("PROD"))
        self.assertEqual(0, result.returncode, result.stderr)
        with zipfile.ZipFile(self.root / "output/FILPRO-2.0.0-win-x64-PROD.zip") as archive:
            self.assertEqual("PROD", json.loads(archive.read(pack.MANIFEST))["flavor"])
        old_sources = self.source / "wad/DEV/source"
        old_sources.mkdir(parents=True)
        (old_sources / "legacy.cs").write_text("SOURCE")
        with self.assertRaisesRegex(ValueError, "DEV"):
            pack.inventory(self.source, "PROD")

    def test_light_archive_needs_its_own_exact_content_receipt(self):
        full_proof = self.fixture_proof("PROD")
        self.assertEqual(0, self.invoke("PROD", "full", full_proof).returncode)
        shutil.rmtree(self.source / "wad/runtime")
        with self.assertRaises(ValueError):
            pack.inventory(self.source, "PROD")
        self.assertIn("interdit sans", self.invoke(pack.LIGHT).stderr)
        self.assertIn("propre à PROD-LIGHT", self.invoke(pack.LIGHT, receipt=full_proof).stderr)
        forged = json.loads(full_proof.read_text())
        forged["flavor"] = pack.LIGHT
        full_proof.write_text(json.dumps(forged))
        self.assertIn("diffèrent de ceux qualifiés", self.invoke(pack.LIGHT, receipt=full_proof).stderr)
        receipt = self.fixture_proof(pack.LIGHT)
        result = self.invoke(pack.LIGHT, receipt=receipt)
        self.assertEqual(0, result.returncode, result.stderr)
        archive_name = "FILPRO-2.0.0-win-x64-PROD-LIGHT.zip"
        output = self.root / "output"
        with zipfile.ZipFile(output / archive_name) as archive:
            self.assertEqual(pack.COMMON_REQUIRED | {pack.MANIFEST}, set(archive.namelist()))
            manifest = json.loads(archive.read(pack.MANIFEST))
            self.assertEqual(pack.LIGHT, manifest["flavor"])
            self.assertEqual(json.loads(receipt.read_text())["files"], manifest["files"])
        self.assertEqual(f"{pack.sha(output / archive_name)}  {archive_name}", (output / "SHA256SUMS").read_text().strip())
        (self.source / "wad/web/index.html").write_text("MODIFIÉ DEPUIS LE REÇU")
        result = self.invoke(pack.LIGHT, "modified", receipt)
        self.assertIn("diffèrent de ceux qualifiés", result.stderr)
        self.assertFalse((self.root / "modified").exists())

    def test_light_refuses_empty_fixed_runtime_and_relocated_dropins(self):
        shutil.rmtree(self.source / "wad/runtime")
        bad = self.source / "wad/RunTime/WeBView2"
        bad.mkdir(parents=True)
        with self.assertRaisesRegex(ValueError, "incompatible avec PROD-LIGHT"):
            pack.inventory(self.source, pack.LIGHT)
        with self.assertRaisesRegex(ValueError, "incompatible avec PROD-LIGHT"):
            copy_publish(self.source, self.root / "publish-copy", pack.LIGHT)
        shutil.rmtree(bad.parent)
        for name in ("MsEDGEWebView2.ExE", "msedge.dll", "MicrosoftEdgeWebView2RuntimeInstallerX64.exe",
                     "Microsoft.WebView2.FixedVersionRuntime.140.x64.cab", "WebView2Loader.dll"):
            with self.subTest(name=name):
                unexpected = self.source / "wad/support" / name
                unexpected.write_text("INTRUS")
                with self.assertRaisesRegex(ValueError, "incompatible avec PROD-LIGHT"):
                    pack.inventory(self.source, pack.LIGHT)
                unexpected.unlink()

    def test_prepare_light_without_fixed_distribution(self):
        # Petit dépôt jetable exerçant la vérification réelle de HEAD, hors du dépôt de développement.
        repository = self.root / "source"
        scripts = repository / "v2/tools/packaging"
        scripts.mkdir(parents=True)
        for name in ("create_pack.py", "prepare_input.py"):
            shutil.copyfile(TOOLS / name, scripts / name)
        (repository / ".gitignore").write_text("__pycache__/\n")
        def git(*args):
            return subprocess.check_output(["git", "-C", str(repository), *args], text=True).strip()
        git("init", "-q")
        git("add", ".")
        git("-c", "user.name=Packaging test", "-c", "user.email=test@example.invalid", "-c", "commit.gpgsign=false",
            "-c", "core.hooksPath=" + str(self.root / "no-hooks"), "commit", "-qm", "Fixture")
        fixture_commit = git("rev-parse", "HEAD")
        shutil.rmtree(self.source / "wad/runtime")
        usmt = self.root / "ADK"
        (self.source / "wad/USMT").rename(usmt)
        for extra in ("MigDocs.xml", "MigUser.xml", "unbcl.dll"):
            (usmt / extra).write_text("USMT FICTIF")
        output = self.root / "assembled"
        command = [sys.executable, str(scripts / "prepare_input.py"), "--publish", str(self.source), "--usmt", str(usmt),
                   "--output", str(output), "--source-commit", fixture_commit]
        def invoke(*args):
            return subprocess.run([*command, *args], capture_output=True, text=True)
        self.assertIn("--webview2 est requis", invoke().stderr)
        self.assertIn("invalid choice", invoke("--flavor", "DEV").stderr)
        self.assertIn("--webview2 est interdit", invoke("--flavor", pack.LIGHT, "--webview2", str(self.root / "browser")).stderr)
        self.assertFalse(output.exists())
        result = invoke("--flavor", pack.LIGHT)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertFalse((output / "wad/runtime").exists())
        self.assertTrue((output / "wad/USMT/MigDocs.xml").is_file())
        self.assertEqual(pack.COMMON_REQUIRED | {"wad/USMT/" + f for f in ("MigDocs.xml", "MigUser.xml", "unbcl.dll")},
                         {f["path"] for f in pack.inventory(output, pack.LIGHT)})

    def test_case_collision_and_links_rejected(self):
        other = self.source / "wad/OTHER.txt"
        other.write_text("fictif")
        (self.source / "wad/other.txt").write_text("fictif")
        # Sur un volume insensible à la casse, le second chemin est le même fichier.
        if not other.samefile(self.source / "wad/other.txt"):
            with self.assertRaises(ValueError):
                pack.inventory(self.source)
        alias = self.source / "wad/link.txt"
        alias.symlink_to(other)
        with self.assertRaises(ValueError):
            pack.inventory(self.source)

    def test_publish_sidecars_are_excluded_from_both_production_editions(self):
        for name in ("Filpro.Core.pdb", "Microsoft.Web.WebView2.Core.xml"):
            (self.source / name).write_text("ANNEXE FICTIVE")
        shutil.rmtree(self.source / "wad/runtime")
        for flavor in pack.FLAVORS:
            target = self.root / flavor
            copy_publish(self.source, target, flavor)
            self.assertEqual({"FILPRO.exe", "wad"}, {p.name for p in target.iterdir()})
            self.assertFalse((target / "wad/DEV").exists())
        self.assertEqual("ANNEXE FICTIVE", (self.source / "Filpro.Core.pdb").read_text())

    def test_publish_unknown_dependency_is_not_silently_dropped(self):
        (self.source / "unexpected.dll").write_text("DEPENDANCE FICTIVE")
        with self.assertRaisesRegex(ValueError, "publish inattendu"):
            copy_publish(self.source, self.root / "unknown", "PROD")

    def test_elevated_host_ignores_startup_hooks(self):
        # DOTNET_STARTUP_HOOKS, posable dans HKCU\Environment, chargerait du code dans FILPRO élevé.
        project = ElementTree.parse(V2 / "src/Filpro.App/Filpro.App.csproj").getroot()
        settings = [(group.get("Condition"), element.get("Condition"), (element.text or "").strip())
                    for group in project.iter("PropertyGroup") for element in group.iter("StartupHookSupport")]
        self.assertEqual([(None, None, "false")], settings)
        for shared in ("Directory.Build.props", "Directory.Build.targets"):
            path = V2 / shared
            if path.exists():
                self.assertNotIn("StartupHookSupport", path.read_text(encoding="utf-8"))


if __name__ == "__main__":
    unittest.main()
