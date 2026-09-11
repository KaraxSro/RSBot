# Magic POP Bot Base – igény és megvalósítási terv

## Folytatási checkpoint – 2026-09-10

### Jelenlegi állapot

- Az implementáció a `master` branchen, még commitolatlan munkafaként található.
- A Magic POP botbase forrása: `Botbases\RSBot.MagicPop`.
- A projekt bekerült az `RSBot.sln` solutionbe, és `RSBot.MagicPop.dll` néven a `Build\Data\Bots` mappába fordul.
- A teljes `RSBot.sln` Release build sikeresen lefutott MSBuilddel. Csak már korábban is létező warningok maradtak; Magic POP fordítási hiba nincs.
- A friss Core, packet logger és Magic POP botbase települt a tényleges `Build` könyvtárba.
- A három útvonalscript verziózott forrása a `Dependencies\Scripts\MagicPop` mappában van. A normál build ezeket a `Build\Data\Scripts\MagicPop` mappába másolja; a forrás- és célfájlok SHA-256 hash-e egyezett.
- A `git diff --check` sikeres volt.
- Commit és push még nem történt.
- A 2026-09-10-i UI smoke teszteken három hiba derült ki: Trainingről Magic POP-ra váltva a botbase lemaradhatott a korábban lefutott `OnLoadGameData` eseményről; a középső transfergombokat a DPI-skálázás összenyomta; a referencia-loader tévesen `Visible=1` rekordokat várt.
- A session log igazolta, hogy a PK2, az `ItemData` (11908 rekord), a shopadatok és a Magic POP card package rendben betöltődtek. A közvetlen PK2-ellenőrzés szerint ezen a kliensen mind a 3344 aktív gacha rekord `Visible=0`, beleértve a capture-rel igazolt `RefItemID=4019`, `GachaID=6` 2D Sun swordot is. A lista ezért maradt üres.
- A javítások elkészültek és elkülönített Release kimenetre sikeresen lefordultak: a botbase inicializáláskor és karakterbetöltéskor is újrapróbálja a katalógust; a választható seteket a `gachanpcmap.txt` aktív Magic POP machine tartományaiból vezeti le; a `Visible` mezőt nem használja kizáró szűrésként, és a mappelt rekordok közül csak a támogatott equipment jutalmak kerülnek a felületre; a gombok fix méretet kaptak.
- A transferlisták alacsonyabbak lettek, alattuk saját, időbélyeges `Magic POP log` található Clear gombbal. Ez kiírja a referencia-parser darabszámait, a mapped seteket, a konkrét betöltési/Start hibát, az állapotváltásokat és a fontos futási eredményeket.
- A saját log első változatának induláskori cross-thread hibája javítva: a belső `RichTextBox` handle-je a létrehozó UI-szálon készül el, és minden későbbi naplóbejegyzés ennek a vezérlőnek az `InvokeRequired`/`BeginInvoke` útján kerül az UI-szálra.
- A Hotan return-point ellenőrzés első változata tévesen világkoordinátaként értelmezte az RBS `move` sorának első két mezőjét. Javítva lett a tényleges `sector 135/92 + offset 1326/377` pozícióra; eltéréskor a log már az aktuális és az elvárt koordinátát/régiót is kiírja.
- Ez a legutóbbi UI/reference/diagnosztikai javítás még nincs a futó RSBot folyamatba telepítve, mert az jelenleg zárolja a botbase DLL-t. Az RSBot bezárása után normál Release build szükséges.

### Elkészült működés

- Önálló `Magic POP` botbase és saját tab, Degree/kategória szűrőkkel.
- Többes kijelölésű transferlista és fel/le rendezhető prioritási queue.
- A jobb oldali lista az aktív, karakterprofilonként mentett célqueue. A bot mindig a legfelső célra rollol.
- A bal oldali lista állandó katalógus: hozzáadás után az item nem tűnik el. Ugyanaz a jutalom többször is felvehető a jobb oldali queue-ba; minden nyerés csak egyetlen, legelső megfelelő példányt távolít el.
- Fail után a cél a jobb oldali listában marad.
- Win után a cél csak akkor kerül ki a listából, ha a card slot nyertes kuponná alakult, és annak Gacha-metaadata tartalmazza a várt reward `RefItemID`-ját. A rövidebb queue azonnal mentődik.
- Látható aktuális cél, roll/win/lose és hátralévő célszámláló.
- PK2-alapú `gachaitemset.txt` és `gachanpcmap.txt` betöltés, szigorú validáció, valamint az aktív machine által mappelt `Service=1` equipment célok listázása. A `Visible` mező ezen a kliensen minden aktív rekordnál `0`, ezért nem kizáró feltétel.
- Capture-rel igazolt `0x7118`/`0xB118` roll, `0x3040` inventory-korreláció, `0x7034`/`0xB034` card purchase és `0x3153` Silk-követés.
- Egy időben csak egy fizetős művelet futhat. Purchase és roll csak a válasz plusz a hozzá tartozó inventoryváltozás után záródik le.
- Minimum három másodperces roll-intervallum és timeout/fail-closed kezelés.
- Automatikus kártyavásárlás az üres helyek feltöltéséig; elfogyott vagy ismeretlen Silk, elutasítás és timeout biztonságos kezelése.
- Machine megnyitása, prioritásos rollolás, kizárólag a vesztes zöld kuponok eladása, visszaút és folytatás.
- A nyertes piros kupon érintetlen marad; a bot nem küld `0x7119` beváltási packetet.
- Disconnect, teleport, karakterváltás és kézi Stop alatt biztonságos leállítás.
- Minden roll előtt lementett pending cél+slot. Újraindításkor ellenőrzött win/lose/card állapotból reconciliation történik; ismeretlen vagy ellentmondó eredménynél a bot nem indít új fizetős műveletet.
- Indulási ellenőrzések: karakter, használható return scroll, célqueue, referenciaadat, inventory és mindhárom script. A bot maga tér vissza Hotan return pontjára, majd a Magic POP műveletek előtt clientless módra vált. A korábbi Dry run mód a felhasználói döntés alapján kikerült.

### Innen kell folytatni

1. Indítsd el a frissen buildelt RSBotot, és ellenőrizd, hogy a `Bots` menüben megjelenik a `Magic POP` botbase, valamint a saját tabja.
2. Ellenőrizd a Degree/kategória szűrést, a transferlistát, az Up/Down sorrendet és a profilmentést.
3. Hotan return ponton, packet capture mellett válassz egyetlen célt, és csak kevés kártyával végezz korlátozott első éles tesztet.
4. Ellenőrizd, hogy a Race, Degree és Category szűrés a várt itemeket adja-e.
5. A log alapján ellenőrizni kell a purchase → útvonal → NPC handshake → roll → win/lose korrelációt és a cél automatikus eltűnését a jobb oldali listából.
6. Ezután következhet a zöldkupon-eladási és visszaút-teszt, majd a többcélos és megszakítás/helyreállítási teszt.
7. A még üres Task 0/1/2/4/10 checkboxokat csak a megfelelő capture vagy játékbeli teszt után szabad kipipálni.

Megjegyzés az inaktív vezérlőkhöz: karakter nélkül ez szándékos, központi RSBot-viselkedés. A főablak minden botbase nézetét letiltja, amíg `Game.Ready == false`, majd az `OnLoadCharacter` eseménynél engedélyezi. Ez nem a referenciaadat-hiba része.

### Fontos tesztbiztonság

- Az első éles teszt előtt a packet capture legyen bekapcsolva.
- Először egy cél és kevés kártya használható; teljes inventorynyi vásárlás csak az első sikeres kör után tesztelendő.
- A piros kuponokat kézzel se add el a helyreállítási teszt lezárásáig.
- Hiba esetén őrizd meg az Application logot és a Packet Capture JSONL fájlt.

