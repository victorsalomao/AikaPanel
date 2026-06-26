# AikaPanel

**Painel web de administração para servidores Aika Online (emulador Delphi).**
Edita os arquivos de dados do servidor — e os arquivos **cifrados do client** —
com saída **byte-exata** (idêntica à que o servidor/cliente já esperam), por uma
interface amigável em português. Tudo num único `.exe` self-contained (.NET 8).

> Por que "byte-exato"? Os formatos do Aika são `packed records` Delphi. O painel
> lê o registro, reescreve **só os campos que você muda** e preserva todo o resto
> byte a byte — então nunca corrompe campos desconhecidos. Cada módulo tem um
> `--selftest` que prova isso (ler → reserializar → comparar == original).

---

## ✨ Recursos

| Aba | Arquivo(s) | O que faz |
|---|---|---|
| **Cash Shop** | `Data\PI.bin` + `UI\PI.bin` (cifrado) | Edita a loja de cash (nome, preço, item entregue, qtd, visível) e **sincroniza a vitrine cifrada do client** |
| **Itens** | `Data\ItemList.bin` + `ItemList4.bin` (cifrado) | Editor dos 31000 itens (nome PT/EN, tipo, level, preços, atributos, IconID) + sync do client v4 |
| **Skills** | `Data\SkillData.bin` + `SkillData4.bin` (cifrado) | Editor das skills (dano, MP, cooldown, alcance, custo…) + sync do client v4 |
| **NPCs** | `Data\NPCs\*.npc` | Edita loja, posição e título do NPC; clona/cria NPC; mostra o **nome in-game** e sinaliza NPCs duplicados |
| **Contas** | MySQL `caelite` | Cria/edita contas e personagens (itens, stats, gold, nação…) direto no banco |
| **Itens Evento (T)** | `Data\ItemList.bin` + DB | Marca itens como "evento" (tecla T) e entrega para jogadores |
| **Quests** | `Data\Quest\Quests.csv` | Edita as quests lidas pelo servidor no boot |
| **Ícones** | `UI\ItemIcons*.jit` | Navegador de ícones do client (atlas JIT → sprites) |

---

## 📋 Pré-requisitos

- **Windows x64** (o exe é `win-x64` self-contained — não precisa instalar .NET pra rodar).
- Para **compilar**: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).
- Os **arquivos de dados** do seu servidor Aika (a pasta `Bin\Data`).
- Para a aba **Contas**: um **MySQL** com o banco do servidor (`caelite` por padrão).
- (Opcional) a pasta do **client** Aika, para o painel sincronizar os arquivos cifrados
  (Cash Shop / Itens / Skills) que o client lê.

---

## ⚙️ Configuração

O painel lê um `appsettings.json` ao lado do `.exe`:

```json
{
  "AikaPanel": {
    "DataDir": "C:\\caminho\\para\\AikaServer\\Bin\\Data",
    "ClientUIDir": "C:\\caminho\\para\\Client\\UI",
    "ClientGameDir": "C:\\caminho\\para\\Client",
    "Host": "127.0.0.1",
    "Port": 8099,
    "Password": "troque-esta-senha",
    "Mysql": {
      "Server": "localhost",
      "Port": 3306,
      "Database": "caelite",
      "User": "root",
      "Password": "sua-senha-do-mysql"
    }
  }
}
```

