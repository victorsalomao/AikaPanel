# AikaPanel — Tasks

- [ ] T1 Scaffold: csproj (net8, single-file self-contained win-x64), appsettings.json, .gitignore. Commit.
- [ ] T2 PiBinService: decode/encode 376-byte records (Win-1252), self-test endpoint+CLI `--selftest`.
      VERIFY round-trip == original BEFORE building UI (REQ-06). Commit.
- [ ] T3 QuestCsvService: read/write Quests.csv with backup (REQ-07). Commit.
- [ ] T4 Program.cs: minimal API, password auth, cash + quests endpoints, backups (REQ-01/05). Commit.
- [ ] T5 Frontend: index.html + app.js + style.css (PT-BR, login, cash table, quests table). Commit.
- [ ] T6 Publish single exe + README. Verify exe runs. Commit.

## Verification gates
- T2 gate: `dotnet run --selftest` prints "ROUND-TRIP OK 6000/6000".
- T6 gate: published exe starts, /api/health responds, browser loads UI.
