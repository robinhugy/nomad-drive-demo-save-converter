# Nomad Drive Save Converter v1.3.0

Készítő: **robinhugy**

A konverter most közvetlenül egyetlen .NET Framework EXE-ként fut. A kiadásból kikerült a Python-futtatókörnyezet, a PyInstaller-csomag, a beágyazott motor, annak kicsomagolása és a külön konvertáló folyamat.

- Megmarad az angol felület, a lakóautós-floppys ikon és az automatikus keresés.
- Megmarad a backup, a külön `(converted)` mentés, a tárgycserék listája, a többjátékos javítás és a külön kapcsolható régi-loot-takarítás.
- A program visszaolvassa és ellenőrzi a teljes elkészített mentést.
- Aktív művelet közben az ablak bezárása megvárja a befejezést.
- A meghajtó-átirányításokat feloldja a forrás és cél biztonsági ellenőrzése előtt.
- A fordításhoz nincs külső Python-csomag; Python csak a teszteléshez és a forráscsomag elkészítéséhez kell.

**Vírusirtói állapot:** a korábbi kiadásokra volt jelzés. Az új felépítés megszünteti a korábbi beágyazott motort, de gyártói jóváhagyás még nincs. Az új EXE-hez új, a pontos SHA-256-hoz tartozó vizsgálat kell. Ez nem állítás arról, hogy minden korábbi jelzés téves, vagy az új fájlra egyetlen motor sem fog jelezni.

Kompatibilitás: **5 → 16** világformátum, **26274244** Steam build. A már átalakított mentéseket emiatt nem kell ismét konvertálni. A régi-loot-takarítás alapból kikapcsolt marad.

Konvertálás előtt ments és zárd be a játékot. A program backupot és külön új mentést készít. Biztonsági másolatok: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`.

A pályagenerálás, a többfelületű festés és a régi küldetés/kilométeróra korlátai megmaradnak; részletek a README-ben. Az elkészített másolatot játékban is ellenőrizni kell.
