# AikaPanel — Tasks

- [x] T1 Scaffold: csproj (net8, single-file self-contained win-x64), appsettings.json, .gitignore.
- [x] T2 PiBinService: decode/encode 376-byte records (Win-1252). Round-trip OK 6000/6000 (byte-exact).
      Found: file = 6000*376 + 4-byte trailer; strings have trailing garbage after null -> preserved.
- [x] T3 QuestCsvService: read/write Quests.csv with backup. Finding: server reads CSV directly at boot (no recompile).
- [x] T4 Program.cs: minimal API, password auth, cash + quests endpoints, timestamped backups.
- [x] T5 Frontend: index.html + app.js + style.css (PT-BR). wwwroot embedded for true single-exe.
- [x] T6 Published publish/AikaPanel.exe (96 MB) + README. Verified: selftest, UI, edit+backup+restore.

## Verification gates
- T2 gate: `dotnet run --selftest` prints "ROUND-TRIP OK 6000/6000".
- T6 gate: published exe starts, /api/health responds, browser loads UI.
