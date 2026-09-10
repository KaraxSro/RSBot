# Módosítási összefoglaló

Ez a dokumentum a jelenlegi fejlesztői állapot teljes összefoglalója. A változtatások külön branchre kerülnek; az alap `master` ág nincs módosítva.

## Ténylegesen azonosított bow/skill probléma

A kínai bow karakter esetén a kliens a támadó skill befejezése után automatikusan elindította az ismétlődő normal attackot, ha a következő skill parancsa nem érkezett meg időben. A bot korábban megvárta az action végét, majd egy `cancel -> következő tick -> skill` ciklust indított. Emiatt:

- az azonos célpont életben maradásakor egy automatikus alap lövés közbeékelődött;
- a cancel és a következő skill között versenyhelyzet alakult ki;
- a szerver bizonyos esetekben a skill-kérést csendben későbbre tolta vagy nem fogadta el azonnal;
- új célpontnál ez nem jelentkezett, mert ott nem volt előző célponthoz tartozó folytatott basic attack.

A végső megoldás a következő skill előzetes queue-zása. A bot az aktuális támadó action alatt kiválasztja a következő használható skillt, majd az action végéhez közeledve elküldi, illetve az action-váltási packetnél azonnal dispatch-eli. Így a skill megelőzi a kliens automatikus bow attackját. A régi cancel ág tartalék viselkedésként megmaradt.

## Harci logolás és állapotkezelés

`SkillManager`, `AttackBundle` és az action/skill packet handlerek:

- célponthoz kötött pending cast állapot és timeout;
- cast-progress és célpontváltozás ellenőrzés;
- sikertelen vagy elakadt kérés célzott újrapróbálása;
- basic attack, recurring basic attack, skill és cancelling állapotok szétválasztása;
- cancel utáni action-tail kezelése és rövid settle védelem;
- következő skill queue-zása és korai dispatch-e az animáció végéhez közel;
- queue, dispatch, accept, reject, timeout és cancel események részletes `CombatTrace` naplózása;
- szerver által visszaküldött nyers cast packetek és hibakódok naplózása;
- cooldown/retry/mana/fegyver-követelmény állapot megjelenítése a trace-ben.

## Imbue javítás

Az imbue külön pending állapotot kapott. Az imbue nem írja felül az utolsó harci skill állapotát, és a rendszer a buff-add packet érkezésekor tekinti sikeresen befejezettnek. Ez megakadályozza az ismételt vagy túl korai imbue-kéréseket.

## Ability pet és auto sort

- Auto sort futó bot közben le van tiltva, így stash/ability-pet mozgatás közben nem módosítja párhuzamosan az inventoryt.
- Az ability pet pickup csak akkor ír `run completed` sort, ha valóban történt sikeres felvétel.
- A részleges hibák és tele inventory állapotok throttlingolt figyelmeztetést kapnak, nem másodpercenkénti spamet.
- A sikeres és sikertelen pickup mennyisége naplózható.

## Egyéb stabilitási javítások

- `Area.IsInSight` null ellenőrzést kapott a `spawnedEntity` null reference elkerülésére.
- A skill konfiguráció alkalmazása lock alatt történik, és a duplikált skill-ID-k kiszűrésre kerülnek.
- A skill objektum elérhetővé teszi a szerver által elutasított cast utáni retry-hátralévőt.

## Ellenőrzés

A Training és az alkalmazás Release buildje sikeresen lefutott, fordítási hibával nem állt meg. A meglévő compiler warningok ettől a változtatási csomagtól függetlenek.

## Későbbi main-be emelés javaslata

Amikor a javításokat visszaemeljük az alap ágra, érdemes külön kiválasztani a végül bizonyítottan szükséges részt: a bow skill queue/korai dispatch logikát és a hozzá tartozó minimális action-state kezelést. Az extra diagnosztikai és korábbi stabilitási változtatások külön mérlegelhetők.