## Megvalósítási munkalista

Ez a lista a tényleges végrehajtási sorrend és az aktuális állapot nyilvántartása. Egy feladat csak akkor kaphat pipát, ha a hozzá tartozó kód vagy dokumentáció elkészült, és az adott szinten érdemben ellenőrizve lett.

### Task 0 – Követelmények és protokollfeltárás

- [x] A Magic POP működésének és az első verzió határainak dokumentálása.
- [x] Döntés: önálló, a `Bots` menüből választható `Magic POP` bot base, saját tabbal.
- [x] Döntés: az első verzió a piros kuponokat nem váltja be; csak megőrzi őket és a zöld kuponokat adja el.
- [x] Kapcsolható, teljes packet capture elkészítése külön JSONL fájllal.
- [x] Item Mall card purchase, Silk update, NPC dialog, roll request/result és inventory-átalakulás packetjeinek igazolása.
- [x] Egy vesztes kupon eladási packetjének igazolása.
- [x] A tömeges reward exchange protokolljának dokumentálása későbbi opcionális funkcióhoz.
- [x] A packet logger legújabb UI-verziójának telepítése a `Build` könyvtárba, amikor a futó RSBot már nem zárolja a DLL-t.
- [ ] Egy második, ismert cél sikeres rolljával a win-kupon reward RefItemID mezőjének keresztellenőrzése. Ez hasznos, de nem blokkolja a referenciafájl-alapú implementációt.

### Task 1 – Bot base projekt és minimális integráció

> Aktuális checkpoint: a projektváz elkészült és célzott MSBuild builddel lefordult. A menübeli megjelenést és a váltási életciklust futó alkalmazásban még ellenőrizni kell.

- [x] `Botbases\RSBot.MagicPop` projekt létrehozása a Training/Alchemy mintájára.
- [x] A projekt felvétele az `RSBot.sln`-be és a standard buildfolyamatba.
- [x] `IBotbase` bootstrap, `Magic POP` cím, saját tab és üres biztonságos Start/Stop/Tick életciklus.
- [ ] Megjelenés ellenőrzése a jobb felső `Bots` menüben.
- [ ] Bot base-váltás, disconnect és karakterváltás alap leállítási viselkedése.

### Task 2 – Gacha referenciaadatok

- [x] `RefGachaItemSet` és `RefGachaNpcMap` típusos modellek.
- [x] `gachaitemset.txt` és `gachanpcmap.txt` betöltése a kliens PK2 referenciaadataiból.
- [x] Oszlopszám, típusok, `Service`, `Visible`, GachaID és RefItemID validációja.
- [x] A cél-itemek összekapcsolása `RefObjItem` rekordokkal.
- [x] Degree, CH/EU kategória, armor/weapon/accessory subtype és rarity leképezése.
- [x] Hibás vagy hiányzó referenciafájlok érthető naplózása és Start-tiltása.
- [ ] Parser- és kategorizálási tesztek valós mintasorokkal.

### Task 3 – Konfiguráció és Magic POP tab

> Aktuális checkpoint: az első működő UI elkészült degree/kategória filterrel, többes transfer listtel, fel/le prioritáskezeléssel és karakterprofilos queue-mentéssel. Drag & drop nincs; a rendezést a determinisztikus Up/Down gombok végzik.

- [x] Saját, programmatikusan felépített `Magic POP` tab létrehozása.
- [x] Degree- és kategóriaszűrők.
- [x] Elérhető itemek egyszerűsített listája Name és Subtype oszlopokkal; a degree, codename, rarity és GachaID nem jelenik meg, de szűréshez vagy belső azonosításra megmarad.
- [x] Külön `All / CH / EU` Race szűrő a Degree és Category mellett.
- [x] Külön rarity szűrő `All / Seal of Star / Seal of Moon / Seal of Sun / Seal of Nova` értékekkel; alapértelmezésben `Seal of Sun`.
- [x] Többes kijelöléses transfer list az elérhető és kiválasztott célok között.
- [x] Ismételhető célmennyiség: ugyanaz az item többször hozzáadható, miközben a bal oldali katalógus változatlan marad; egy win csak egy queue-elemet teljesít.
- [x] Prioritási lista fel/le rendezése és lehetőség szerint drag & drop.
- [x] Szűrőváltáskor a kiválasztott célok és sorrend megőrzése.
- [x] Karakterprofilonkénti, sorrendtartó konfigurációmentés.
- [x] Aktuális állapot, cél és futási számlálók megjelenítése.
- [x] Saját, időbélyeges diagnosztikai log a transferlisták alatt, kézi törléssel és visszafogott automatikus görgetéssel.

### Task 4 – Magic POP domain- és protokollréteg

> Aktuális checkpoint: a capture-rel igazolt request/response contract és az inventoryváltozással korrelált, egyszerre egy rollt engedő élő végrehajtás elkészült. Játékbeli smoke teszt még szükséges.

- [x] `MagicPopTarget`, futási állapotok és in-flight műveletmodell.
- [x] `0x7118` play request típusos előállítása: NPC UID + GachaID + card slot.
- [x] `0xB118` protocol result és win/lose outcome parser.
- [x] A kapcsolódó `0x3040` card-slot átalakulás korrelációja a roll requesttel.
- [x] Card, win és lose itemek referenciaadat-alapú felismerése, rögzített szerver-specifikus ID-kre építés nélkül.
- [x] Win/lose kupon Gacha-metaadatainak megőrzése az inventorymodellben.
- [x] Egyetlen in-flight request, timeout, késő válasz és ellentmondó response/inventory-diff kezelése.
- [ ] Valódi capture-ekből készített parser fixture tesztek.

### Task 5 – Indulási validáció

- [x] Induláskori return scroll használata, teleport start/complete követése és a Hotan return-pont utólagos ellenőrzése.
- [x] Játék-, karakter-, inventory-, referencia-, script- és célqueue-validáció.
- [x] Card/win/lose kuponok és szabad inventorykapacitás indulási összesítése.
- [x] A korábbi Dry run mód eltávolítása a felületről és az indítási folyamatból a felhasználói döntés alapján.

### Task 6 – Item Mall card purchase és Silk követés

- [x] `PACKAGE_ITEM_MALL_GACHA_CARD` biztonságos feloldása a referenciaadatból.
- [x] A capture-rel igazolt `0x7034` cash-item purchase request előállítása.
- [x] A játékbeli tesztben feltárt azonosítóhiba javítása: a request a `RefShopGroup` + shop/tab indexekből képzett wire shop ID-t (`0x0002041B`) és a `RefPackageItem` valódi package ID-ját (`0x000150FC`) küldi, nem a `RefShopTab.Id` és `RefScrapOfPackageItem.Id` értékeket.
- [x] A vásárlásnál és rollnál jelentkező kliensoldali `0xC0000005` crash kezelése: a return-scroll teleport és a Hotan return-pont ellenőrzése után a bot clientless módra vált, és csak ezután kezdi a fizetős Magic POP műveleteket.
- [x] A return-scroll befejezés felismerése a Training bothoz igazítva: az `OnTeleportComplete` az irányadó, mert return scrollnál nem garantált a külön `OnTeleportStart` esemény.
- [x] Jól látható előfeltétel-szöveg a Magic POP tab tetején: Hotan Return/recall point, használható return scroll, valamint a kötelező clientless végrehajtás és annak kliens-crash oka.
- [x] A nyertes cél queue-ból törlésekor jelentkező UI-deadlock javítása aszinkron UI-frissítéssel; a bot állapot-lockja és a targetmentés többé nem vár egymásra.
- [x] A felső előfeltétel-szöveg tördelése fix magasságú blokkban, hogy ne szélesítse túl a layoutot és a két transferlista ismét azonos szélességet kapjon.
- [x] Route-váltás előtt az aktív NPC-interakció szabályos lezárása (`0x704B` / `0xB04B`). Ez megszünteti a Magic POP → Potion első move parancsának `0xB021` timeoutját; a close hibája külön diagnosztikával, biztonságos leállással jár.
- [x] A Hotan potion NPC codename javítása `NPC_KT_POTION` értékre; részletes hibaüzenetek az NPC kereséséhez, kiválasztásához és az eladás visszaigazolásához.
- [x] A Magic POP log kétoszlopos (`Time`, `Message`), többsoros kijelölést, `Ctrl+C`-t és jobb klikkes Copy műveletet támogató táblázat lett. A `WIN` piros, a `LOSE` zöld kiemelést kap az itemek játékbeli színéhez igazítva.
- [x] A státuszsor külön jelzi a hátralévő célokat (`Remaining items`) és az inventoryban lévő Magic POP kártyákat (`Remaining cards`); a log `Time` oszlopa szélesebb alapméretet kapott.
- [x] A felső tájékoztató egyértelműen jelzi, hogy a return scroll után a kliens leválik, a bot pedig clientless módban folytatja.
- [x] Két sikeresen visszaigazolt vásárlás között legalább 100 ms várakozás.
- [x] Sikeres `0xB034` cash-item purchase response típusos feldolgozása.
- [x] A purchase response és a card inventoryba kerülésének in-flight korrelációja.
- [x] `0x3153` Silk-egyenleg típusos követése.
- [x] Üres slotok és megőrzött piros kuponok figyelembevételével biztonságos vásárlási mennyiség számítása.
- [x] Sikertelen vásárlás, timeout és ismeretlen Silk-állapot biztonságos kezelése.

