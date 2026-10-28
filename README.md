# Nomad Drive Save Converter

Author: **robinhugy**

An unofficial Windows tool that converts legacy **Nomad Drive Demo** worlds to the updated save format and fixes the saved player limit that can block multiplayer.

**Run the EXE: it finds your saves, makes backups and converts all eligible worlds automatically.** New worlds are named **`(converted)`**. Existing slots are never overwritten.

The app and its reports are in English.

## Download and run

1. Download **`NomadDriveSaveConverter-v1.2.1-win-x64.exe`** from the repository's **Releases** section, or extract the Windows release ZIP.
2. Save and close the game, then run the EXE from a writable folder.
3. The app detects the save folder and Steam libraries, creates backups, and processes all eligible worlds. Read its completion report.
4. Start the game and select the newest **`(converted)`** entries in **Load Game**.

The EXE is self-contained: no separate engine file, Python installation or installer is needed. Windows 10/11 x64 with .NET Framework 4.8 is supported. The engine is extracted to the app's own local runtime cache on first use. License texts are available through **Licenses**.

If the game is open, writing stops until you close it and click **Back up and convert all**. If detection cannot find an unusual installation, use **Browse**. Clicking a world also gives access to a single-world preview, custom name and player limit.

## Supported game version

| Item | Supported value |
| --- | --- |
| Game | Nomad Drive Demo |
| Legacy world format | 5 |
| Target world format | 16 |
| Inspected Steam build | 26274244 |
| Current-format action | Multiplayer repair and opt-in legacy-loot cleanup in a separate copy |

Version 1.2.1 checks the 28 October 2026 update. Existing format-16 worlds do not need reconversion solely for this update.

The app checks the installed game's catalog fingerprint before processing saves. Other game updates need an updated compatibility mapping; changing the fingerprint alone is not sufficient.

## Automatic processing

- Finds saves in the current Windows user's `AppData\LocalLow\Indie Devil\Nomad Drive Demo\EvilSave` folder. It reads Steam's configured libraries to locate the game.
- Uses the newest `save.dat` or `autosave.dat` for each world. Historical files in a world's `backup` folder are not turned into extra worlds.
- Backs up the selected source file, slot metadata and thumbnail before committing a new slot.
- Converts all supported legacy worlds, including multiplayer player records and vehicle attachment links.
- Matches original-file fingerprints to their converted descendants. Repeated runs skip suitable results instead of creating duplicates.
- If an older converter result needs multiplayer repair, uses its most recently saved state rather than recreating the world from the old source.
- Leaves native format-16 worlds without a converter report alone. A selected format-16 world can be repaired manually in a new copy.
- Reports damaged or unsupported saves individually and continues with other eligible worlds.

The app does not search other Windows users' profiles or scan every file on every drive.

### Multiplayer

The updated game stores a player limit in the world settings. A value of `1` makes the game refuse **Activate Multiplayer**, even when remote player records survived conversion.

**Auto (multiplayer)** selects at least two player slots and enough slots for the host plus saved remote players, up to eight. Existing higher limits are retained during repair. You can choose a different limit in **Player slots**; the app rejects a limit smaller than the saved player count. The game's own connection limit still applies.

Current-format repair changes only the player-limit byte in the world manifest. Object state, player progress, paint images and resource density settings remain unchanged. Repair creates a copy and a backup.

### Floating legacy loot after changed world generation

The updated game regenerates terrain and buildings differently. A full conversion can leave old building loot floating at the original coordinates. Version 1.2 adds an **optional** cleanup; it is **off by default** because the old format cannot reliably distinguish untouched loot from intentional ground stashes.

1. Save and close the game. Select the latest converted world, rather than the original if you have continued playing.
2. Enable **Clean old world loot**. Read the preview's removal counts, item list and limitations.
3. Click **Create cleaned copy**, or use **Back up and convert all** to clean the latest converted descendant of each legacy world. Load the newest `(converted)` entry afterward.

Cleanup preserves all vehicles, linked parts and cargo, host/remote held and hotbar items, worn backpacks and their contents, all objects created after conversion, and imported objects moved over one metre since the original save. Linked objects are kept together. It removes the remaining imported loose components, **including ground stashes and their contents**. Do not use this mode if those stashes must survive.

Old-generation loot reservations are reset, while new reservations and live native loot markers are retained. This lets the updated world spawn loot at its own locations; previously cleared locations can receive new loot. Protected objects or old objects already moved by physics may remain floating. This is a conservative migration cleanup, not a perfect detector of airborne objects, and it does not recreate old buildings.