| Campo | Descrição |
|---|---|
| `DataDir` | Pasta `Data` do **servidor** (`PI.bin`, `ItemList.bin`, `SkillData.bin`, `NPCs\`, `Quest\Quests.csv`). **Obrigatório.** |
| `ClientUIDir` | Pasta `UI` do **client** (vitrine cifrada `UI\PI.bin` + atlas de ícones). Vazio (`""`) desativa o sync. |
| `ClientGameDir` | **Raiz** do client (`ItemList4.bin`, `SkillData4.bin` cifrados v4). Vazio (`""`) desativa o sync. |
| `Host` / `Port` | Onde o painel escuta. `127.0.0.1` = só esta máquina (recomendado). |
| `Password` | Senha de login do painel. **Troque sempre.** |
| `Mysql` | Conexão da aba **Contas**. **Opcional** — se omitido, o painel tenta ler a seção `[MySQL]` do `Bin\AikaServer.ini` (ao lado de `Data`) automaticamente. |

> 🔒 **Segurança:** o `appsettings.json` contém senhas — ele **não vai pro repositório**
> (está no `.gitignore` na pasta `publish/`). O `src/appsettings.json` versionado é só um
> modelo sem credenciais reais.

---

## ▶️ Como rodar (local)

1. Edite o `appsettings.json` (no mínimo `DataDir` e `Password`).
2. Dê duplo-clique em `AikaPanel.exe` (ou rode pelo terminal).
3. Abra `http://127.0.0.1:8099`, faça login com a `Password` e edite.

**Auto-teste (recomendado antes de confiar na UI):**

```bash
AikaPanel.exe --selftest
```

Lê todos os registros de `PI.bin`, `ItemList.bin`, `SkillData.bin`, `Title.bin` e os
`*.npc`, reserializa e compara byte a byte. Deve imprimir `ROUND-TRIP OK` em cada um.

---

## 🔨 Build (gerar o exe)

```bash
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../publish
```

Saída: `publish/AikaPanel.exe` (a interface web fica **embutida** no exe). Para
implantar, copie só **`AikaPanel.exe` + `appsettings.json`**.

> A interface é embutida como recurso (`EmbeddedResource wwwroot\**`); por isso
> qualquer mudança no front-end (`wwwroot/`) só chega ao usuário após um novo
> `dotnet publish`.

---

## ☁️ Deploy no VPS (Windows)

1. Copie `AikaPanel.exe` + `appsettings.json` para o VPS.
2. Ajuste `DataDir` (e `Mysql`, se for usar Contas) e ponha uma `Password` forte.
3. Para acesso externo, mude `Host` para `0.0.0.0` e **libere a porta no firewall**
   — ou, mais seguro, mantenha `127.0.0.1` e acesse por túnel SSH/RDP.
4. Rode o exe (numa janela ou como tarefa de inicialização).

---

## 🧩 Como cada módulo funciona

### Cash Shop (`PI.bin`)
- Registro **packed** de **376 bytes**, little-endian, strings Windows-1252. O arquivo tem
  6000 registros + 4 bytes de trailer (preservados). Campos editáveis: `show`, Nome, Descrição,
  Preço (cash), `ItemIndex` (item entregue), Quantidade. Todo o resto é preservado byte a byte.
- **Vitrine do client (`UI\PI.bin` cifrado):** o servidor lê o `Data\PI.bin` cru; o client lê
  `UI\PI.bin`, que é o **mesmo catálogo cifrado** (Key1). Por isso editar só o cru não fazia o
  item aparecer na loja. Ao salvar, o painel grava **os dois** (com backup do arquivo do client).
  A cifra replica `enc[j] = (raw[j] + Key1[j mod 102] + j) mod 256` — provada byte-idêntica
  (2.256.004/2.256.004). Badge de sync + botão **Sincronizar vitrine**.
- CLI: `AikaPanel.exe --cryptotest <Data\PI.bin> <UI\PI.bin>` · `--encrypt/--decrypt <in> <out>`.

### Itens (`ItemList.bin`)
- Registro **packed** de **464 bytes**, 31000 registros + trailer. ID = índice no array.
  Editáveis: Nome PT/EN, Descrição, ItemType, UseEffect, Level, IconID, Classe, raridade,
  tipo de troca, preços (Gold/Honor/Medal/SellPrince) e atributos (ATK/DEF Fís/Mag, HP, MP).
- **Sync do client v4 (`ItemList4.bin`):** header de 12 bytes em texto (`BR00022I`) + corpo
  cifrado (Key1, com `j` reiniciando no início do corpo). Ao salvar, grava o cru do server **e**
  reescreve o v4 do client. Badge de sync + botão. Provado byte-a-byte.

### Skills (`SkillData.bin`)
- Registro **packed** de **720 bytes**, 12000 registros + trailer. Editáveis: Nome PT/EN,
  Descrição, Level, Classe, SkillPoints, MP, Dano, Alcance, Taxa de sucesso, Cooldown,
  Cast time, Duração, etc. Sync do client `SkillData4.bin` (header `BR00010S` + corpo Key2).

### NPCs (`Data\NPCs\*.npc`)
- Cada `.npc` é um `TNPCFile` (`TNPCHeader` 558B + `TBasicNpc`), 5639 bytes. Offsets validados:
  Título @0, **Options[] (menu) @36**, Index @558, Equip @902, **loja (Inventory) @1226**,
  posição (X/Y Single) @4807/@4811, **name-id (nome in-game) @574**.
- Edita **loja** (itens que o NPC vende — só slots 0–39 aparecem no jogo), **posição** e
  **título**; **clona/cria** NPC; avisa quando a posição é fixada no código (`NPC.pas`).
- **Nome in-game vs nome do arquivo:** o nome que o jogador vê vem do **name-id** (resolvido
  pelo client), **não** do nome do arquivo. NPCs diferentes com o mesmo name-id aparecem
  idênticos no jogo — um pode ter a loja, o outro ser um "vazio". O painel mostra o name-id,
  marca duplicados e, ao abrir um vazio, **aponta o gêmeo que tem a loja** ("a loja in-game é
  a do NPC X").

### Contas (MySQL `caelite`)
- Cria/edita **contas** (usuário, senha MD5, e-mail, nação, tipo, cash) e **personagens**
  (nome, level, stats, gold, aparência, itens nos slots de equipamento/inventário/armazém).
- ⚠️ **Edite personagens com o char OFFLINE** — o servidor mantém o char logado em memória e
  sobrescreve o banco ao deslogar. Toda alteração faz snapshot em `Bin\panel_backups\*.json`.

### Itens de Evento (tecla T)
- Marca itens com `slot_type` de evento (acessível pela tecla **T** no client) e os entrega
  a jogadores. Integra com os comandos GM `/addeventitem[all]` do servidor.

### Quests (`Quests.csv`)
- O servidor lê `Data\Quest\Quests.csv` **direto no boot** (`InitQuests`) — não há compilação
  CSV→`Quest.bin`. Arquivo sem cabeçalho, 34 colunas por linha.

### Ícones (`UI\ItemIcons*.jit`)
- Navegador dos atlas de ícones do client (`ItemIcons01..11.jit`, 1024×1024, células 32×32).
  Há um conversor JIT→PNG em `tools/jit2png/`. *Obs.:* a fórmula exata `IconID → (atlas, célula)`
  ainda exige engenharia reversa do `AIKA.exe`, então o thumbnail por item não é embutido.

---

## 🛡️ Segurança e backups

- Antes de **qualquer** gravação, é criada uma cópia `*.AAAAMMDD-hhmmss.bak` na mesma pasta.
- `PI.bin` / `ItemList.bin` / `SkillData.bin` são validados (tamanho múltiplo do registro +
  trailer) e **nunca** gravam registro parcial.
- O acesso ao painel exige senha (token de sessão); deixe-o em `127.0.0.1` sempre que possível.

## 🔁 Reinicie o servidor para aplicar

Os dados (`PI.bin`, `ItemList.bin`, `SkillData.bin`, `NPCs`, `Quests.csv`) são carregados na
**inicialização** do servidor. Após salvar, **reinicie o servidor** (NPCs só recarregam com
restart completo; itens/skills/etc. têm comandos `reload*` no console do servidor).

---

## 🗂️ Estrutura do projeto

```
AikaPanel/
├── src/                     # código C# (.NET 8, ASP.NET minimal API)
│   ├── Program.cs           # entrypoint: auth, rotas, CLI (--selftest/--cryptotest/...)
│   ├── *Service.cs / *Repository.cs   # leitura/escrita byte-exata de cada formato
│   ├── CashCrypto.cs / V4Cipher.cs    # cifras do client (Key1/Key2, v4)
│   ├── Db.cs / AccountService.cs      # MySQL (aba Contas)
│   └── wwwroot/             # interface web (embutida no exe no publish)
├── tools/                   # utilitários de RE (jit2png, v4crack, memscan…)
└── publish/                 # saída do build (gitignored — contém appsettings com segredos)
```

---

## ⚠️ Aviso

Ferramenta **não-oficial**, feita por engenharia reversa dos formatos do Aika, para uso em
servidores próprios/privados. Não afiliada à Gala/Aika. Faça backup dos seus dados (o painel
já cria `.bak`, mas tenha os seus). Use por sua conta e risco.