### Task 7 – Rollolás és célkezelés

- [x] Magic POP machine kiválasztása és `MagicPopPlay` dialog handshake.
- [x] Prioritási lista első függő céljának rollolása.
- [x] Egy kártya / egy request / teljes response+inventory korreláció.
- [x] Vesztes kupon számlálása és folytatás a következő kártyával.
- [x] Nyertes kupon ellenőrzése, a cél teljesítése és eltávolítása az aktív queue-ból.
- [x] A piros kupon érintetlen megőrzése; az első verzióban `0x7119` soha nem küldhető.
- [x] Stop, elfogyott kártya, elfogyott Silk, inventorylimit és protokollhiba kezelése.

### Task 8 – Útvonalak és veszteskupon-takarítás

> Aktuális checkpoint: a felhasználó által elkészített három script változatlan tartalommal bekerült a `Dependencies\Scripts\MagicPop` forrásmappába. Az alkalmazás meglévő `CopyDependencies` build targetje mindhármat a `Build\Data\Scripts\MagicPop` mappába másolja; ezt SHA-256 egyezéssel ellenőriztük. Az útvonalak végpontjainak játékbeli ellenőrzése még hátravan.

- [x] `HotanTeleportToMagicPop.rbs` meglétének és célpontjának ellenőrzése.
- [x] `HotanMagicPopToPotion.rbs` meglétének és célpontjának ellenőrzése.
- [x] `HotanPotionToMagicPop.rbs` meglétének és célpontjának ellenőrzése.
- [x] A scriptek hozzáadása a megfelelő dependency/build mappába.
- [x] Kizárólag referenciaadat alapján felismert vesztes kuponok célzott eladása; a capture-ben látott RefItemID `9240` csak szerver-specifikus ellenőrző adat.
- [x] Minden eladási request `0xB034` visszaigazolásának megvárása.
- [x] Takarítás után visszatérés és a függő célok folytatása.
- [x] Utolsó takarítás minden cél teljesülése vagy a Silk+kártya elfogyása után.

### Task 9 – Állapotgép, helyreállítás és naplózás

- [x] A dokumentált állapotgép teljes bekötése a bot base `Tick` ciklusába.
- [x] Állapotátmenetek, aktuális cél, költség és eredmények jól elkülöníthető naplózása.
- [x] Disconnect/teleport/karakterváltás során in-flight műveletek törlése.
- [x] Újraindítási reconciliation a mentett queue és az inventory win/lose kuponjai alapján.
- [x] Ismeretlen kupon vagy bizonytalan fizetős eredmény esetén fail-closed leállítás.
- [x] A normál Items sell/store és más botfunkciók kizárása a Magic POP műveletek alatt.

### Task 10 – Ellenőrzés és átadás

- [ ] Unit tesztek a referencia-parserhez, packet parserekhez és állapotgéphez.
- [x] Release build MSBuilddel, a repository buildszabályai szerint.
- [ ] UI smoke test: botválasztó, tab, filterek, transfer list, profilmentés.
- [ ] Korlátozott éles teszt egy céllal és kevés kártyával, packet capture mellett.
- [ ] Többcélos, takarítási, Stop/disconnect és újraindítási tesztek.
- [ ] A munkalista, protokollcontract és felhasználói működés végleges dokumentálása.

## 1. Cél

Új, önálló `RSBot.MagicPop` bot base készítése, amely Hotanban automatizálja a Magic POP Cardok megvásárlását, a kiválasztott jutalmakra történő rollolást és kizárólag a vesztes (zöld) kuponok eladását. A nyertes kuponok beváltása nem része az első verziónak: ezeket a bot érintetlenül hagyja a felhasználónak.

A bot ne a kliens nehézkes, háromlépcsős Class / Type / Degree kiválasztását másolja. Az RSBot felületén Degree és tágabb kategória alapján egyszerre legyen látható az összes releváns jutalom, és egy rendezhető prioritási listába lehessen őket felvenni.

## 2. Felhasználói igény

### 2.1. Ismert objektumok

- Magic POP machine: `NPC_CH_GACHA_MACHINE`
- Magic POP reward operator: `NPC_CH_GACHA_OPERATOR`
- Magic POP Card: `ITEM_MALL_GACHA_CARD`
- vesztes kupon: `ITEM_MALL_GACHA_CARD_LOSE`, szövegkulcs: `SN_ITEM_MALL_GACHA_CARD_LOSE`
- nyertes kupon: `ITEM_MALL_GACHA_CARD_WIN`, szövegkulcs: `SN_ITEM_MALL_GACHA_CARD_WIN`
- NPC opciók:
  - `MagicPopPlay = 17`
  - `MagicPopExchange = 18`
- kliens referenciafájlok:
  - `server_dep\silkroad\textdata\gachaitemset.txt`
  - `server_dep\silkroad\textdata\gachanpcmap.txt`

### 2.2. Felület

- A Magic POP önálló bot base legyen, ugyanúgy, mint a Training és az Alchemy.
- A jobb felső `Bots` menüben lehessen `Magic POP` néven kiválasztani; ne a Training vagy valamelyik általános plugin részeként fusson.
- Kiválasztásakor a bot base saját `Magic POP` tabja jelenjen meg a Training és Alchemy bot base-ekkel azonos integrációs mintát követve.
- A Start/Stop, aktív bot base, státusz, karakterbetöltés és profilváltás életciklusa az `IBotbase` infrastruktúrán keresztül működjön. Másik bot base-re váltáskor a Magic POP biztonságosan álljon le, és ne maradjon függőben vásárlási, rollolási, eladási vagy scriptművelet.
- Degree legördülő, csak a `gachaitemset.txt` alapján ténylegesen elérhető degree értékekkel.
- Race legördülő `All`, `CH` és `EU` értékekkel.
- Rarity legördülő, amely alapértelmezésben csak a `Seal of Sun` célokat mutatja, de választható Star, Moon, Nova vagy All is.
- Kategória legördülő. Első körben:
  - Armor
  - Protector
  - Garment
  - Heavy Armor
  - Light Armor
  - Robe
  - Weapon
  - Accessory