For an existing conversion, the exact format-5 original must still exist in a sibling slot or the converter's backup folder. Its SHA-256 must match the conversion report. The developer CLI also accepts `--legacy-source`. If the original is missing or a link schema is unsupported, cleanup stops. Native format-16 worlds without converter lineage are never swept. The latest save/autosave is used; kept records, player progress and sidecar images are preserved.

Cleanup is recorded in each new slot's report, including every removed GUID, item name and position. Repeating a batch with cleanup enabled skips already-cleaned results. Ordinary startup conversion still leaves all transferable objects in place.

### Backups and reports

Backups: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`

Runtime cache: `%LOCALAPPDATA%\NomadDriveSaveConverter\Runtime`

Each new world contains `CONVERSION_REPORT.txt` and `conversion-report.json`, including replacements, omissions, player slots and verification results. Batch reports are stored with the backups when worlds are created. All processing is local; the app has no upload or telemetry feature.

## What is preserved

World seed and time, player poses and survival state, supported objects and their identifiers, vehicle attachment links, component condition, liquids, food, plants, and compatible restoration images are carried over. Known optional trainer state is retained when present; the trainer itself is not included or required for ordinary saves.

Removed assets can be replaced with explicit alternatives, such as an off-road tire with a regular tire. Unavailable assets without a replacement are omitted and listed. Turn off **Replace removed items** to omit all removed assets instead.

## Known limitations

- Updated terrain, buildings and model dimensions can differ at the old coordinates. Inspect the converted world, save, exit, and reload it before continuing a long session.
- Placed objects are linked to a current plane on the same container while retaining their saved world pose. Moving supports and changed models may need manual adjustment.
- Paint mapping for multi-surface bodies follows the current prefab hierarchy and is provisional. One old RV-Hippie surface no longer exists. Unmapped image bytes remain in the original backup and archived sidecars.
- Legacy quests and the old shared odometer are archived; the new quest and per-vehicle odometer systems start from defaults. Old paint aging is no longer supported. New hotbar/backpack fields begin empty.
- The compatibility check targets one inspected Steam build. This is not a general save editor or a guarantee for future updates.

File compatibility was checked with the actual game's envelope and manifest readers on three legacy worlds. Initial in-game loading and multiplayer activation have also been reported working. That is a smoke check, not exhaustive testing of every item or multiplayer combination.

## Build and test

The source requires Python 3.11+; the Windows executable is built with 64-bit Python 3.11, PyInstaller and the Windows .NET Framework C# compiler. Game assemblies are not needed to build or run the synthetic tests.

```powershell
python -m pip install -r requirements-build.txt
python -m unittest discover -s tests -v
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Python python
```

Build outputs are placed in `dist/`:

- Standalone Windows EXE.
- Release ZIP containing the EXE, README, release notes, license texts and checksums.
- `SHA256SUMS.txt` for the release assets.

The build script embeds both the conversion engine and license notices in the final EXE. It never copies game files or personal saves into a release. The GitHub Actions workflow runs the same synthetic tests and Windows build, then provides downloadable build artifacts; publishing a GitHub Release remains a manual step.

### Developer CLI

```powershell
python src\engine\converter_engine.py --list
python src\engine\converter_engine.py --batch
python src\engine\converter_engine.py --preview --source "PATH_TO_SLOT"
python src\engine\converter_engine.py --convert --source "PATH_TO_SLOT" --players 4
python src\engine\converter_engine.py --repair-multiplayer --source "PATH_TO_SLOT" --players 4
python src\engine\converter_engine.py --preview --source "PATH_TO_CONVERTED_SLOT" --clean-legacy-loot
python src\engine\converter_engine.py --batch --clean-legacy-loot
```

Use `--game`, `--output-root`, `--backup-root` or `--report` for explicit paths. The GUI EXE forwards the same arguments to its embedded engine; use `--report result.json` to retrieve output from the windowed executable.

## Repository contents

`src/engine` contains the Python save parser, conversion/repair logic and compatibility metadata. `src/gui` contains the English Windows Forms interface and embedded-engine loader. `tests` uses generated synthetic worlds, and `scripts/build.ps1` builds the release.

Application code and documentation are under the [MIT license](LICENSE). Runtime component license texts are included in releases and accessible in the EXE. Game names and compatibility identifiers belong to their respective owners. This project is not affiliated with the game developer and distributes no game assemblies, game media, or personal saves.
