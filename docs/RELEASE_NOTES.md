# Nomad Drive Save Converter v1.3.0

Author: **robinhugy**

The save converter now runs directly as a single .NET Framework Windows application. The Python runtime, PyInstaller package, embedded executable engine, runtime extraction cache and converter subprocess have been removed from the release.

- Keeps the English interface, motorhome-and-floppy icon and automatic save/game discovery.
- Keeps backup copies, separate `(converted)` slots, item replacement reports, multiplayer repair and optional old-loot cleanup.
- Preserves compatibility checks and complete save/manifest read-back verification.
- Defers closing the window until the active operation finishes.
- Resolves redirected filesystem paths before checking source/destination boundaries.
- Builds without third-party Python packages. Python is used only for tests and source packaging, including independent decoded-save comparisons.

**Antivirus status:** earlier packaged releases received detections. This release removes their embedded executable architecture, but has not received a vendor verdict. A fresh scan must refer to this EXE's exact SHA-256. No claim of confirmed false positives or zero detections is made.

Compatibility: legacy format **5** to **16**, inspected Steam build **26274244**. Already converted worlds do not require another conversion for this release. Automatic conversion leaves suitable existing worlds alone; optional cleanup remains off by default.

Save and close the game before converting. Original slots are backed up and never overwritten. Load the newest `(converted)` copy afterward. Backups: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`.

Known world-generation, multi-surface paint and legacy quest/odometer limitations remain; see README. File checks do not replace an in-game load/save/reload check.

The **win-x64 EXE** is the standalone application. The **win-x64 ZIP** contains that same EXE plus documentation, license notices and checksums. Source archives contain the complete buildable source. `SHA256SUMS.txt` identifies the release assets.