- A bal oldali listában az adott degree és kategória összes elérhető Magic POP cél-iteme jelenjen meg.
- Az `Other` kategória nem része az első verziónak.
- A lista oszlopai:
  - item neve;
  - konkrét subtype, például Staff vagy Protector Helm.
- A degree, codename, rarity és GachaID továbbra is része a szűrésnek vagy a belső modellnek és a mentett célazonosításnak, de nem foglal külön oszlopot a felületen. A rarity az item nevéből is látható.
- A lista subtype szerint, azon belül rarity szerint legyen rendezve. A javasolt rarity-sorrend: `A_RARE`, `B_RARE`, `C_RARE`.
- Transfer list jellegű kezelés:
  - balról jobbra felvétel;
  - jobbról eltávolítás;
  - többes kijelölés;
  - fel/le mozgatás és lehetőség szerint drag & drop.
- Degree vagy kategória váltásakor a jobb oldali kiválasztott lista maradjon meg.
- A bal oldali katalógusból hozzáadott item maradjon látható és legyen újra hozzáadható, hogy ugyanabból a célból több darab is kérhető legyen.
- A jobb oldali sorrend a rollolási prioritás. A bot mindig a legfelső függő célra rollol.
- A beállítás karakterprofilonként, sorrendtartó módon mentődjön. Elsődleges kulcs a `GachaID`, mellé ellenőrzésként a `RefItemID` és codename is kerüljön.
- Látható futási állapot és számlálók:
  - aktuális cél;
  - felhasznált kártyák;
  - vesztes és nyertes eredmények;
  - megmaradt célok;
  - jelenlegi állapot, például `Buying cards`, `Walking`, `Rolling`, `Selling losses`, `Completed`.

### 2.3. Működés

1. Induláskor ellenőrizze, hogy a karakter játékra kész, van használható return scroll, nincs másik script vagy shopping művelet folyamatban, és van legalább egy kiválasztott cél. Használja a return scrollt, várja meg a teleport teljes befejezését, ellenőrizze a Hotan return pontot, majd váltson clientless módra.
2. Vásároljon Magic POP Cardokat az Item Mallból, ameddig biztonságosan lehet.
3. Futtassa a `HotanTeleportToMagicPop.rbs` scriptet.
4. A prioritási lista első elemére rolloljon, amíg:
   - nyer;
   - elfogy a kártya;
   - elfogy a szükséges inventoryhely;
   - a szerver hibát ad;
   - a felhasználó leállítja a botot.
5. Nyertes kupon esetén azt egyértelműen az aktuális célhoz kell rendelni. A cél ekkor teljesítettnek tekinthető és kivehető az aktív prioritási listából. A bot a piros kupont nem váltja be és nem adja el; a következő cél rollolásával folytathatja.
6. Kártyaelfogyás vagy inventorynyomás esetén fusson le a `HotanMagicPopToPotion.rbs`, majd kizárólag a vesztes, zöld Magic POP kuponokat adja el a `NPC_CH_POTION` NPC-nek.
7. Ha maradt cél és van még elkölthető silk, a `HotanPotionToMagicPop.rbs` segítségével térjen vissza, vásároljon újabb biztonságos kártyaadagot, és folytassa.
8. Ha minden cél elkészült, vagy elfogyott a silk és már nincs felhasználható kártya, egyszer még adja el a vesztes kuponokat, majd álljon le.

## 3. Fontos pontosítások és biztonsági szabályok

### 3.1. Inventorykapacitás

A capture igazolta, hogy a roll eredménye a felhasznált kártya saját slotjában jelenik meg, tehát külön result slot nem szükséges. Az opcionális későbbi beváltás szintén helyben alakítja át a kupont.

A vásárlási kapacitást az aktuális üres slotok és a már megőrzött piros kuponok alapján kell számolni. A bot minden vásárlás és roll előtt ellenőrizze, hogy a hivatkozott card slot továbbra is a várt itemet tartalmazza. Általános, két slotos `Reserved result slots` korlátozás nem szükséges; legfeljebb külön konfigurálható működési tartalék tartható más botfunkciók számára.

### 3.2. Kizárólag bizonyított itemet szabad eladni

Az eladási döntés ne a szín, a lokalizált név vagy a globális Items sell filter alapján történjen. Saját, szűk Magic POP eladási lista kell, amely csak a referenciaadat alapján azonosított `ITEM_MALL_GACHA_CARD_LOSE` rekordokat engedi eladni.

Soha nem adható el:

- `ITEM_MALL_GACHA_CARD_WIN`;
- a nyertes kuponból később, kézzel beváltott cél-item;
- más zöld színű vagy más Item Mall item;
- ismeretlen vagy nem egyértelműen dekódolt kupon.

### 3.3. Egy időben egy hálózati művelet

Vásárlásból, rollból és eladásból egyszerre csak egy kérés lehet folyamatban. Következő kérés csak a megfelelő válasz vagy ellenőrzött inventory-diff után indulhat. Timeout esetén nem szabad automatikusan ugyanazt a kérést korlátlanul ismételni, mert a késő válasz dupla műveletet okozhat.

### 3.4. A nyertes kupon a bot számára teljesített cél

Az első verzió nem vált be kuponokat. A megfelelő GachaID-hoz tartozó, hitelesített nyertes kupon megjelenése teljesíti az aktuális célt. A bot megőrzi a piros kupont, eltávolítja a célt az aktív queue-ból, és folytatja a következő céllal. Az exchange capture eredménye kizárólag jövőbeli opcionális funkció dokumentációja.

### 3.5. Leállítás és újraindítás

- Felhasználói Stop esetén új kérés már ne induljon; a folyamatban lévő választ rövid ideig meg lehet várni, majd a bot álljon biztonságos `Stopped` állapotba.
- Disconnect, teleport vagy karakterváltás törölje az in-flight műveletet.
- Újraindításkor az inventory és a mentett queue alapján állapot-reconciliation fusson. Ismeretlen nyertes kupon esetén a bot álljon meg, ne kezdjen új célra rollolni.
- A korábban már inventoryban lévő cél-item önmagában ne törölje automatikusan az újonnan felvett queue-elemet. A teljesítéshez az adott futásban vagy mentett folyamatállapotban igazolt nyertes roll kell.

## 4. Amit a jelenlegi kód már támogat

- A Core-ban létezik a `MagicPopPlay` és `MagicPopExchange` talk option.
- A `ShoppingManager.ChooseTalkOption` képes az NPC kiválasztására és a `0x7046` / `0xB046` dialog handshake kezelésére.
- A `ScriptManager` képes `.rbs` fájl betöltésére, futtatására, leállítására és eredményének jelzésére.
- A jelenlegi Hotan town script régióazonosítója `25000`, és tartalmazza a `NPC_CH_POTION` útvonalpontját.
- A `RefObjItem` már tartalmazza a szükséges alapadatokat: `Degree`, `Rarity`, `TypeID2/3/4`, `IsArmor`, `IsWeapon`, `IsAccessory`, codename és lokalizált név.
- Az inventory parser felismeri a Gacha win/lose típuscsaládot (`TypeID3 == 14 && TypeID4 == 2`), de a kuponban lévő magic paraméterpárokat jelenleg eldobja.
- Az inventory operation parser már kezeli a sikeres Item Mallból inventoryba kerülést (`SP_BUY_CASH_ITEM`).
- Az Items és Skills felületekben van újrahasznosítható minta transfer listre, ikonokra és sorrendezésre.
- A `ShoppingManager.SellItem` felhasználható célzott eladásra, de a Magic POP botnak nem szabad a globális sell filtert futtatnia.

## 5. Feltárt hiányok

### 5.1. Referenciaadat

A `ReferenceManager` jelenleg nem tölti be a `gachaitemset.txt` és `gachanpcmap.txt` fájlokat. Új modellek és loader szükségesek.

A nyilvánosan ismert `gachaitemset.txt` mezősorrend valószínűleg:

