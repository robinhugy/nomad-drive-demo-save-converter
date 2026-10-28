# Nomad Drive Save Converter v1.2.1

Author: **robinhugy**

Standalone Windows x64 release with an English interface.

This compatibility update supports Steam build **26274244** (28 October 2026). The world manifest, inspected save-contributor methods, loot identities, paint surface IDs, placement planes and plant phase durations are unchanged from the previously inspected build. The catalog fingerprint is updated after checking the actual game code and assets. Existing format-16 saves do **not** need reconversion for this update. Optional legacy-loot cleanup keeps the same safeguards.

- Adds opt-in **Clean old world loot** to remove imported floating leftovers after changed map generation.
- Protects vehicles and their linked cargo/parts, all players' carried equipment/backpack contents, current-only objects and moved imports.
- Uses the latest converted progress, an exact original-save fingerprint, backup copies, removal previews and detailed reports.
- Resets old loot reservations while keeping current-world reservations and live native markers. Repeated cleanup batches skip completed worlds.
- Cleanup can remove intentional loose ground stashes; it is off by default. Protected/moved floating items may remain.
- Finds the current user's Nomad Drive Demo saves and the game in Steam libraries.
- Automatically backs up and converts all supported legacy worlds on launch.
- Uses the `(converted)` suffix and skips suitable existing conversions on repeated runs.
- Fixes the player-limit setting that incorrectly marked converted co-op worlds as single-player.
- Selects enough player slots for the saved players, with a manual override.
- Repairs older converter output from its latest progress in a separate copy.
- Includes item replacement/omission reports, checksum/read-back checks and embedded license texts.
- Builds as one EXE: no separate engine, Python installation or installer required.

Compatibility: legacy world format **5** to **16**, inspected Steam build **26274244**. Native format-16 worlds are not changed by the automatic batch; they can be selected for manual multiplayer repair.

Save and close the game before starting the tool. Load the newest `(converted)` worlds afterward. Backups are stored under `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`.

Known limitations remain: changed terrain/model geometry, provisional multi-surface paint mapping, omitted unavailable assets, and reset legacy quest/shared-odometer systems. See README for details.

Download the **win-x64 EXE** for the standalone app, or the **win-x64 ZIP** for the app plus documentation and license files. `SHA256SUMS.txt` contains the asset checksums. GitHub's automatic Source code archives contain source rather than the runnable app.
