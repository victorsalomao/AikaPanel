# AikaPanel

Editor web local para o servidor Aika Online (Delphi). Edita dois arquivos de dados
com saída **byte-exata** (idêntica à que o servidor já espera) através de uma interface
amigável em português:

- **Cash Shop** — `Data\PI.bin` (6000 registros de 376 bytes) + vitrine cifrada do client
- **Itens** — `Data\ItemList.bin` (31000 registros de 464 bytes) + `ItemList4.bin` cifrado do client
- **Skills** — `Data\SkillData.bin` (12000 registros de 720 bytes) + `SkillData4.bin` cifrado do client
- **Quests** — `Data\Quest\Quests.csv`

É um único `.exe` self-contained (.NET 8, win-x64). Não precisa instalar nada no VPS.

---

## Como rodar (local)

1. Edite `appsettings.json` (fica ao lado do `.exe`):

   ```json
   {
     "AikaPanel": {
       "DataDir": "C:\\Users\\user\\Downloads\\AikaMerged\\Bin\\Data",
       "ClientUIDir": "C:\\Users\\user\\Downloads\\AnotherAikaDelphi\\AIKAClient\\UI",
       "ClientGameDir": "C:\\Users\\user\\Downloads\\AnotherAikaDelphi\\AIKAClient",
       "Host": "127.0.0.1",
       "Port": 8099,
       "Password": "troque-esta-senha"
     }
   }
   ```

   - `DataDir` — pasta `Data` do servidor (`PI.bin`, `ItemList.bin`, `SkillData.bin`, `Quest\Quests.csv`).
   - `ClientUIDir` — pasta `UI` do **client** (onde fica o `PI.bin` CIFRADO da vitrine da loja).
     Ao salvar a Loja de Cash, o painel grava os DOIS: `Data\PI.bin` cru + `UI\PI.bin` cifrado.
   - `ClientGameDir` — **raiz** do client (onde ficam `ItemList4.bin` e `SkillData4.bin` cifrados v4).
     Ao salvar Itens/Skills, o painel grava o cru do server E o arquivo v4 cifrado do client.
   - Deixe `ClientUIDir`/`ClientGameDir` **vazios** (`""`) para desativar o sync do client.
   - `Host` / `Port` — onde o painel escuta. `127.0.0.1` = só esta máquina.
   - `Password` — senha de administrador exigida na tela de login. **Troque sempre.**

2. Dê um duplo-clique em `AikaPanel.exe` (ou rode pelo terminal).
3. Abra o navegador em `http://127.0.0.1:8099`.
4. Faça login com a senha do `appsettings.json` e edite.

**Auto-teste (opcional):** `AikaPanel.exe --selftest` lê os 6000 registros do `PI.bin`,
re-serializa e confere byte a byte. Deve imprimir `ROUND-TRIP OK 6000/6000`.

---

## Como implantar no VPS (Windows)

1. Copie para o VPS apenas **2 arquivos**: `AikaPanel.exe` e `appsettings.json`
   (o `.pdb` e o `web.config` não são necessários). A interface web está embutida no exe.
2. No `appsettings.json` do VPS:
   - ajuste `DataDir` para a pasta `Data` do servidor lá;
   - defina uma `Password` forte;
   - se for acessar de fora da máquina, mude `Host` para `0.0.0.0` e **libere a porta no
     firewall** (recomendado manter `127.0.0.1` e acessar via túnel/RDP por segurança).
3. Rode `AikaPanel.exe` (deixe numa janela ou crie um serviço/atalho na inicialização).
4. Abra `http://SEU_IP:8099`, faça login, edite.

---

## Como ele edita os arquivos

### Cash Shop (`PI.bin`)
- Cada item é um registro **packed** de **376 bytes**, little-endian, strings AnsiChar
  (Windows-1252) terminadas em nulo. O arquivo tem 6000 registros + 4 bytes finais
  (checksum/trailer) que são **preservados intactos**.
- Campos editáveis: **show** (1=visível/0=oculto), **Nome**, **Descrição**, **Preço (cash)**,
  **ItemIndex** (id do item entregue), **Quantidade**.