`Service, Set_ID, RefItemID, Ratio, Count, GachaID, Visible, Param1, Param1Desc, Param2, Param2Desc, Param3, Param3Desc, Param4, Param4Desc`

Ezt a használt kliens tényleges fájlján ellenőrizni kell. A `Set_ID` jelentése különösen fontos: nyilvános vSRO leírások alapján az 1-es set a cél/jutalom, a 2-es set a sikertelen eredménykészlet lehet. A felületen csak aktív, látható, érvényes itemreferenciával rendelkező célrekordok jelenhetnek meg.

### 5.2. Magic POP protokoll

A jelenlegi repositoryban nincs `0x7118`/`0xB118` Magic POP play és `0x7119` reward exchange handler. A konkrét capture-ben `0xB119` válasz nem érkezett; a beváltás eredményét `0x3040` inventory update packetek közölték.

Nyílt forrású opcode-listák és egy működő phBot plugin alapján erős jelölt:

- play request: `0x7118`;
- play response: `0xB118`;
- reward exchange request: `0x7119`;
- egyes implementációkban feltételezett reward response: `0xB119`, amely ezen a szerveren a rögzített sikeres beváltásnál nem érkezett.

A konkrét kliensen rögzített és az implementációban használt play payload:

`NpcUniqueId:uint32 + GachaID:uint32 + CardSlot:uint8`

A request 9 byte hosszú és nem encrypted. A capture három egymást követő kérésénél ez a forma stabilan egyezett; a hozzájuk tartozó `0xB118` válaszok két byte hosszúak voltak.

### 5.3. Item Mall vásárlás és silk

- A Core a sikeres cash-item inventory response-ot részben kezeli, de cash-item vásárlási kérés nincs implementálva.
- A `Player` modell goldot követ, silk egyenleget nem.
- Ismeretlen a kártya Item Mall package ID/tab/slot leképezése és a vásárlási request payloadja.
- Az „elfogyott a silk” állapotot nem szabad pusztán sikertelen vásárlásból találgatni; a pontos error code vagy silk update packet azonosítása szükséges.

### 5.4. Nyertes kupon tartalma

Az `InventoryItem.FromPacket` a Gacha kupon magic paramétereit jelenleg beolvassa, de nem tárolja. Emiatt most nem bizonyítható, hogy egy `ITEM_MALL_GACHA_CARD_WIN` mely GachaID-hoz vagy RefItemID-hoz tartozik. Ezt meg kell őrizni egy típusos adatmodellben, és össze kell vetni a roll request aktuális céljával.

### 5.5. Reward operator útvonal – első verzión kívül

A megadott három script a machine és potion shop közötti mozgást lefedi. Mivel az első verzió nem vált be kuponokat, a `NPC_CH_GACHA_OPERATOR` megközelítésére nincs szüksége. Ha később opcionális automatikus beváltás készül, ellenőrizni kell, hogy az operator a machine mellől elérhető-e; ellenkező esetben további machine ↔ operator `.rbs` útvonal kell.

### 5.6. A megadott scriptek még nincsenek a repositoryban

Jelenleg nem található:

- `HotanTeleportToMagicPop.rbs`;
- `HotanMagicPopToPotion.rbs`;
- `HotanPotionToMagicPop.rbs`.

A bot indulási validációja adjon pontos hibát minden hiányzó vagy üres scriptre. Javasolt célmappa: `Dependencies\Scripts\MagicPop`, build után `Build\Data\Scripts\MagicPop`.

## 6. Javasolt architektúra

### 6.1. Új bot base

Új projekt: `Botbases\RSBot.MagicPop`.

Az integráció a meglévő `RSBot.Training` és `RSBot.Alchemy` mintáját kövesse:

- saját `IBotbase` bootstrap és saját `Magic POP` tab;
- regisztráció a jobb felső `Bots` választóba `Magic POP` megjelenítési névvel;
- csak akkor fusson az állapotgép, amikor ez az aktív bot base;
- a közös bot Start/Stop vezérlés irányítsa;
- bot base-váltás, karakterváltás, disconnect és alkalmazásbezárás esetén ugyanazokat a biztonságos leállítási szabályokat alkalmazza;
- a Magic POP-specifikus beállítások és futási állapot ne keveredjen a Training vagy Alchemy konfigurációjával.

Javasolt fő részek:

- `Bootstrap`: `IBotbase` integráció és életciklus.
- `MagicPopBot`: állapotgép és magas szintű vezérlés.
- `MagicPopConfig`: rendezett célok és UI-beállítások karakterprofilonként.
- `MagicPopReferenceProvider`: a két gacha referenciafájl betöltése és validálása.
- `MagicPopClient`: az első verzióban play requestek és eredmények kezelése; az exchange külön, későbbi opcionális bővítés.
- `ItemMallClient`: kártyacsomag feloldása, vásárlás és silk/error állapot kezelése.
- `MagicPopInventoryService`: card/win/lose/target azonosítás, inventory-diff és biztonságos eladás.
- `MagicPopRouteRunner`: kizárólagos hozzáférés a `ScriptManager`-hez, completion/error kezelés.
- `MagicPopTrace`: célzott diagnosztikai packet- és állapotnapló.
- `Views\Main`: filter, transfer list, prioritás, státusz és diagnosztikai vezérlők.

Első körben a gacha referencia-loader maradhat a bot base-ben, mert `Game.MediaPk2` publikus és így a Core módosítása korlátozható. Ha később más modul is használja az adatot, akkor érdemes a `ReferenceManager` részévé tenni.

### 6.2. Célmodell

Javasolt `MagicPopTarget` mezők:

- `GachaId`;
- `RefItemId`;
- `CodeName`;
- `DisplayName`;
- `Degree`;
- `Category`;
- `SubType`;
- `Rarity`;
- `QueueOrder`;
- `State`: `Pending`, `Rolling`, `WinnerCouponHeld`, `Completed`, `Error`.

A queue-ban a GachaID a hálózati azonosító, a RefItemID pedig a jutalom inventoryban történő igazolásához szükséges. Betöltéskor mindkettőt újra fel kell oldani; eltérés esetén az elem hibásként jelenjen meg és a Start legyen tiltva.

### 6.3. Kategorizálás

Elsődlegesen a `RefObjItem.TypeID` mezőiből kell kategorizálni. A codename csak kiegészítő, szerverenként eltérő fallback legyen. A pontos CH/EU armor subtype és weapon subtype mappinghez a használt `ItemData` rekordokat dumpoló diagnosztika készüljön, hogy ne szöveges név alapján kelljen következtetni.

## 7. Állapotgép

Javasolt állapotok:

1. `Idle`
2. `Validating`
3. `BuyingCards`
4. `WalkingToMachine`
5. `WaitingForMachine`
6. `Rolling`
7. `WaitingForRollResult`
8. `WalkingToPotion`
9. `SellingLosses`
10. `WalkingBackToMachine`
11. `FinalCleanup`
12. `Completed`
13. `Stopped`
14. `Error`

### 7.1. Átmenetek

- `Validating → BuyingCards`: minden előfeltétel rendben.
- `BuyingCards → WalkingToMachine`: van legalább egy kártya, és minden megvett kártyának érvényes inventory slotja van.
- `BuyingCards → FinalCleanup`: nincs kártya, és a silk igazoltan elfogyott.
- `Rolling → WaitingForRollResult`: pontosan egy play request elküldve.
- `WaitingForRollResult → Rolling`: bizonyított vesztes eredmény és van további kártya/hely.
- `WaitingForRollResult → Rolling`: bizonyított nyertes kupon esetén az aktuális cél teljesített, és van következő cél, kártya és elegendő hely.
- `WaitingForRollResult → FinalCleanup`: az utolsó cél nyertes kuponja megérkezett.
- `WaitingForRollResult → WalkingToPotion`: nincs kártya vagy elértük a biztonságos inventory-limitet.
- `SellingLosses → WalkingBackToMachine`: van függő cél és a futás folytatható.
- `SellingLosses → Completed`: minden cél kész, vagy nincs silk és nincs kártya.
- Bármelyik állapotból `Stopped/Error`: felhasználói leállítás, disconnect, timeout utáni bizonytalan állapot vagy sérült referencia.

