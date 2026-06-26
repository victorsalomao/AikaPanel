# AikaPanel — Spec

## Goal
Local web editor (runs locally now, Windows VPS later) for an Aika Online (Delphi) server.
Edits two data files with BYTE-EXACT output the server already expects. Friendly UI in Portuguese.

## Stack (decided)
- .NET 8, ASP.NET minimal API (Kestrel), serving static vanilla-JS front-end (no SPA framework).
- Published as single self-contained win-x64 .exe (PublishSingleFile, no installs on VPS).
- Built at `C:\Users\user\Downloads\AikaPanel\` (separate from server). Server code never modified.
- `appsettings.json`: dataDir (default `C:\Users\user\Downloads\AikaMerged\Bin\Data`), host/port (127.0.0.1:8099), admin password.

## Requirements
- REQ-01 Password gate before any edit (token in header). Bind localhost by default.
- REQ-02 Cash Shop: read `PI.bin` (6000 × 376-byte packed records, LE, Win-1252 strings).
- REQ-03 Editable fields: show(6), Name(8,64), Description(72,256), price(328,u32), amount(334,u16), ItemIndex(336,u16). Preserve every other byte.
- REQ-04 Searchable/paged table: #slot, Índice(u32 @0), Name, show, price, ItemIndex, amount. Counter "X registros, Y visíveis (show=1)".
- REQ-05 Backup target to timestamped `.bak` before any write. Validate size % 376 == 0; refuse otherwise.
- REQ-06 Round-trip self-test: decode→encode of all 6000 records == original bytes (proof of byte-exactness).
- REQ-07 Quests: edit `Quest\Quests.csv` (34 cols, headerless). Table edit/add/delete rows, save with backup.
- REQ-08 Itens: read/edit `ItemList.bin` (31000 × 464-byte packed `TItemFromList`, LE, Win-1252, +4 trailer).
  Offsets authoritative from FilesData.pas (sum==464). Editable: Name/NameEnglish/Descrition/ItemType/UseEffect/
  Level/IconID/Classe/TypeItem/TypeTrade/prices/stats. Searchable by ID/PT/EN. Backup + round-trip self-test.
- REQ-09 (bônus) Ícones: atlas ItemIcons01..11.jit (1024×1024, 32px cells). JIT→PNG decoder in tools/jit2png.
  IconID→cell formula NOT simple-sequential (verification failed) → IconID as number; thumbnails deferred (AIKA.exe RE).
- REQ-10 Cash sync client: ao salvar Loja de Cash, gravar `Data\PI.bin` (cru) E `ClientUIDir\PI.bin` (cifrado Key1),
  com backup do client. Cifra replica MasterEditor SaveEncriptedFileKey1: enc[j]=(raw[j]+cipher[j%102]+j)%256.
  PROVA: encrypt(decrypt(UI))==UI 2256004/2256004 byte-idêntico; decrypt(UI) legível. Badge de sync + botão Sincronizar.

## Quest pipeline finding (CRITICAL — verified)
Load.pas `InitQuests` (L863) reads `Quests.csv` DIRECTLY into `_Quests` at boot and assigns to NPCs (L1834).
NO CSV→Quest.bin compilation exists. `Quest.bin` (`InitQuestList` L836, 2332-byte records) is a SEPARATE
structure loaded independently. => Editing `Quests.csv` TAKES EFFECT after a server restart. Ship CSV editor.
Quest.bin left untouched (out of scope, too risky for budget).

## Out of scope
NPC editing, item definitions, client cash-shop display, Quest.bin binary editing.
