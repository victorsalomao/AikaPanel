# AikaPanel

Editor web local para o servidor Aika Online (Delphi). Edita dois arquivos de dados
com saída **byte-exata** (idêntica à que o servidor já espera) através de uma interface
amigável em português:

- **Cash Shop** — `Data\PI.bin` (6000 registros de 376 bytes)
- **Itens** — `Data\ItemList.bin` (31000 registros de 464 bytes)
- **Quests** — `Data\Quest\Quests.csv`

É um único `.exe` self-contained (.NET 8, win-x64). Não precisa instalar nada no VPS.

---

## Como rodar (local)

1. Edite `appsettings.json` (fica ao lado do `.exe`):

   ```json
   {
     "AikaPanel": {
       "DataDir": "C:\\Users\\user\\Downloads\\AikaMerged\\Bin\\Data",
       "Host": "127.0.0.1",
       "Port": 8099,
       "Password": "troque-esta-senha"
     }
   }
   ```

   - `DataDir` — pasta `Data` do servidor (onde estão `PI.bin` e `Quest\Quests.csv`).
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
