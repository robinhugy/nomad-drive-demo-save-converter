# Nomad Drive Save Converter

Készítő: **robinhugy** · Verzió: **2.0.1**

Demo mentések átvitele a Nomad Drive Early Access 1.0-ba, multiplayer bekapcsolása és a játékosszám módosítása kiválasztott mentéseken. A program és a jelentések angol nyelvűek. Windows 10/11 x64 és .NET Framework 4.8 szükséges; a kiadás egyetlen önálló EXE.

## Használat

1. Ments, majd zárd be teljesen a játékot. Indítsd a `NomadDriveSaveConverter-v2.0.1-win-x64.exe` fájlt.
2. A **Find / refresh saves** kilistázza a jelenlegi Windows-felhasználó demo- és Early Access-mentéseit. Másik forráshoz válaszd a **Custom folder** lehetőséget vagy a **Browse** gombot.
3. A **Select** oszlopban csak a kívánt világok melletti jelölőnégyzetet pipáld ki. A sor kiemelése önmagában nem választja ki a mentést. A **Set action** csak a pipás sorok műveletét állítja be, fájlt nem másol. Ha már játszottál egy átalakított világban, annak legfrissebb példányát válaszd a régi eredeti helyett.
4. Mentésenként válassz műveletet az **Action** oszlopban. Több kijelölt sorra a **For checked worlds** és **Set action** is használható.
5. Ellenőrizd a cél mentésmappát (**Destination saves**) és a teljes játék mappáját (**Early Access game**). Egyedi telepítésnél használd a **Browse** gombot.
6. **Preview selected**: ellenőrzés és előnézet, fájlmódosítás nélkül. Sikeres előnézet után aktiválódik a **backup and create copies** gomb: biztonsági mentést és új világokat kizárólag a kipipált mentésekből készít. Hiba esetén javítsd a jelzett problémát, majd indíts új előnézetet.
7. Az Early Accessben a `(converted)` nevű új világokat töltsd be. Az eredetiket tartsd meg, amíg a betöltést, mentést és újratöltést is kipróbáltad.

Induláskor semmi nincs kijelölve és semmit nem alakít át automatikusan. Minden eredmény külön mentés, az eredetiket nem írja felül.

| Művelet | Hatás |
| --- | --- |
| Copy to Early Access | Átvitel, a jelenlegi single / multiplayer mód és létszám megtartásával. Az ősi, 5-ös formátumnál a mentett játékosokból következtet a létszámra. |
| Enable multiplayer | Legalább 2 fő; a már meglévő magasabb korlát megmarad. |
| Set player limit | A **New limit** értéke: 1 = single player; 2–8 = multiplayer. Növelhető és csökkenthető. |

A **New limit** csak a **Set player limit** műveletnél szerkeszthető. Csökkentéskor a korábban csatlakozott játékosok mentett profilja, felszerelése és haladása megmarad. Egy adagban különböző műveleteket és létszámokat is választhatsz. A játék saját kapcsolódási korlátai továbbra is érvényesek.

## Mentések és kompatibilitás

A program a `%USERPROFILE%\AppData\LocalLow\Indie Devil` alatt keresi a `Nomad Drive Demo\EvilSave` és `Nomad Drive\EvilSave` mappát. A Steam könyvtárait is ellenőrzi a játék megtalálásához. Más Windows-felhasználók profilját és a teljes meghajtókat nem vizsgálja.

Biztonsági mentések: `%LOCALAPPDATA%\NomadDriveSaveConverter\Backups`. A kiválasztott világ legfrissebb `save.dat` / `autosave.dat` fájlja, metaadatai és meglévő jelentései kerülnek mentésre. Az új világ saját részletes jelentést kap.

Támogatott: régi demo 5-ös formátum; jelenlegi demo / Early Access 16-os formátum; cél: Early Access 1.0, 2026. október 31-én ellenőrzött tartalomkészlettel. Az aktuális formátum egyszerű másolásakor a mentésfájl bájtról bájtra megmarad. Ismeretlen céljáték-verziónál a program megáll és frissített kompatibilitási adatok szükségesek.

Az opcionális **Clean old imported world loot** eltávolíthatja a régi épületek helyén lebegő lootot. Alapból kikapcsolt; az eredeti, pontosan azonosítható régi mentés kell hozzá. A járműveket, kapcsolt rakományt, játékosfelszerelést és hátizsákokat védi, de a külön földre rakott régi készleteket is törölheti. Olvasd el az előnézetet.

A régi terep és épületek elhelyezkedése változhat. Egyes régi küldetés- és kilométeradatok archiválva maradnak; a régi festéköregedés nem vihető át. A többfelületű festést játékban ellenőrizd. A fájlellenőrzés nem helyettesíti a játékbeli próbát.

## Fordítás

Windows, .NET Framework 4.8 és Python 3.11 szükséges:

```powershell
.\scripts\build.ps1 -Python python
```

A `dist` mappába kerül az EXE, a kiadási ZIP és a teljes forráscsomag. A fordítás lefuttatja a régi formátum tesztjeit a 21 kiválasztási tesztet és a 18 felületi munkafolyamat-ellenőrzést. A részletes technikai leírás az angol `README.md` fájlban található.

## Vírusirtó-ellenőrzés

Korábbi kiadásoknál voltak vírusirtói találatok. Ez a kiadás közvetlen .NET EXE. VirusTotal-hivatkozáshoz az adott kiadás pontos fájlját és SHA-256 azonosítóját használd; egy korábbi verzió vizsgálata másik fájlra vonatkozik.

MIT licenc, lásd `LICENSE`. Nem hivatalos közösségi eszköz.