- Todos os outros bytes (status, trade, price_off, unk*, Amount_2, trailer) são **preservados
  byte a byte** — só os campos que você muda são reescritos.
- **Prova de exatidão:** ler todos os 6000 registros e regravá-los sem alteração produz um
  arquivo idêntico ao original (`--selftest` → `ROUND-TRIP OK 6000/6000`).

#### Vitrine do client (`UI\PI.bin` cifrado) — sincronização automática
- O servidor lê o `Data\PI.bin` **cru**; o **client** lê a vitrine de `UI\PI.bin`, que é o
  **mesmo catálogo, porém CIFRADO** (Key1 do MasterEditor). Por isso editar só o `Data\PI.bin`
  **não fazia o item aparecer na loja** — faltava atualizar a versão cifrada do client.
- Agora, ao salvar a Loja de Cash, o painel grava os **dois**: `Data\PI.bin` (cru) **e**
  `UI\PI.bin` (cifrado), gerado dos mesmos bytes. Faz **backup do arquivo do client** antes.
- A cifra replica **exatamente** `TFunctions.SaveEncriptedFileKey1` (MasterEditor `Functions.pas`):
  `enc[j] = (raw[j] + cipher[j mod 102] + j) mod 256`, com a tabela Key1 (102 bytes). Provado
  byte-a-byte: `encrypt(decrypt(UI\PI.bin)) == UI\PI.bin` → **2.256.004/2.256.004 idêntico**, e
  `decrypt(UI\PI.bin)` produz texto legível (nomes em PT) igual ao `Data\PI.bin`.
- **Indicador de sync** no topo da aba Cash Shop: verde `✓ server ↔ client sincronizados` quando
  `encrypt(Data\PI.bin) == UI\PI.bin`; vermelho quando divergem. Botão **Sincronizar vitrine do
  client** força a regravação do `UI\PI.bin` a partir do `Data\PI.bin` atual (útil na 1ª vez,
  já que os snapshots costumam estar defasados).
- Verificação por linha de comando: `AikaPanel.exe --cryptotest <Data\PI.bin> <UI\PI.bin>`
  (compara encrypt/decrypt byte-a-byte). Também há `--encrypt`/`--decrypt <in> <out>`.

### Itens (`ItemList.bin`)
- Cada item é um registro **packed** de **464 bytes** (`TItemFromList` em
  `Src\Data\FilesData.pas`), little-endian, strings Windows-1252. O arquivo tem 31000
  registros + 4 bytes finais (trailer) **preservados intactos**.
- ID = índice do registro no array (0..30999).
- Campos editáveis: Nome (PT), Nome (EN), Descrição, ItemType, UseEffect, Level, IconID,
  Classe, TypeItem (raridade), TypeTrade, preços (PriceGold/Honor/Medal/SellPrince,
  TypePriceItem/Value) e atributos (ATKFis/DefFis/MagATK/DefMag/HP/MP).
