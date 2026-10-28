# Nomad Drive Save Converter v1.2.1

Készítő: **robinhugy**

Önállóan futtatható Windows x64 kiadás, **angol kezelőfelülettel**.

Ez a kompatibilitási frissítés a 2026. október 28-i **26274244** Steam-buildet támogatja. A világ mentésformátuma, az ellenőrzött tárgyállapot-mentők, tárgyazonosítók, festési felületek, lerakási síkok és növényfázisok nem változtak. Az új játékfájlok és tárgymodellek ellenőrzése után frissült a kompatibilitási azonosító. A már 16-os formátumú mentéseket emiatt **nem kell újrakonvertálni**. A választható régi-loot takarítás változatlan védelemmel elérhető.

- Új, külön bekapcsolható **Clean old world loot**: a megváltozott pályagenerálás miatt lebegő régi loot maradványainak takarítása.
- Megőrzi az autókat, kapcsolt alkatrészeiket/rakományukat, minden játékos felszerelését és hátizsáktartalmát, az új tárgyakat és az elmozdult régi tárgyakat.
- A legutóbbi konvertált állapotból dolgozik, az eredeti mentést ellenőrzőösszeggel azonosítja. Előnézet, törlési lista és biztonsági másolat készül.
- A régi loot foglalásait felszabadítja, az újakat megtartja. Ismételt futáskor nem takarítja újra a kész példányokat.
- A takarítás szándékos földi készleteket is törölhet, ezért alapból ki van kapcsolva. Védett vagy már elmozdult tárgyak maradhatnak lebegve.
- Automatikusan megkeresi a mentéseket és a játék Steam-telepítését.
- Indításkor biztonsági másolatot készít, majd átalakítja az összes támogatott régi világot.
- Az új nevek végére `(converted)` kerül; ismételt futáskor a megfelelő példányokat kihagyja.
- Javítja azt a játékoslétszám-beállítást, amely miatt a többjátékos világok egyjátékosnak látszottak.
- Figyelembe veszi a mentett résztvevőket, és külön is választható játékoslétszám.
- A korábban konvertált mentések legutóbbi állapotából is készíthet javított másolatot.
- Listázza a tárgyhelyettesítéseket és kihagyásokat, ellenőrzi az új fájlt.
- Egyetlen EXE: külön Python, motorfájl vagy telepítő nem szükséges.

**Kompatibilitás:** 5-ös világformátum → 16-os világformátum, **26274244** Steam build. Az automatikus futás a játék saját, eleve 16-os formátumú mentéseit nem változtatja meg.

Használat előtt ments és zárd be a játékot. Utána a legújabb `(converted)` világokat töltsd be. A biztonsági másolatok helye: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`.

A terep és a modellek változása, valamint a több felületből álló kasztni festése miatt továbbra is kell játékbeli ellenőrzés. A régi küldetések és közös kilométeróra az új rendszerben alaphelyzetből indulnak. Részletek a README-ben.

Futtatható program: **win-x64 EXE**. Program dokumentációval és licencekkel: **win-x64 ZIP**. Ellenőrzőösszegek: `SHA256SUMS.txt`. A GitHub automatikus **Source code** csomagjai a forrást tartalmazzák.