### 7.2. Roll ütemezése

A nyilvános plugin 3 másodpercet vár két roll között. A végleges bot ne csak fix sleepre épüljön: várja meg a `0xB118` választ és a hozzá tartozó inventory-változást, majd tartson egy konzervatív minimum intervallumot. Kezdetben 3000 ms ajánlott, és csak igazolt működés után csökkenthető.

## 8. Packet-logolási és feltárási terv

### 8.1. Diagnosztikai mód

A Magic POP felület kapjon egy külön `Protocol capture` részt:

- `Enable Magic POP packet capture` checkbox, alapból kikapcsolva;
- `Capture all agent packets for 10 seconds` gomb;
- kézi marker mező és `Add marker` gomb;
- gyors marker gombok: `Open machine`, `Change filter`, `Select reward`, `Roll`, `Open operator`, `Redeem`, `Buy card`, `Sell lose`;
- aktuális capture fájl elérési útja;
- `Open log folder` gomb.

A capture ne a normál logablakot spamelje. Külön fájlba írjon, például:

`Build\User\Logs\MagicPop\<character>_<yyyyMMdd_HHmmss>.log`

### 8.2. Naplóformátum

Minden bejegyzés tartalmazza:

- UTC és lokális timestamp milliszekundummal;
- növekvő sequence ID;
- irány: `C→S` vagy `S→C`;
- opcode hex és payload hossz;
- encrypted/massive flag;
- teljes payload hex;
- aktuális marker és bot state;
- NPC unique ID/codename, ha ismert;
- inventory snapshot hash, szabad slotok, kártya/win/lose darabszám;
- ismert packetnél strukturált, mezőnkénti parse;
- parse végén a fel nem használt byte-ok száma.

Példa:

```text
2026-09-10T14:32:15.123+02:00 #0042 C→S opcode=0x7118 len=9 state=Manual marker="Roll D11 Staff C_RARE"
hex=78 56 34 12 2A 10 00 00 0D
parsed npcUid=0x12345678 gachaId=4138 unknown=0 cardSlot=13 remaining=0
inventory before free=3 cards=25 win=0 lose=4
```

### 8.3. Technikai bekötés

A proxy már kibocsátja az `OnClientPacketReceive` és `OnServerPacketReceive` eseményeket. A logger ezekre feliratkozva, a packetet másolva olvasson, és soha ne módosítsa az eredeti reader pozícióját.

Két capture mód kell:

1. szűrt, folyamatos mód az ismert jelöltekre;
2. rövid, teljes agent capture egy kézi művelet körül, hogy ismeretlen opcode is megtalálható legyen.

Kezdeti szűrőlista:

- `0x7045`, `0xB045` – NPC select;
- `0x7046`, `0xB046` – NPC talk option;
- `0x7118`, `0xB118` – feltételezett Gacha play;
- `0x7119`, `0xB119` – feltételezett Gacha reward exchange;
- `0x7034`, `0xB034` – inventory és Item Mall műveletek;
- `0x3153` – feltételezett silk update;
- `0x3154` – feltételezett silk notify/error;
- minden olyan ismeretlen opcode, amely csak a marker utáni rövid ablakban jelenik meg.

### 8.4. Kézi capture forgatókönyv

A felhasználó az alábbiakat külön capture-ben, markerrel végezze el:

1. 5–10 másodperc tétlen baseline a Magic POP NPC mellett.
2. NPC kijelölése és `1. Participate in the game` megnyitása.
3. Csak a Class megváltoztatása.
4. Csak a Type megváltoztatása.
5. Csak a Degree megváltoztatása.
6. Egy ismert `A_RARE`, majd `B_RARE`, majd `C_RARE` cél kijelölése roll nélkül.
7. Pontosan egy roll, előtte feljegyezve: NPC UID, kiválasztott item neve/codename/GachaID, card slot és inventory.
8. Egy biztos vesztes és – amikor előfordul – egy nyertes eredmény teljes válaszfolyama.
9. Reward operator megnyitása és pontosan egy nyertes kupon beváltása.
10. Item Mall megnyitása és pontosan egy Magic POP Card megvásárlása.
11. Ha biztonságosan előidézhető, sikertelen vásárlás elégtelen silk miatt.
12. Pontosan egy vesztes kupon eladása a potion NPC-nek.

Minden capture mellé rövid jegyzet kell: mit kattintott a felhasználó, mi volt előtte/utána az inventoryban, és mit írt ki a kliens. Így opcode-diff alapján a mezők nagy része kliens-visszafejtés nélkül is meghatározható.

### 8.5. Különösen igazolandó kérdések

- A play request ténylegesen 9 vagy bizonyos klienseken 10 byte-os-e.
- A `0xB118` tartalmazza-e közvetlenül a win/lose státuszt, GachaID-t és/vagy az új item slotját.
- A nyertes kupon magic paraméterei között GachaID, RefItemID vagy más kulcs van-e.
- Megerősíteni egy további mintával, hogy sikeres beváltáskor ezen a szerveren mindig elmarad-e a `0xB119`, és kizárólag az inventory update-ek jelzik-e a befejezést.
- A reward exchange során várt `0x3040` packetek darabszámát az exchange előtti kuponsnapshotból kell-e meghatározni.
- A cash shop purchase request opcode-ja, mezői és encryption flagje.
- Melyik response/error code jelenti biztosan az elégtelen silk állapotot.
- A kártya, win és lose item stackelési szabálya.
- A machine és operator közötti kapcsolat a `gachanpcmap.txt` fájlban.

### 8.6. Igazolt capture-eredmények – 2026-09-10

A `Build\User\Logs\PacketCapture\packet-capture-20260910-180816-337.jsonl` fájl egy Item Mallból történő háromkártyás vásárlást, a Magic POP machine megnyitását, majd három rollt tartalmaz. A felhasználó által megadott eredménysorrend `fail → success → fail`, és ez egyértelműen megfelel a packeteknek.

#### Item Mall vásárlás

- A Magic POP Card vásárlása `0x7034` kéréssel történik.
- A request első byte-ja `0x18`, amely a meglévő `InventoryOperation.SP_BUY_CASH_ITEM` értéknek felel meg.
- A request tartalmazza a `PACKAGE_ITEM_MALL_GACHA_CARD` package codename-et.
- A request shop-azonosító része referenciaadatból feloldható, de nem a sima `RefShopTab.Id`: a wire forma `RefShopGroupID:uint16 + GroupIndex:byte + TabIndex:byte`, majd `RefShopGoods.SlotIndex`. A Magic POP card esetén ez `GROUP_MALL (1051) + MALL_CONSUME shop index 2 + tab index 0 = 0x0002041B`, a slot pedig `11`.
- A request végén szereplő `0x000150FC` (`86268`) a `RefPackageItem.txt` Magic POP card rekordjának ID-ja. Nem a `RefScrapOfPackageItem` utolsó, ezen a kliensen `88838` értékű mezője. Az implementáció a helyes ID-t közvetlenül a megfelelő referenciafájlból olvassa, nem konstansként használja.
- A teljes, capture-rel egyező request felépítése: operation, encoded shop-tab ID, shop slot, package codename, quantity, két nullázott fenntartott mező és package index.
- Mindhárom vásárlást sikeres `0xB034` válasz követte.
- A kártyák külön inventory slotokba kerültek: `32`, `33`, majd `34`.
- Minden vásárlás után `0x3153` érkezett. Ennek első `uint32` mezője `9 999 946 → 9 999 936 → 9 999 926` értékre változott, tehát ezen a szerveren egy kártya 10 Silkbe került.
- A `0x3153` további két `uint32` mezője ebben a capture-ben nulla volt. A három mező pontos Silk/Gift Silk/Silk Point jelentését további minták nélkül nem szabad véglegesnek tekinteni.

