# 2.0.0

By **robinhugy**

- Move selected legacy or current-format Demo worlds to Nomad Drive Early Access 1.0.
- Search both current-user save profiles; choose individual worlds, actions and player limits.
- Enable multiplayer only on checked worlds, or set each selected world's cap from 1 to 8.
- Decrease caps while retaining every saved player profile and inventory.
- Read-only startup and previews: creating copies requires the explicit action button.
- Preserve current-format save bytes when no mode change is requested; keep progress, objects, paint and sidecars.
- Back up selected sources, create separate `(converted)` slots and refuse stale previews or unsupported targets.
- Retain optional legacy floating-loot cleanup and the motorhome/floppy icon.
- One standalone .NET Framework 4.8 EXE; author and application text remain English.

Validation: legacy regression suite, 53 independent decoded-save comparisons, 21 selection cases, interface checks, and 10 real-world copies accepted by the full game's native save/manifest readers. Seven current-format worlds were copied byte for byte. In-game load/save/reload remains a separate check.

Antivirus status: earlier releases received detections. Use a VirusTotal report matching this release's exact EXE and SHA-256; older reports refer to different files.

Close the game before creating copies. Load the new `(converted)` world afterward. Original saves remain unchanged. See README for compatibility, backup locations and world-generation limitations.
