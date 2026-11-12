"""Build the Event Instruction Upgrade's Reloaded-II mod folder, and optionally check, install and zip it.

    python -I build.py --reloaded C:/Reloaded-II                      # build out/fftivc.utility.eventinstructionupgrade
    python -I build.py --reloaded C:/Reloaded-II --exe "C:/.../FFT_enhanced.exe"   # and run the offline check
    python -I build.py --reloaded C:/Reloaded-II --install --zip      # and copy it into Reloaded-II's Mods, and zip it

Needs Python 3.11+ (standard library only), the .NET 9 SDK, and a Reloaded-II install with the shared hooks mod
(reloaded.sharedlib.hooks, which the FFT mod loader also needs): the code builds against Reloaded-II's interfaces.
--reloaded defaults to the RELOADEDII environment variable.
"""

import argparse
import json
import os
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parent
OUT = ROOT / "out"
CHECK_PROJECT = ROOT / "src" / "EventInstructionUpgrade.Check" / "EventInstructionUpgrade.Check.csproj"  # builds all
MOD_CONFIG = ROOT / "mod" / "ModConfig.json"
# What goes in the mod's folder: the upgrade, the library it's built on and its API (Reloaded-II shares this copy of
# the API with the mods that use it).
MOD_FILES = ["EventInstructionUpgrade.dll", "EventInstructionUpgrade.deps.json", "EventInstructionUpgrade.pdb",
             "IvaliceCore.dll", "IvaliceCore.pdb",
             "EventInstructionUpgrade.Interfaces.dll", "EventInstructionUpgrade.Interfaces.pdb"]
# The upgrade's word variables (0xB00-0xB7F) and text values start as the 0x400 bytes at PS1 0x80165EF4 in the game's
# BATTLE.BIN; that's game data, so it isn't in this repo. Without the file they start at 0.
WORDS_FILE = "eiu-battle-words.bin"


def mod_id() -> str:
    return json.loads(MOD_CONFIG.read_text(encoding="utf-8"))["ModId"]


def build(reloaded: Path) -> Path:
    """Compile everything into out/bin; returns it."""
    dotnet = shutil.which("dotnet")
    if not dotnet:
        sys.exit("No .NET SDK (dotnet) found: install the .NET 9 SDK.")
    if not (reloaded / "Loader" / "X64" / "Reloaded.Mod.Interfaces.dll").exists():
        sys.exit(f"{reloaded} isn't a Reloaded-II folder (no Loader/X64/Reloaded.Mod.Interfaces.dll).")
    if not (reloaded / "Mods" / "reloaded.sharedlib.hooks").is_dir():
        sys.exit("Install Reloaded-II's shared hooks mod (reloaded.sharedlib.hooks) first: the code builds against it.")
    bin_dir = OUT / "bin"
    shutil.rmtree(bin_dir, ignore_errors=True)
    run = subprocess.run([dotnet, "build", str(CHECK_PROJECT), "-c", "Release", "-o", str(bin_dir),
                          f"-p:ReloadedDir={reloaded}", f"-p:ReloadedModsDir={reloaded / 'Mods'}", "--nologo", "-v", "q"],
                         capture_output=True, text=True)
    if run.returncode:
        sys.exit(f"The build failed:\n{run.stdout}{run.stderr}")
    return bin_dir


def assemble(bin_dir: Path, words: Path | None) -> Path:
    """The mod's folder, out/<ModId>, from the build and mod/ModConfig.json."""
    folder = OUT / mod_id()
    shutil.rmtree(folder, ignore_errors=True)
    folder.mkdir(parents=True)
    for name in MOD_FILES:
        shutil.copy2(bin_dir / name, folder / name)
    shutil.copy2(MOD_CONFIG, folder / "ModConfig.json")
    if words:
        data = words.read_bytes()
        if len(data) != 0x400:
            sys.exit(f"{words} should be 0x400 bytes (BATTLE.BIN's at PS1 0x80165EF4); it's {len(data):#x}")
        (folder / WORDS_FILE).write_bytes(data)
    return folder


def check(bin_dir: Path, exe: Path) -> None:
    """The offline check against the exe on disk (the game isn't started)."""
    run = subprocess.run([str(bin_dir / "EventInstructionUpgrade.Check.exe"), str(exe), "live-hooks"],
                         capture_output=True, text=True)
    print(run.stdout.rstrip())
    if run.returncode:
        sys.exit(f"The offline check failed against {exe}:\n{run.stderr}")


def install(folder: Path, mods: Path) -> Path:
    """Copy the folder over Reloaded-II's installed copy, removing files it no longer has (in place, as Reloaded-II
    may hold the folder open)."""
    dest = mods / folder.name
    dest.mkdir(parents=True, exist_ok=True)
    for old in sorted(dest.rglob("*"), reverse=True):
        if old.is_file() and not (folder / old.relative_to(dest)).exists():
            old.unlink()
    for new in folder.rglob("*"):
        if new.is_file():
            target = dest / new.relative_to(folder)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(new, target)
    return dest


def zip_folder(folder: Path) -> Path:
    """out/<ModId>.zip with the folder at its top, as Reloaded-II installs a dropped archive."""
    archive = OUT / f"{folder.name}.zip"
    with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
        for file in sorted(folder.rglob("*")):
            if file.is_file():
                z.write(file, Path(folder.name) / file.relative_to(folder))
    return archive


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--reloaded", type=Path, default=os.environ.get("RELOADEDII"),
                   help="your Reloaded-II folder (default: the RELOADEDII environment variable)")
    p.add_argument("--exe", type=Path, help="FFT_enhanced.exe, to run the offline check against")
    p.add_argument("--words", type=Path, help=f"the 0x400 bytes for {WORDS_FILE} (see the README)")
    p.add_argument("--install", action="store_true", help="copy the mod into Reloaded-II's Mods folder")
    p.add_argument("--zip", action="store_true", help="also write out/<ModId>.zip")
    args = p.parse_args()
    if not args.reloaded:
        sys.exit("Give --reloaded (your Reloaded-II folder) or set RELOADEDII.")
    bin_dir = build(args.reloaded)
    folder = assemble(bin_dir, args.words)
    print(f"Built {folder.relative_to(ROOT)}")
    if args.exe:
        check(bin_dir, args.exe)
    if args.install:
        print(f"Installed to {install(folder, args.reloaded / 'Mods')}")
    if args.zip:
        print(f"Wrote {zip_folder(folder).relative_to(ROOT)}")


if __name__ == "__main__":
    main()