#### Magic POP párbeszéd megnyitása

- NPC kijelölés: `0x7045` / `0xB045`.
- A rögzített machine runtime UniqueID-ja `0x000004CA` volt.
- A játék megnyitása: `0x7046` payload `NpcUniqueId:uint32 + 0x11:byte`.
- A sikeres válasz `0xB046`, payloadja `01 11`.
- Ez megerősíti, hogy a `0x11` a `MagicPopPlay` talk option.

#### Roll request és eredmény

A három request ugyanazt a 9 byte-os struktúrát használta:

```text
0x7118 Magic POP play request
NpcUniqueId : uint32
GachaId     : uint32
CardSlot    : byte
```

A konkrét minták:

```text
CA040000 06000000 21  -> NPC 0x04CA, GachaID 6, slot 33 -> fail
CA040000 06000000 22  -> NPC 0x04CA, GachaID 6, slot 34 -> success
CA040000 06000000 20  -> NPC 0x04CA, GachaID 6, slot 32 -> fail
```

A `0xB118` válasz két byte-os:

```text
01 00 -> a request feldolgozása sikeres, a roll eredménye fail
01 01 -> a request feldolgozása sikeres, a roll eredménye success
```

Ebből az implementáció számára igazolt contract:

- első byte: request/protocol eredmény; a mintában `1`;
- második byte: roll outcome; `0 = fail`, `1 = success`.

A három próbánál a szerver előbb küldte el a `0x3040` inventory update packetet, és csak utána a `0xB118` eredményt. A különbség körülbelül 34–44 ms volt. A bot ezért egy rollt csak akkor tekintsen teljesen lezártnak, amikor az adott requesthez tartozó eredményválasz és inventory-változás is megérkezett; a kód ne feltételezze, hogy a `0xB118` érkezik elsőként.

#### A kártya átalakulása

A `0x3040` packet alapján a szerver a felhasznált kártyát ugyanabban az inventory slotban alakítja eredménykuponná:

- nyertes kupon RefItemID: `9239` (`0x00002417`);
- vesztes kupon RefItemID: `9240` (`0x00002418`).

A mintában tehát nem egy új result slotba került a kupon. Ez igazolja, hogy a roll önmagában nem igényel külön üres inventory slotot, mert a kártya helyét használja fel. A későbbi exchange capture azt is megmutatta, hogy a beváltott reward ugyanebben a kuponslotban jelenik meg. A vásárláshoz és több, megőrzendő piros kupon felhalmozásához azonban továbbra is megfelelő inventorykapacitás kell.

A felhasználó megerősítette, hogy a capture során kiválasztott cél az `ITEM_CH_SWORD_02_C_RARE`, vagyis a 2nd degree Sun kínai sword volt. Ez alapján ezen a kliensen/szerveren a capture-ben szereplő `GachaID = 6` ehhez a célhoz tartozik.

A kuponok további metaadatokat hordoznak. Az `ITEM_CH_SWORD_02_C_RARE` nyertes kuponjának első, célazonosítónak tűnő értéke `4019`. Ez erős jelölt a kiválasztott jutalom RefItemID-jára, de a referenciaadatból történő közvetlen feloldással, illetve egy másik ismert cél sikeres rolljával még ellenőrizni kell. A fail mintákban eltérő értékpárok szerepeltek (`3680 / 1`, illetve `6101 / 10`), ezért a jelenlegi általános `MagicOptionInfo` értelmezést nem szabad automatikusan helyes Gacha-metaadat modellként használni.

#### Implementációs következmények

- Egy rollhoz egyetlen in-flight `0x7118` request tartozhat.
- A korreláció kulcsa legalább a card slot, a GachaID és az elküldés ideje legyen.
- A `0xB118` outcome és a `0x3040` slotátalakulás egymást kölcsönösen ellenőrizze.
- `fail` csak akkor fogadható el, ha az outcome `0`, és a request card slotjában a bizonyított lose RefItemID jelenik meg.
- `success` csak akkor fogadható el, ha az outcome `1`, és ugyanott a bizonyított win RefItemID jelenik meg.
- Ellentmondó, hiányzó vagy timeoutos válasz esetén a bot álljon meg bizonytalan eredménnyel; ne küldjön újabb fizetős rollt.
- A nyertes kupon cél-metaadatait meg kell őrizni, és a nyertes roll elfogadása előtt össze kell vetni az aktuális queue-elemmel.
- A korábbi két fenntartott result slot követelményét sem a rollhoz, sem az opcionális későbbi exchange-hez nem kell vakon alkalmazni; mindkettő helyben alakítja át a kupon slotját.

#### Következő szükséges capture-ek

1. Egy másik cél-itemre történő sikeres roll, az item neve/codename-je és RefItemID-ja feljegyzésével, hogy az `ITEM_CH_SWORD_02_C_RARE` success kuponjában talált `4019` mező jelentése keresztellenőrizhető legyen.
2. Opcionálisan még egy reward exchange, hogy a `0xB119` hiánya és a több kupon együttes beváltása ismételten igazolható legyen.
3. Ha biztonságosan előidézhető, sikertelen Item Mall vásárlás elégtelen Silk vagy megtelt inventory mellett.

### 8.7. Igazolt eladás és tömeges beváltás – jövőbeli referencia, 2026-09-10

A `Build\User\Logs\PacketCapture\packet-capture-20260910-182150-794.jsonl` fájlban a felhasználó előbb eladott egy zöld vesztes kupont a potion NPC-nek, majd a reward operatornál egyetlen művelettel beváltott egy megmaradt zöld és egy piros kupont.

Az automatikus beváltás nem része az első Magic POP botverziónak. Az alábbi exchange-információt azért őrizzük meg, hogy egy esetleges későbbi bővítésnél rendelkezésre álljon; a jelenlegi botterv nem küld `0x7119` packetet.

#### Vesztes kupon eladása

Az eladási request:

```text
0x7034: 09 21 0100 B8020000
```

Mezők:

- `0x09`: `InventoryOperation.SP_SELL_ITEM`;
- `0x21`: inventory slot `33`;
- `0x0001`: eladott mennyiség `1`;
- `0x000002B8`: a potion NPC runtime UniqueID-ja.

A szerver sikeres `0xB034` választ küldött:

```text
01 09 21 0100 B8020000 01
```

Ez megerősíti, hogy a meglévő `ShoppingManager.SellItem` packetformátuma használható, de a botnak az eladás előtt RefItemID alapján explicit módon ellenőriznie kell, hogy a slotban a bizonyított vesztes kupon (`9240`) van.

#### Reward operator és exchange request

- A reward operator runtime UniqueID-ja ebben a capture-ben `0x000004C9` volt.
- Az exchange request opcode-ja igazoltan `0x7119`.
- A request payloadja pontosan négy byte, és csak az operator UniqueID-ját tartalmazza:

```text
0x7119: C9040000
```

Nincs benne kuponslot, GachaID vagy mennyiség. A kliens egyetlen exchange művelete az összes inventoryban lévő zöld és piros Magic POP kupont beváltja.

A capture-ben a request után legalább hat másodpercig nem érkezett `0xB119`. A sikeres művelet eredményét két `0x3040` inventory update közölte, ezért ezen a szerveren nem szabad `0xB119`-re várni a completion feltételeként.

#### A zöld kupon beváltási eredménye

A slot `32` vesztes kuponja a következő update-tel alakult át:

```text
0x3040: 20 29 D5170000 0A00 00
```

