#!/usr/bin/env python3
"""Create a disposable Unity project and run the embedded texture importer's editor checks."""
import argparse
import hashlib
import json
import os
import re
from pathlib import Path
import shutil
import subprocess
import tempfile


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--unity", type=Path, required=True, help="Unity 6000.4.3f1 Editor executable")
    parser.add_argument("--output", type=Path, help="New directory for the harness, results and logs")
    parser.add_argument("--resolve-marble", metavar="GIT_URL",
                        help="Resolve a new full-SHA pin against Marble's package configuration through UPM")
    source_group = parser.add_mutually_exclusive_group(required=True)
    source_group.add_argument("--marble-root", type=Path,
                              help="Marble checkout whose manifest supplies the Git pin")
    source_group.add_argument("--dependency", metavar="GIT_URL",
                              help="Published full-SHA Git dependency to validate without a Marble checkout")
    args = parser.parse_args()
    if args.resolve_marble and not args.marble_root:
        parser.error("--resolve-marble requires --marble-root.")
    package_name = "com.unity.cloud.gltfast"
    packages = args.marble_root.resolve() / "unity/Packages" if args.marble_root else None
    manifest = json.loads((packages / "manifest.json").read_text()) if packages else None
    dependency = args.resolve_marble or args.dependency or manifest["dependencies"][package_name]
    if not re.fullmatch(r"https://github\.com/[^/]+/[^?#]+\.git\?path=[^#]+#[0-9a-f]{40}", dependency):
        parser.error("glTFast must use a Git package URL with a package path and full commit SHA.")
    if packages and (packages / package_name).exists():
        parser.error("Remove the embedded glTFast package before resolving or validating the Git dependency.")
    fixture = Path("Assets/Art/Props/VehicleShopBuilding/Shack_W2_Shingle.glb")
    source = Path(__file__).resolve().parent / "fixtures/Shack_W2_Shingle.glb"
    expected = "4b85ad1698dc0bbd2f20a913f7811d9f81c1580b60dcaa2c0b014957c574e5cf"
    if hashlib.sha256(source.read_bytes()).hexdigest() != expected:
        parser.error("Hydrate the original shack GLB with Git LFS before running; its hash does not match.")
    if args.output:
        run = args.output.resolve()
        run.mkdir(parents=True, exist_ok=False)
    else:
        run = Path(tempfile.mkdtemp(prefix="marble-glb-validation-"))
    project = run / "project"
    results = run / "results"
    results.mkdir()
    env = dict(os.environ, MARBLE_GLB_SPIKE_RESULTS=str(results),
               MARBLE_GLB_SPIKE_DEPENDENCY=dependency, UPM_CACHE_ROOT=str(run / "upm-cache"))
    unity = str(args.unity.resolve())

    def execute(label, *arguments):
        command = [unity, "-batchmode", *map(str, arguments), "-logFile", str(run / f"{label}.log")]
        print(f"{label}: {run / (label + '.log')}", flush=True)
        with (run / f"{label}-stdout.log").open("w") as log:
            subprocess.run(command, env=env, stdout=log, stderr=subprocess.STDOUT, check=True)

    execute("create", "-createProject", project, "-quit")
    editor = project / "Assets/Editor"
    editor.mkdir(parents=True, exist_ok=True)
    scripts = Path(__file__).resolve().parent
    shutil.copy2(scripts / "PackageInstaller.cs", editor)
    if args.resolve_marble:
        # Other embedded packages are part of Marble's dependency configuration.
        for package_dir in packages.iterdir():
            if package_dir.is_dir() and (package_dir / "package.json").is_file():
                shutil.copytree(package_dir, project / "Packages" / package_dir.name)
        for name in ("manifest.json", "packages-lock.json"):
            shutil.copy2(packages / name, project / "Packages" / name)
    # UPM is asynchronous. This method exits on completion, so do not pass -quit.
    method = "InstallMarbleDependency" if args.resolve_marble else "Install"
    execute("packages", "-projectPath", project, "-executeMethod", "Marble.GlbImporterSpike.PackageInstaller." + method)
    resolved_manifest = json.loads((project / "Packages/manifest.json").read_text())
    resolved_lock = json.loads((project / "Packages/packages-lock.json").read_text())
    entry = resolved_lock["dependencies"][package_name]
    if (resolved_manifest["dependencies"][package_name] != dependency or entry["source"] != "git"
            or entry["version"] != dependency or entry["hash"] != dependency.rsplit("#", 1)[1]):
        raise RuntimeError(f"Resolved package differs from the requested Git pin: {entry}")
    (results / "package-resolution.json").write_text(json.dumps(entry, indent=2) + "\n")
    if args.resolve_marble:
        expected_dependencies = dict(manifest["dependencies"], **{package_name: dependency})
        if resolved_manifest["dependencies"] != expected_dependencies:
            raise RuntimeError("UPM changed unrelated manifest dependencies.")
        old_lock = json.loads((packages / "packages-lock.json").read_text())["dependencies"]
        new_lock = resolved_lock["dependencies"]
        if ({k: v for k, v in old_lock.items() if k != package_name}
                != {k: v for k, v in new_lock.items() if k != package_name}):
            raise RuntimeError("UPM changed unrelated locked dependencies; inspect the generated files before continuing.")
        for name in ("manifest.json", "packages-lock.json"):
            shutil.copy2(project / "Packages" / name, packages / name)
        print(f"PASS. Marble package configuration resolved through UPM. Evidence: {results}")
        return
    for name in ("Validation.cs", "EdgeCases.cs"):
        shutil.copy2(scripts / name, editor)
    destination = project / fixture
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, destination)
    shutil.copy2(str(source) + ".meta", str(destination) + ".meta")
    for label, target, method in (
        ("ios", "iOS", "Validation.RunIos"),
        ("desktop", "StandaloneOSX", "Validation.RunDesktop"),
        ("ios-return", "iOS", "Validation.RunIosReturn"),
        ("edges", "iOS", "EdgeCases.Run"),
    ):
        execute(label, "-projectPath", project, "-buildTarget", target,
                "-executeMethod", "Marble.GlbImporterSpike." + method, "-quit")
    print(f"PASS. Results: {results}")


if __name__ == "__main__":
    main()