- Todo byte não editado é preservado (mesma técnica do `PI.bin`).
- **Prova de exatidão:** `--selftest` → `ItemList.bin ROUND-TRIP OK 31000/31000`.
- **Ícones:** o `IconID` é mostrado/editado como número. O atlas de ícones é
  `AIKAClient\UI\ItemIcons01..11.jit` (1024×1024, células de 32×32 = 1024 ícones por atlas).
  Existe um conversor JIT→PNG funcional em `tools\jit2png\` (uso:
  `dotnet run -- <arquivo.jit> <saida.png> [maxSize]`). **Pendente:** a fórmula exata
  IconID→(atlas,célula) **não** é o mapeamento sequencial simples (`atlas=IconID/1024`) —
  a verificação falhou; a lógica de seleção do atlas está no `AIKA.exe` (sem fonte) e
  precisa de mais engenharia reversa. Por isso o thumbnail por item **não** foi embutido.

### Skills (`SkillData.bin`)
- Registro **packed** de **720 bytes** (`T_SkillData` em `Src\Data\FilesData.pas`), 12000 registros + 4
  de trailer. ID = índice no array. Win-1252; Nome(PT)@84, Nome(EN)@20, Descrição@428.
- Editáveis: Nome PT/EN, Descrição, Level, MinLevel, Classe, Classification, SkillPoints, LearnCosts,
  MP, Damage, Range, SuccessRate, MaxTargets, Cooldown, CastTime, Duration, TargetType.
- **Prova:** `--selftest` → `SkillData.bin ROUND-TRIP OK 12000/12000`.

### Sincronização do client v4 (`ItemList4.bin` / `SkillData4.bin`)
- O **server** lê o cru (`Data\ItemList.bin`, `Data\SkillData.bin`); o **client** lê versões CIFRADAS
  (`ItemList4.bin`, `SkillData4.bin` na raiz do client). Editar só o cru não muda o que o client mostra.
- **Esquema v4 (engenharia reversa, provado byte-a-byte):** cada arquivo do client =
  **header de 12 bytes em texto** (`"BR00022I"` no ItemList, `"BR00010S"` no SkillData) **+** corpo
  cifrado com a cifra posicional do MasterEditor, mas com o `j` **reiniciando em 0 no início do corpo**
  (após o header). ItemList usa **Key1**, SkillData usa **Key2**.
  - `enc[j] = (raw[j] + tabela[j mod len] + j) mod 256` sobre o corpo (cru = arquivo inteiro do server).
  - Prova: `encrypt(decrypt(corpo)) == corpo` byte-idêntico nos dois; e `decrypt(SkillData4) == Data\SkillData.bin`
    100% (zero drift). Nomes decifrados legíveis ("Pedra da Pran", "Ataque Poderoso", "Revitalizar").
- Ao salvar um Item/Skill, o painel grava o cru do server E reescreve o arquivo v4 do client =
  **header existente preservado + Encrypt(cru do server)** (backup do arquivo do client antes).
- **Badge de sync** em cada aba + botão **Sincronizar client**. O ItemList do client costuma começar
  defasado (snapshot mais antigo) → badge vermelho até o 1º sync; depois fica verde. O SkillData já
  vinha idêntico ao server (verde de cara).
- **Atenção:** sincronizar **sobrescreve o catálogo do client com o do server** (o client passa a
  espelhar o server). Itens ficam no mesmo índice — é atualização de valores, não embaralha.
- CLI de RE/prova: `tools\v4crack` (decifra um arquivo v4, mostra o Name de um registro e prova o
  round-trip): `dotnet run -- <ItemList4.bin> key1 12 464 100 0 64 [Data\ItemList.bin]`.

### Quests (`Quests.csv`)
- O servidor lê `Data\Quest\Quests.csv` **diretamente no boot** (`InitQuests` em `Load.pas`)
  e atribui as quests aos NPCs. **Não há compilação CSV→Quest.bin** — editar o CSV funciona.
- Arquivo sem cabeçalho, 34 colunas por linha (nomes derivados do código do servidor).
- O `Quest.bin` é uma estrutura **separada** e **não é tocada** por este painel.

### Segurança (sempre)
- Antes de **qualquer** gravação, é criada uma cópia com data/hora:
  `PI.bin.AAAAMMDD-hhmmss.bak` / `ItemList.bin.AAAAMMDD-hhmmss.bak` /
  `Quests.csv.AAAAMMDD-hhmmss.bak` na mesma pasta.
- `PI.bin` e `ItemList.bin` são validados (tamanho múltiplo do registro, +4 de trailer); recusa se inválido.
- Nunca grava registro parcial.

---

## ⚠️ Reinicie o servidor para aplicar

Tanto o `PI.bin` quanto o `Quests.csv` são carregados **na inicialização** do servidor Aika.
Depois de salvar no painel, **reinicie o servidor** (ou recarregue os dados) para as
mudanças entrarem em efeito. Os jogadores online só veem o novo Cash Shop / novas quests
após esse restart.

---

## Build (para regenerar o exe)

```bash
cd src
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o ../publish
```

Saída em `publish/AikaPanel.exe`.