Értelmezés:

- slot: `32`;
- update flags: `0x29` = `RefObjID + Quantity + MagParams`;
- új RefItemID: `6101` (`0x000017D5`);
- mennyiség: `10`;
- magic parameter count: `0`.

Ez pontosan megfelel a roll során a vesztes kuponban látott `6101 / 10` metaadatpárnak. Tehát a vesztes kupon metaadata előre leírja, milyen consolation itemmé és milyen mennyiséggé alakulna beváltáskor.

#### A piros kupon beváltási eredménye

A slot `34` nyertes kuponja ugyanabban a slotban ténylegesen az `ITEM_CH_SWORD_02_C_RARE` jutalommá alakult:

- új RefItemID: `4019` (`0x00000FB3`);
- pluszszint: `+3`;
- variance: `0x00000000C610B0E4`;
- durability: `78`;
- két magic option: `ID 11 = 3`, illetve `ID 5 = 3`.

Ez közvetlenül igazolja, hogy a nyertes kuponban korábban talált első `4019` érték a cél-item RefItemID-ja volt. A success kupon metaadata tehát legalább a beváltandó reward RefItemID-ját tartalmazza.

#### Követelmények egy esetleges későbbi automatikus beváltáshoz

- A `0x7119` globális exchange: nem lehet vele csak a piros kupont kiválasztani.
- Ha később automatikus exchange készül, minden exchange előtt garantálni kell, hogy egyetlen lose kupon sincs az inventoryban.
- Egy jövőbeli megvalósítás helyes sorrendje: roll leállítása → potion route → összes lose kupon célzott eladása → visszaút → operator → egyetlen `0x7119` → az összes várt `0x3040` ellenőrzése.
- Exchange előtt snapshot kell az összes win/lose kupon slotjáról és dekódolt reward metaadatáról.
- Completion akkor állapítható meg, amikor minden snapshotolt kuponslot a várt valódi itemmé alakult, és az inventoryban már nincs win/lose kupon. Timeout vagy eltérés esetén újabb exchange request nem küldhető automatikusan.
- A cél csak akkor távolítható el a queue-ból, ha a hozzá tartozó RefItemID a várt slotban ténylegesen megjelent.

## 9. Megvalósítási fázisok

### Fázis 0 – diagnosztika

1. Célzott packet logger elkészítése külön fájllal és markerekkel.
2. Gacha referenciafájlok nyers dumpja és oszlopszám-validációja.
3. A fenti kézi capture-sorozat elvégzése.
4. Packet contract dokumentálása mintafájlokkal.

Kimenet: igazolt opcode/payload leírás, ismert error code-ok és inventory itemformátum. E nélkül az automata Silk-vásárlást és rollolást nem szabad élesíteni.

### Fázis 1 – referenciaadat és UI

1. `RefGachaItemSet` és `RefGachaNpcMap` modellek.
2. PK2 loader, hibás/hiányzó fájlok kezelése.
3. TypeID-alapú degree/category/subtype/rarity mapping.
4. Transfer list UI, sorrendezés és karakterprofilos mentés.
5. Start előtti validáció és diagnosztikai összefoglaló.

### Fázis 2 – protokollréteg

1. Magic POP play request/response típusos kezelése.
2. Nyertes/vesztes inventory item metaadat megőrzése.
3. Item Mall card purchase és silk update/error kezelés.
4. Timeout, egyetlen in-flight kérés és késő válasz elleni védelem.
5. A reward exchange parser és automatizálás csak külön, későbbi opcionális fázisban készülhet el.

### Fázis 3 – útvonal és állapotgép

1. A három megadott script felvétele és validálása.
2. A teljes, beváltás nélküli állapotgép implementálása.
3. Célzott veszteskupon-eladás.
4. Stop/disconnect/restart reconciliation.

### Fázis 4 – tesztelés és fokozatos élesítés

1. Parser tesztek valódi, anonimizált packet fixture-ökkel.
2. Állapotgép tesztek fake Magic POP és inventory service-szel.
3. `Dry run` mód: listázza a következő műveletet, de nem küld fizetős vagy eladási packetet.
4. Első éles teszt: egy cél, egyesével vásárolt kevés kártya, részletes trace.
5. Többcélos prioritási teszt és restart teszt.
6. Csak ezután teljes inventory-batch és ismétlődő machine–potion ciklus.

## 10. Elfogadási feltételek

- A felület csak a kliens gachaadataiban valóban elérhető equipment/weapon/accessory célokat mutatja.
- Degree/kategória váltás nem törli és nem rendezi át a kiválasztott queue-t.
- A mentett sorrend újraindítás után változatlan.
- A bot mindig a queue első függő elemére rollol.
- Azonos cél többszöri felvétele több külön queue-elemet jelent; egy igazolt nyerés pontosan egy elemet távolít el.
- Egy roll egy kártyát használ, és nincs párhuzamos vagy dupla kérés.
- A hitelesített nyertes kupon megjelenésekor a cél kikerül az aktív queue-ból, a piros kupon pedig érintetlenül az inventoryban marad.
- A bot soha nem ad el nyertes kupont vagy nem Magic POP vesztes itemet.
- A bot az első verzióban soha nem küld `0x7119` exchange requestet.
- A bot nem követel felesleges result-slotot, de soha nem vásárol több kártyát, mint amennyi tényleges üres inventoryhely rendelkezésre áll.
- Silk elfogyásakor a már megvett kártyákat még felhasználja, utána elvégzi az utolsó veszteskupon-takarítást és érthetően leáll.
- Minden megállási ok jól elkülöníthetően megjelenik a logban és a bot státuszában.
- Hiányzó NPC, script, referencia, card, inventoryhely, nem ismert kupon vagy packet timeout esetén nincs végtelen retry.
- Disconnect és kézi Stop után nem indul új fizetős művelet.

## 11. Nyitott döntések a capture után

1. Hány inventoryhelyet kell ténylegesen fenntartani a kártyavásárláshoz és a több piros kupon biztonságos megőrzéséhez?
2. A kártyavásárlás maximális batchmérete és a kártyák stackmérete mennyi?
3. A silk három típusa közül – ha a kliens külön kezeli a Silk, Gift Silk, Silk Point egyenleget – melyiket és milyen sorrendben használja a szerver?
4. A nyertes kupon közvetlenül tartalmazza-e a cél GachaID-ját, vagy az aktuális requesttel kell korrelálni?
5. A queue-ból eltávolított, teljesített célokat külön historyban is meg kell-e őrizni a felületen? Javaslat: igen, futásonkénti read-only historyban.
6. Egy későbbi automatikus beváltásnál az operator elérhető-e közvetlenül a machine mellől, vagy új route script kell?

## 12. Feltárási források

- A repository saját `TalkOption`, `ReferenceManager`, `RefObjItem`, `InventoryItem`, `InventoryOperationResponse`, `ShoppingManager`, `ScriptManager`, Items és Skills UI implementációi.
- Nyílt forrású opcode lista: [EasySSA OPCode.cs](https://github.com/Dentrax/EasySSA/blob/master/EasySSA/Packets/OPCode.cs).
- Nyílt forrású működési példa és play payload: [Bunker141/Phbot-Plugins AutoGacha.py](https://github.com/Bunker141/Phbot-Plugins/blob/master/AutoGacha.py).
- A `gachaitemset` ismert mezőihez és Set_ID jelentéséhez felhasznált közösségi referencia: [Magic POP RefGachaItemSet mezők](https://www.elitepvpers.com/forum/sro-private-server-questions-answers/4856746-help-magic-pop-i-wanna-know-some-thing-about-magic-pop.html).

Ezek a külső források kiindulási pontok. A használt szerver és kliens konkrét packetjei elsőbbséget élveznek, ezért az éles implementáció előtt a Fázis 0 capture kötelező.
