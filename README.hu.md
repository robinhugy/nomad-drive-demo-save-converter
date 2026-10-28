# Nomad Drive Save Converter

Készítő: **robinhugy**

Nem hivatalos Windows-program a **Nomad Drive Demo** régi mentéseinek átalakításához, illetve a többjátékos módot tiltó játékoslétszám javításához. **A program felülete és jelentései angol nyelvűek.**

Indításkor megkeresi az aktuális Windows-felhasználó mentéseit és a Steam-könyvtárakban a játékot, biztonsági másolatot készít, majd átalakítja az összes megfelelő régi világot. Az új nevek végére **`(converted)`** kerül. Az eredeti mentéshelyeket nem írja felül.

## Használat

1. A **Releases** részből töltsd le a Windows EXE-t, vagy csomagold ki a release ZIP-et.
2. Ments és zárd be a játékot, majd indítsd el a `NomadDriveSaveConverter` EXE-t.
3. Az automatikus futás eredményét a program ablakában látod. Újraindításhoz: **Back up and convert all**.
4. A játék betöltésénél a legújabb **`(converted)`** nevű példányt válaszd.

A már megfelelően átalakított világokat kihagyja. Ha egy régebbi konverter eredményén kell javítani a többjátékos módot, annak legutóbbi állapotából készít új másolatot. A játék saját, eleve új formátumú mentéseit automatikusan nem módosítja.

Az 1.3.0-s kiadás megtartja a lakóautót és floppylemezt ábrázoló ikont. A konvertálást közvetlenül a programba fordított .NET-kód végzi: nincs beágyazott Python-futtatókörnyezet, kicsomagolt belső EXE vagy külön konvertáló folyamat. Folyamatban lévő művelet alatt az ablak bezárása várakozik.

Az EXE önállóan fut, külön Python vagy telepítő nélkül. Windows 10/11 x64 és .NET Framework 4.8 szükséges. Szokatlan telepítési hely esetén a **Browse** gombbal lehet mappát választani. A kiválasztott világ külön előnézete és beállításai is elérhetők.

## Lebegő régi loot eltakarítása

A frissített pályagenerálás más helyekre teheti az épületeket, miközben a konverter által megőrzött régi tárgyak a korábbi helyükön lebegnek. Az 1.2-es verzióban erre külön, alapból kikapcsolt takarítás van.

1. Ments és lépj ki a játékból. Válaszd a legfrissebb konvertált világot.
2. Kapcsold be a **Clean old world loot** opciót, és olvasd el az előnézetet.
3. **Create cleaned copy**: a kiválasztott világ takarított másolata. **Back up and convert all**: minden régi világ legújabb konvertált példányának takarítása. A játékban a legújabb `(converted)` mentést töltsd be.

Megmarad minden autó, a hozzájuk kapcsolt alkatrészek és rakomány, a játékosok kezében/hotbarjában lévő felszerelés, a viselt hátizsákok tartalma, a konvertálás után létrejött tárgyak és a régi állapothoz képest több mint egy métert elmozdult tárgyak. Az összekapcsolt tárgyakat együtt tartja meg.

**A többi régi, különálló földi tárgyat és kapcsolt tartalmát törölheti, szándékosan lerakott készleteket is.** A régi mentésből nem állapítható meg biztosan, mi volt érintetlen loot. Ha földi készletet vagy bázist is meg kell tartani, ezt a módot ne használd.

A régi loot foglalásait felszabadítja, az újakat megtartja; az új pálya saját helyein új loot keletkezhet, korábban kifosztott helyeken is. Védett vagy már elmozdult tárgyak maradhatnak lebegve. Régi épületeket nem állít vissza.

A korábbi konvertálás javításához szükséges a pontos eredeti, 5-ös formátumú mentés és a `conversion-report.json`. Az eredetit a többi mentés és a konverter backupmappája között keresi, ellenőrzőösszeg alapján. Hiányzó eredetinél leáll, találgatás helyett. A törölt tárgyak listája az új mentés jelentésébe kerül. Az alap konvertálás és a program indulásakor végzett automatikus futás nem kapcsolja be ezt a takarítást.

## Többjátékos mód és biztonsági másolatok

Az **Auto (multiplayer)** beállítás legalább két játékoshelyet ad, és figyelembe veszi a mentett résztvevőket. Más létszám a **Player slots** mezőben választható. A játék saját csatlakozási korlátja továbbra is érvényes.

Biztonsági másolatok: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`

Az új mentés melletti `CONVERSION_REPORT.txt` felsorolja a cseréket, kihagyásokat és ellenőrzéseket. A program helyben dolgozik, nincs benne feltöltés vagy telemetria.

A 2026. október 28-i frissítés továbbra is 16-os mentésformátumot használ. A már konvertált világokat emiatt nem kell újra átalakítani.

## Kompatibilitás és korlátok

- A korábbi kiadásokra több vírusirtó jelzett, köztük a COMODO a belső motorra. Az 1.3.0 más felépítést használ, nincs benne külön EXE vagy Python-futtatókörnyezet. Ez nem vírusirtói jóváhagyás: az új fájlt külön kell vizsgálni, gyártói eredmény még nincs. Mindig az adott kiadás ellenőrzőösszegéhez tartozó jelentést tedd közzé.

- **5-ös világformátum → 16-os világformátum**, a **26274244** Steam buildhez.
- A megszűnt tárgyak egy részét hasonló tárggyal helyettesíti, a többit listázva kihagyja.
- A megváltozott terep és modellek, valamint a több felületből álló kasztni festése miatt lehet szükség kézi ellenőrzésre.
- A régi küldetések és közös kilométeróra adatai archiválva maradnak, az új rendszerek alaphelyzetből indulnak.
- Az 1.3.0 három valódi világ konvertált és takarított másolatán átment a játék saját fájlolvasóján. A kezdeti játékbeli betöltést és a többjátékos aktiválást a korábbi motorral igazolták vissza; az átírt változatnál még szükséges a játékbeli betöltés/mentés/újratöltés ellenőrzése.

A részletes fejlesztői és fordítási leírás az [angol README-ben](README.md) található.
