"use strict";
let TOKEN = "";
let cashPage = 1;
const PAGE = 50;
let questCols = [];
let questRows = [];
let itemPage = 1;
let itensLoaded = false;
let skillPage = 1;
let skillsLoaded = false;

const $ = (id) => document.getElementById(id);

async function api(path, opts = {}) {
  opts.headers = Object.assign({ "Content-Type": "application/json" }, opts.headers || {});
  if (TOKEN) opts.headers["X-Auth-Token"] = TOKEN;
  const r = await fetch(path, opts);
  let body = null;
  try { body = await r.json(); } catch { body = { sucesso: false, mensagem: "Resposta inválida do servidor." }; }
  return { status: r.status, body };
}

/* ---------- LOGIN ---------- */
async function login() {
  $("loginErro").textContent = "";
  const senha = $("senha").value;
  const { status, body } = await api("/api/login", { method: "POST", body: JSON.stringify({ password: senha }) });
  if (status === 200 && body.sucesso) {
    TOKEN = body.dados.token;
    $("login").classList.add("hidden");
    $("app").classList.remove("hidden");
    await loadIconData();
    loadCash();
  } else {
    $("loginErro").textContent = body.mensagem || "Falha no login.";
  }
}

/* ---------- TABS ---------- */
function setTab(name) {
  document.querySelectorAll(".tab").forEach(t => t.classList.toggle("ativo", t.dataset.tab === name));
  $("tab-cash").classList.toggle("hidden", name !== "cash");
  $("tab-itens").classList.toggle("hidden", name !== "itens");
  $("tab-skills").classList.toggle("hidden", name !== "skills");
  $("tab-npcs").classList.toggle("hidden", name !== "npcs");
  $("tab-contas").classList.toggle("hidden", name !== "contas");
  $("tab-quests").classList.toggle("hidden", name !== "quests");
  if (name === "itens" && !itensLoaded) { itensLoaded = true; loadItens(); }
  if (name === "skills" && !skillsLoaded) { skillsLoaded = true; loadSkills(); }
  if (name === "npcs" && !npcsLoaded) { npcsLoaded = true; loadNpcs(); }
  if (name === "contas" && !contasLoaded) { contasLoaded = true; loadContas(); }
  $("tab-evento").classList.toggle("hidden", name !== "evento");
  if (name === "evento" && !eventoLoaded) { eventoLoaded = true; loadEvento(); }
  if (name === "quests" && questRows.length === 0) loadQuests();
  $("tab-icones").classList.toggle("hidden", name !== "icones");
  if (name === "icones") { if (!iconsLoaded) iconsLoaded = true; initIconBrowser(); }
}

/* ---------- CASH ---------- */
async function loadCash() {
  const q = encodeURIComponent($("busca").value.trim());
  const onlyVis = $("soVisiveis").checked;
  const { body } = await api(`/api/cash?q=${q}&page=${cashPage}&limit=${PAGE}&onlyVisible=${onlyVis}`);
  if (!body.sucesso) { showMsg("cashMsg", body.mensagem, true); return; }
  const d = body.dados;
  $("cashStats").textContent = `${d.total} registros, ${d.visiveis} visíveis (show=1)` + (d.matched !== d.total ? ` — ${d.matched} no filtro` : "");
  const tb = $("tblCash").querySelector("tbody");
  tb.innerHTML = "";
  for (const it of d.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${it.slot}</td>
      <td>${it.indice}</td>
      <td>${esc(it.name)}</td>
      <td>${it.show === 1 ? '<span class="badge on">visível</span>' : '<span class="badge off">oculto</span>'}</td>
      <td>${it.price.toLocaleString("pt-BR")}</td>
      <td>${it.itemIndex}</td>
      <td>${it.amount}</td>
      <td><button class="primary" data-slot="${it.slot}">Editar</button></td>`;
    tr.querySelector("button").onclick = () => openEdit(it);
    tb.appendChild(tr);
  }
  const maxPag = Math.max(1, Math.ceil(d.matched / PAGE));
  $("cashPag").textContent = `Página ${d.page} de ${maxPag}`;
  $("cashPrev").disabled = d.page <= 1;
  $("cashNext").disabled = !d.tem_mais;
  loadSyncStatus();
}

async function loadSyncStatus() {
  const badge = $("syncBadge");
  const { body } = await api("/api/cash/syncstatus");
  if (!body.sucesso) { badge.className = "sync-badge na"; badge.textContent = "sync indisponível"; return; }
  const s = body.dados;
  if (!s.clientConfigured) { badge.className = "sync-badge na"; badge.textContent = "client não configurado"; $("btnSync").disabled = true; return; }
  $("btnSync").disabled = false;
  if (!s.clientExists) { badge.className = "sync-badge off"; badge.textContent = "UI\\PI.bin não encontrado"; }
  else if (s.inSync) { badge.className = "sync-badge ok"; badge.textContent = "✓ server ↔ client sincronizados"; }
  else { badge.className = "sync-badge off"; badge.textContent = "✗ dessincronizados"; }
  badge.title = (s.mensagem || "") + (s.clientPath ? `\n${s.clientPath}` : "");
}

async function syncClient() {
  showMsg("cashMsg", "Sincronizando vitrine do client...", false);
  const { body } = await api("/api/cash/sync", { method: "POST" });
  if (body.sucesso) showMsg("cashMsg", `${body.mensagem}` + (body.dados.clientBackup ? ` Backup client: ${body.dados.clientBackup}.` : ""), false);
  else showMsg("cashMsg", body.mensagem, true);
  loadSyncStatus();
}

function openEdit(it) {
  $("mSlot").value = it.slot;
  $("mIndice").value = it.indice;
  $("mNome").value = it.name;
  $("mDesc").value = it.descricao;
  $("mShow").value = String(it.show === 1 ? 1 : 0);
  $("mPreco").value = it.price;
  $("mItemIndex").value = it.itemIndex;
  $("mQtd").value = it.amount;
  $("modalErro").textContent = "";
  $("modal").classList.remove("hidden");
}

async function saveEdit() {
  $("modalErro").textContent = "";
  const slot = parseInt($("mSlot").value, 10);
  const payload = {
    show: parseInt($("mShow").value, 10),
    name: $("mNome").value,
    descricao: $("mDesc").value,
    price: parseInt($("mPreco").value || "0", 10),
    amount: parseInt($("mQtd").value || "0", 10),
    itemIndex: parseInt($("mItemIndex").value || "0", 10),
  };
  const { body } = await api(`/api/cash/${slot}`, { method: "POST", body: JSON.stringify(payload) });
  if (body.sucesso) {
    $("modal").classList.add("hidden");
    showMsg("cashMsg", `${body.mensagem} Backup server: ${body.dados.backup}.` + (body.dados.clientBackup ? ` Backup client: ${body.dados.clientBackup}.` : ""), false);
    loadCash();
  } else {
    $("modalErro").textContent = body.mensagem || "Erro ao salvar.";
  }
}

/* ---------- QUESTS ---------- */
async function loadQuests() {
  const { body } = await api("/api/quests");
  if (!body.sucesso) { showMsg("questMsg", body.mensagem, true); return; }
  questCols = body.dados.colunas;
  questRows = body.dados.linhas;
  const thr = $("tblQuests").querySelector("thead tr");
  thr.innerHTML = questCols.map(c => `<th>${c}</th>`).join("") + "<th></th>";
  renderQuests();
}

function renderQuests() {
  const filtro = ($("buscaQuest").value || "").trim();
  const tb = $("tblQuests").querySelector("tbody");
  tb.innerHTML = "";
  questRows.forEach((row, idx) => {
    if (filtro && !row.join(",").includes(filtro)) return;
    const tr = document.createElement("tr");
    tr.innerHTML = row.map((v, ci) =>
      `<td><input value="${esc(v)}" data-r="${idx}" data-c="${ci}" /></td>`).join("") +
      `<td><button class="btn-del" data-del="${idx}">x</button></td>`;
    tr.querySelectorAll("input").forEach(inp => {
      inp.onchange = () => { questRows[inp.dataset.r][inp.dataset.c] = inp.value; };
    });
    tr.querySelector("button").onclick = () => { questRows.splice(idx, 1); renderQuests(); };
    tb.appendChild(tr);
  });
}

function addQuest() {
  questRows.push(new Array(questCols.length).fill("0"));
  $("buscaQuest").value = "";
  renderQuests();
}

async function saveQuests() {
  const { body } = await api("/api/quests", { method: "POST", body: JSON.stringify({ linhas: questRows }) });
  if (body.sucesso) showMsg("questMsg", `Salvo (${body.dados.linhas} linhas). Backup: ${body.dados.backup}. Reinicie o servidor.`, false);
  else showMsg("questMsg", body.mensagem, true);
}

/* ---------- ITENS ---------- */
const ITEM_NUM = ["itemType","useEffect","level","iconID","classe","typeItem","typeTrade",
  "priceGold","priceHonor","priceMedal","sellPrince","typePriceItem","typePriceItemValue",
  "atkFis","defFis","magATK","defMag","hp","mp"];
const ITEM_MAP = { iItemType:"itemType", iUseEffect:"useEffect", iLevel:"level", iIconID:"iconID",
  iClasse:"classe", iTypeItem:"typeItem", iTypeTrade:"typeTrade", iPriceGold:"priceGold",
  iPriceHonor:"priceHonor", iPriceMedal:"priceMedal", iSellPrince:"sellPrince",
  iTypePriceItem:"typePriceItem", iTypePriceItemValue:"typePriceItemValue",
  iATKFis:"atkFis", iDefFis:"defFis", iMagATK:"magATK", iDefMag:"defMag", iHP:"hp", iMP:"mp" };

async function loadItens() {
  const q = encodeURIComponent($("buscaItem").value.trim());
  let url = `/api/itens?q=${q}&page=${itemPage}&limit=${PAGE}`;
  const minL = $("itemMinLevel").value.trim();
  const maxL = $("itemMaxLevel").value.trim();
  if (minL !== "") url += `&minLevel=${encodeURIComponent(minL)}`;
  if (maxL !== "") url += `&maxLevel=${encodeURIComponent(maxL)}`;
  const { body } = await api(url);
  if (!body.sucesso) { showMsg("itemMsg", body.mensagem, true); return; }
  const d = body.dados;
  $("itemStats").textContent = `${d.total} registros — ${d.matched} na lista`;
  const tb = $("tblItens").querySelector("tbody");
  tb.innerHTML = "";
  for (const it of d.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${iconHtml(it.iconID)}</td>
      <td>${it.id}</td>
      <td>${esc(it.name)}</td>
      <td>${esc(it.nameEnglish)}</td>
      <td>${it.itemType}</td>
      <td>${it.level}</td>
      <td>${it.priceGold.toLocaleString("pt-BR")}</td>
      <td>${it.iconID}</td>
      <td><button class="primary">Editar</button></td>`;
    tr.querySelector("button").onclick = () => openItemEdit(it);
    tb.appendChild(tr);
  }
  const maxPag = Math.max(1, Math.ceil(d.matched / PAGE));
  $("itemPag").textContent = `Página ${d.page} de ${maxPag}`;
  $("itemPrev").disabled = d.page <= 1;
  $("itemNext").disabled = !d.tem_mais;
  loadSyncBadge("/api/itens/syncstatus", "itemSyncBadge", "btnItemSync");
}

/* Generic v4 sync badge loader: server <-> client (encrypted) */
async function loadSyncBadge(statusPath, badgeId, btnId) {
  const badge = $(badgeId);
  const { body } = await api(statusPath);
  if (!body.sucesso) { badge.className = "sync-badge na"; badge.textContent = "sync indisponível"; return; }
  const s = body.dados;
  if (!s.configured) { badge.className = "sync-badge na"; badge.textContent = "client não configurado"; $(btnId).disabled = true; return; }
  $(btnId).disabled = false;
  if (!s.exists) { badge.className = "sync-badge off"; badge.textContent = "arquivo do client não encontrado"; }
  else if (s.inSync) { badge.className = "sync-badge ok"; badge.textContent = `✓ sincronizados (${s.header})`; }
  else { badge.className = "sync-badge off"; badge.textContent = `✗ dessincronizados (${s.header})`; }
  badge.title = (s.mensagem || "") + (s.clientPath ? `\n${s.clientPath}` : "");
}

async function syncV4(syncPath, statusPath, badgeId, btnId, msgId) {
  showMsg(msgId, "Sincronizando client...", false);
  const { body } = await api(syncPath, { method: "POST" });
  if (body.sucesso) showMsg(msgId, `${body.mensagem}` + (body.dados.clientBackup ? ` Backup client: ${body.dados.clientBackup}.` : ""), false);
  else showMsg(msgId, body.mensagem, true);
  loadSyncBadge(statusPath, badgeId, btnId);
}

function openItemEdit(it) {
  $("iId").textContent = `ID ${it.id}`;
  $("iSalvar").dataset.id = it.id;
  $("iNome").value = it.name;
  $("iNomeEn").value = it.nameEnglish;
  $("iDesc").value = it.descricao;
  for (const [el, key] of Object.entries(ITEM_MAP)) $(el).value = it[key];
  $("modalItemErro").textContent = "";
  refreshItemIconPreview();
  $("modalItem").classList.remove("hidden");
}

async function saveItemEdit() {
  $("modalItemErro").textContent = "";
  const id = parseInt($("iSalvar").dataset.id, 10);
  const payload = { name: $("iNome").value, nameEnglish: $("iNomeEn").value, descricao: $("iDesc").value };
  for (const [el, key] of Object.entries(ITEM_MAP)) payload[key] = parseInt($(el).value || "0", 10);
  const { body } = await api(`/api/itens/${id}`, { method: "POST", body: JSON.stringify(payload) });
  if (body.sucesso) {
    $("modalItem").classList.add("hidden");
    showMsg("itemMsg", `${body.mensagem} Backup server: ${body.dados.backup}.` + (body.dados.clientBackup ? ` Backup client: ${body.dados.clientBackup}.` : ""), false);
    loadItens();
  } else {
    $("modalItemErro").textContent = body.mensagem || "Erro ao salvar.";
  }
}

/* ---------- SKILLS ---------- */
const SKILL_MAP = { sMinLevel:"minLevel", sLevel:"level", sClassification:"classification",
  sClasse:"classe", sSkillPoints:"skillPoints", sLearnCosts:"learnCosts", sMP:"mp",
  sCooldown:"cooldown", sTargetType:"targetType", sMaxTargets:"maxTargets", sRange:"range",
  sSuccessRate:"successRate", sDamage:"damage", sDuration:"duration", sCastTime:"castTime" };

async function loadSkills() {
  const q = encodeURIComponent($("buscaSkill").value.trim());
  const { body } = await api(`/api/skills?q=${q}&page=${skillPage}&limit=${PAGE}`);
  if (!body.sucesso) { showMsg("skillMsg", body.mensagem, true); return; }
  const d = body.dados;
  $("skillStats").textContent = `${d.total} registros — ${d.matched} na lista`;
  const tb = $("tblSkills").querySelector("tbody");
  tb.innerHTML = "";
  for (const it of d.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${it.id}</td>
      <td>${esc(it.name)}</td>
      <td>${esc(it.nameEnglish)}</td>
      <td>${it.level}</td>
      <td>${it.classe}</td>
      <td>${it.mp}</td>
      <td>${it.cooldown}</td>
      <td>${it.damage}</td>
      <td><button class="primary">Editar</button></td>`;
    tr.querySelector("button").onclick = () => openSkillEdit(it);
    tb.appendChild(tr);
  }
  const maxPag = Math.max(1, Math.ceil(d.matched / PAGE));
  $("skillPag").textContent = `Página ${d.page} de ${maxPag}`;
  $("skillPrev").disabled = d.page <= 1;
  $("skillNext").disabled = !d.tem_mais;
  loadSyncBadge("/api/skills/syncstatus", "skillSyncBadge", "btnSkillSync");
}

function openSkillEdit(it) {
  $("sId").textContent = `ID ${it.id} (Index ${it.index})`;
  $("sSalvar").dataset.id = it.id;
  $("sNome").value = it.name;
  $("sNomeEn").value = it.nameEnglish;
  $("sDesc").value = it.descricao;
  for (const [el, key] of Object.entries(SKILL_MAP)) $(el).value = it[key];
  $("modalSkillErro").textContent = "";
  $("modalSkill").classList.remove("hidden");
}

async function saveSkillEdit() {
  $("modalSkillErro").textContent = "";
  const id = parseInt($("sSalvar").dataset.id, 10);
  const payload = { name: $("sNome").value, nameEnglish: $("sNomeEn").value, descricao: $("sDesc").value };
  for (const [el, key] of Object.entries(SKILL_MAP)) payload[key] = parseInt($(el).value || "0", 10);
  const { body } = await api(`/api/skills/${id}`, { method: "POST", body: JSON.stringify(payload) });
  if (body.sucesso) {
    $("modalSkill").classList.add("hidden");
    showMsg("skillMsg", `${body.mensagem} Backup server: ${body.dados.backup}.` + (body.dados.clientBackup ? ` Backup client: ${body.dados.clientBackup}.` : ""), false);
    loadSkills();
  } else {
    $("modalSkillErro").textContent = body.mensagem || "Erro ao salvar.";
  }
}

/* ---------- NPCs ---------- */
let npcsLoaded = false;
let npcList = [];        // summaries (for table + dropdowns)
let npcCities = [];      // {name,x,y,count}
let npcCurrent = null;   // detail being edited
let npcShopMap = {};     // slot -> {itemId, app, itemName, sellPrince}  (desired state)

async function loadNpcs() {
  const q = encodeURIComponent($("buscaNpc").value.trim());
  const { body } = await api(`/api/npcs?q=${q}`);
  if (!body.sucesso) { showMsg("npcMsg", body.mensagem, true); return; }
  const d = body.dados;
  npcList = d.itens;
  $("npcStats").textContent = `${d.total} NPCs` + (d.matched !== d.total ? ` — ${d.matched} no filtro` : "");
  const tb = $("tblNpcs").querySelector("tbody");
  tb.innerHTML = "";
  for (const n of d.itens) {
    const tr = document.createElement("tr");
    const hc = n.hardcodedPos ? ' <span class="badge off" title="Posição fixada no código (NPC.pas) — mover no arquivo não tem efeito">pos fixa</span>' : "";
    const dup = n.dupCount > 1 ? ` <span class="badge" title="Mesmo nome in-game compartilhado por ${n.dupCount} NPCs (aparecem iguais no jogo)">×${n.dupCount}</span>` : "";
    const twin = (n.shopCount === 0 && n.twinShopId) ? ` <span class="badge off" title="Este aqui está vazio — a loja que você vê no jogo é a do NPC ${n.twinShopId} (${n.twinShopCount} itens), que tem o mesmo nome in-game">loja em ${n.twinShopId}</span>` : "";
    tr.innerHTML = `
      <td>${n.id}</td>
      <td>${esc(n.name)}</td>
      <td>${esc(n.title)}</td>
      <td>${esc(n.nameId || "")}${dup}${twin}</td>
      <td>${n.x}, ${n.y}${hc}</td>
      <td>${n.shopCount}</td>
      <td><button class="primary">Editar</button></td>`;
    tr.querySelector("button").onclick = () => openNpcEdit(n.id);
    tb.appendChild(tr);
  }
  loadNpcCities();
  loadNpcSelftest();
}

async function loadNpcCities() {
  if (npcCities.length) return;
  const { body } = await api("/api/npcs/cities");
  if (body.sucesso) npcCities = body.dados.cidades;
}

async function loadNpcSelftest() {
  const b = $("npcSelftest");
  const { body } = await api("/api/npcs/selftest");
  if (!body.sucesso) { b.className = "sync-badge na"; b.textContent = "selftest indisponível"; return; }
  const s = body.dados;
  if (s.ok) { b.className = "sync-badge ok"; b.textContent = `✓ ${s.total} NPCs validados (round-trip)`; }
  else { b.className = "sync-badge off"; b.textContent = `✗ falha no arquivo #${s.firstMismatch}`; }
}

function fillCitySelect(sel) {
  sel.innerHTML = '<option value="">— escolher —</option>' +
    npcCities.map(c => `<option value="${c.x},${c.y}">${esc(c.name)} (${c.x}, ${c.y}) · ${c.count} NPCs</option>`).join("");
}
function fillNpcSelect(sel, placeholder, valueMode) {
  sel.innerHTML = `<option value="">${placeholder}</option>` +
    npcList.map(n => `<option value="${valueMode === "pos" ? n.x + "," + n.y : n.id}">[${n.id}] ${esc(n.name)}</option>`).join("");
}

async function openNpcEdit(id) {
  const { body } = await api(`/api/npcs/${id}`);
  if (!body.sucesso) { showMsg("npcMsg", body.mensagem, true); return; }
  npcCurrent = body.dados;
  $("nTitulo").textContent = `[${npcCurrent.id}] ${npcCurrent.name}`;
  $("nTitle").value = npcCurrent.title;
  $("nX").value = npcCurrent.x;
  $("nY").value = npcCurrent.y;
  const hc = $("nHardcoded");
  if (npcCurrent.hardcodedPos) { hc.classList.remove("hidden"); hc.textContent = "⚠ Posição deste NPC é fixada no código (NPC.pas). Mudar X/Y aqui não vai movê-lo sem recompilar o servidor. Loja e preço funcionam normal."; }
  else hc.classList.add("hidden");
  // Aviso de NPC duplicado: o nome in-game vem do name-id, não do arquivo. Se este parece vazio
  // mas tem um "gêmeo" com a loja, o jogador está vendo a loja do gêmeo.
  const tw = $("nTwins");
  const twins = npcCurrent.twins || [];
  if (twins.length) {
    const stocked = twins.filter(t => t.shopCount > 0).sort((a, b) => b.shopCount - a.shopCount);
    const lst = twins.map(t => `[${t.id}] ${esc(t.name)} (${t.shopCount} ${t.shopCount === 1 ? "item" : "itens"})`).join(", ");
    let msg = `ℹ Nome in-game <b>${esc(npcCurrent.nameId || "?")}</b> é compartilhado com: ${lst}. No jogo todos aparecem com o mesmo nome.`;
    if (npcCurrent.shopCount === 0 && stocked.length)
      msg += `<br><b>Esta loja está vazia</b> — a loja que você vê no jogo é a do <b>[${stocked[0].id}] ${esc(stocked[0].name)}</b> (${stocked[0].shopCount} itens). Edite esse para mexer no que ele vende.`;
    tw.innerHTML = msg; tw.classList.remove("hidden");
  } else tw.classList.add("hidden");
  fillCitySelect($("nCidade"));
  fillNpcSelect($("nPertoDe"), "— escolher NPC —", "pos");
  npcShopMap = {};
  for (const s of npcCurrent.shop) npcShopMap[s.slot] = { itemId: s.itemId, app: s.app, itemName: s.itemName, sellPrince: s.sellPrince };
  renderNpcShop();
  $("modalNpcErro").textContent = "";
  $("modalNpc").classList.remove("hidden");
}

function renderNpcShop() {
  const tb = $("tblNpcShop").querySelector("tbody");
  tb.innerHTML = "";
  const slots = Object.keys(npcShopMap).map(Number).sort((a, b) => a - b);
  if (!slots.length) { tb.innerHTML = '<tr><td colspan="5" class="muted">Loja vazia. Adicione itens acima.</td></tr>'; return; }
  for (const slot of slots) {
    const s = npcShopMap[slot];
    const tr = document.createElement("tr");
    const hidden = slot >= 40 ? ' <span class="badge off" title="Slots ≥40 não aparecem na loja in-game">não aparece</span>' : "";
    const blocked = s.sellPrince <= 1 ? ' <span class="badge off" title="Preço ≤1 impede a compra">não compra</span>' : "";
    tr.innerHTML = `
      <td>${slot}${hidden}</td>
      <td>${s.itemId}</td>
      <td>${esc(s.itemName || "")}${blocked}</td>
      <td><input type="number" min="0" value="${s.sellPrince}" data-price="${s.itemId}" style="max-width:130px" /></td>
      <td><button class="btn-del" title="Remover da loja">x</button></td>`;
    tr.querySelector("input").onchange = (e) => { s.sellPrince = parseInt(e.target.value || "0", 10); };
    tr.querySelector("button").onclick = () => { delete npcShopMap[slot]; renderNpcShop(); };
    tb.appendChild(tr);
  }
}

function npcAddItem() {
  const id = parseInt($("nAddItemId").value || "0", 10);
  if (id <= 0) { $("modalNpcErro").textContent = "Informe um ID de item válido."; return; }
  let free = -1;
  for (let s = 0; s < 40; s++) if (!(s in npcShopMap)) { free = s; break; }   // prefer visible slots
  if (free < 0) for (let s = 40; s < 64; s++) if (!(s in npcShopMap)) { free = s; break; }
  if (free < 0) { $("modalNpcErro").textContent = "Sem slots livres (64 cheios)."; return; }
  $("modalNpcErro").textContent = "";
  npcShopMap[free] = { itemId: id, app: id, itemName: "(novo — salve para ver o nome)", sellPrince: 0 };
  $("nAddItemId").value = "";
  renderNpcShop();
  if (free >= 40) $("modalNpcErro").textContent = "Aviso: adicionado no slot " + free + " (≥40) — não vai aparecer na loja. Remova itens dos slots 0–39.";
}

async function saveNpcEdit() {
  if (!npcCurrent) return;
  $("modalNpcErro").textContent = "";
  // 1) price changes -> ItemList (global por item)
  const priceJobs = [];
  for (const slot in npcShopMap) {
    const s = npcShopMap[slot];
    const orig = npcCurrent.shop.find(x => x.itemId === s.itemId);
    if (s.itemId > 0 && (!orig || orig.sellPrince !== s.sellPrince) && s.sellPrince > 0)
      priceJobs.push({ id: s.itemId, sellPrince: s.sellPrince });
  }
  for (const j of priceJobs) {
    const { body } = await api(`/api/itens/${j.id}/preco`, { method: "POST", body: JSON.stringify({ sellPrince: j.sellPrince }) });
    if (!body.sucesso) { $("modalNpcErro").textContent = `Falha ao salvar preço do item ${j.id}: ${body.mensagem}`; return; }
  }
  // 2) full 64-slot shop + title + pos
  const shop = [];
  for (let s = 0; s < 64; s++) {
    if (s in npcShopMap) shop.push({ slot: s, itemId: npcShopMap[s].itemId, app: npcShopMap[s].app || 0 });
    else shop.push({ slot: s, itemId: 0, app: 0 });
  }
  const payload = { title: $("nTitle").value, x: parseFloat($("nX").value || "0"), y: parseFloat($("nY").value || "0"), shop };
  const { body } = await api(`/api/npcs/${npcCurrent.id}`, { method: "POST", body: JSON.stringify(payload) });
  if (!body.sucesso) { $("modalNpcErro").textContent = body.mensagem || "Erro ao salvar."; return; }
  $("modalNpc").classList.add("hidden");
  let msg = `${body.mensagem} Backup: ${body.dados.backup}.`;
  if (priceJobs.length) msg += ` ${priceJobs.length} preço(s) atualizado(s) no ItemList.`;
  if (body.dados.avisos && body.dados.avisos.length) msg += " ⚠ " + body.dados.avisos.join(" ");
  showMsg("npcMsg", msg, false);
  npcCities = [];
  loadNpcs();
}

async function npcFreeSpot() {
  const x = parseFloat($("nX").value || "0"), y = parseFloat($("nY").value || "0");
  if (!x || !y) { $("modalNpcErro").textContent = "Defina X e Y (ou escolha uma cidade) primeiro."; return; }
  const { body } = await api(`/api/npcs/freespot?x=${x}&y=${y}`);
  if (body.sucesso) { $("nX").value = body.dados.x; $("nY").value = body.dados.y; }
}

async function npcDelete() {
  if (!npcCurrent) return;
  if (!confirm(`Excluir o NPC [${npcCurrent.id}] ${npcCurrent.name}? (um backup .bak é criado)`)) return;
  const { body } = await api(`/api/npcs/${npcCurrent.id}`, { method: "DELETE" });
  if (!body.sucesso) { $("modalNpcErro").textContent = body.mensagem; return; }
  $("modalNpc").classList.add("hidden");
  showMsg("npcMsg", body.mensagem, false);
  npcCities = [];
  loadNpcs();
}

/* create / clone */
async function openNpcCreate() {
  await loadNpcCities();
  fillNpcSelect($("cSource"), "— escolher NPC base —", "id");
  fillCitySelect($("cCidade"));
  const { body } = await api("/api/npcs/nextid");
  $("cId").value = body.sucesso && body.dados.id > 0 ? body.dados.id : "";
  $("cNome").value = ""; $("cTitle").value = ""; $("cX").value = ""; $("cY").value = "";
  $("modalNpcNovoErro").textContent = "";
  $("modalNpcNovo").classList.remove("hidden");
}

async function npcCreate() {
  $("modalNpcNovoErro").textContent = "";
  const dto = {
    sourceId: parseInt($("cSource").value || "0", 10),
    newId: parseInt($("cId").value || "0", 10),
    name: $("cNome").value.trim(),
    title: $("cTitle").value,
    x: parseFloat($("cX").value || "0"),
    y: parseFloat($("cY").value || "0"),
  };
  if (!dto.sourceId) { $("modalNpcNovoErro").textContent = "Escolha um NPC base para clonar."; return; }
  if (!dto.name) { $("modalNpcNovoErro").textContent = "Informe o nome."; return; }
  const { body } = await api("/api/npcs", { method: "POST", body: JSON.stringify(dto) });
  if (!body.sucesso) { $("modalNpcNovoErro").textContent = body.mensagem; return; }
  $("modalNpcNovo").classList.add("hidden");
  let msg = body.mensagem;
  if (body.dados.avisos && body.dados.avisos.length) msg += " ⚠ " + body.dados.avisos.join(" ");
  showMsg("npcMsg", msg, false);
  npcCities = [];
  loadNpcs();
}

/* ---------- CONTAS / PERSONAGENS ---------- */
let contasLoaded = false;
let contaPage = 1;
const NATIONS = { 1: "Elsinore", 2: "Odeon", 3: "Tibérica" };
const EQUIP_LABELS = ["Rosto", "Cabelo", "Armadura", "Calça", "Luvas", "Botas", "Arma", "Escudo",
  "Capacete", "Manto/Asa", "Brinco", "Colar", "Anel", "Anel 2", "Bracelete", "Slot 15"];
let charCurrent = null;   // character detail being edited
let charItems = [];       // all ItemRows for the char
let invTab = 1;           // 1 inventory, 2 storage

async function loadDbBadge() {
  const b = $("dbBadge");
  const { body } = await api("/api/db/ping");
  if (body.sucesso) { b.className = "sync-badge ok"; b.textContent = "✓ DB " + body.dados.msg; }
  else { b.className = "sync-badge off"; b.textContent = "✗ DB offline: " + (body.mensagem || ""); }
}

async function loadContas() {
  const q = encodeURIComponent($("buscaConta").value.trim());
  const { body } = await api(`/api/accounts?q=${q}&page=${contaPage}&limit=${PAGE}`);
  if (!body.sucesso) { showMsg("contaMsg", body.mensagem, true); return; }
  const d = body.dados;
  $("contaStats").textContent = `${d.total} contas` + (d.matched !== d.total ? ` — ${d.matched} no filtro` : "");
  const tb = $("tblContas").querySelector("tbody");
  tb.innerHTML = "";
  for (const a of d.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${a.id}</td>
      <td>${esc(a.username)}</td>
      <td>${esc(a.mail)}</td>
      <td>${a.accountType}${a.accountType >= 5 ? ' <span class="badge on">GM</span>' : ""}</td>
      <td>${NATIONS[a.nation] || a.nation}</td>
      <td>${a.cash.toLocaleString("pt-BR")}</td>
      <td>${a.charCount}</td>
      <td><button class="primary">Editar</button></td>`;
    tr.querySelector("button").onclick = () => openContaEdit(a.id);
    tb.appendChild(tr);
  }
  const maxPag = Math.max(1, Math.ceil(d.matched / PAGE));
  $("contaPag").textContent = `Página ${d.page} de ${maxPag}`;
  $("contaPrev").disabled = d.page <= 1;
  $("contaNext").disabled = !d.tem_mais;
  loadDbBadge();
}

async function openContaEdit(id) {
  const { body } = await api(`/api/accounts/${id}`);
  if (!body.sucesso) { showMsg("contaMsg", body.mensagem, true); return; }
  const a = body.dados;
  $("aSalvar").dataset.id = a.id;
  $("aTitulo").textContent = `[${a.id}] ${a.username}`;
  $("aUser").value = a.username; $("aMail").value = a.mail; $("aNation").value = String(a.nation || 1);
  $("aType").value = a.accountType; $("aStatus").value = a.accountStatus; $("aActive").value = a.isActive;
  $("aBan").value = a.banDays; $("aCash").value = a.cash; $("aStorage").value = a.storageGold;
  $("aPremium").value = a.premiumTime; $("aDiscord").value = a.discord; $("aNewPass").value = "";
  const tb = $("tblAccChars").querySelector("tbody"); tb.innerHTML = "";
  if (!a.characters.length) tb.innerHTML = '<tr><td colspan="7" class="muted">Sem personagens.</td></tr>';
  for (const c of a.characters) {
    const tr = document.createElement("tr");
    tr.innerHTML = `<td>${c.id}</td><td>${esc(c.name)}${c.deleted ? ' <span class="badge off">del</span>' : ""}</td>
      <td>${c.slot}</td><td>${c.level}</td><td>${c.classInfo}</td><td>${c.gold.toLocaleString("pt-BR")}</td>
      <td><button class="ghost">Editar char</button></td>`;
    tr.querySelector("button").onclick = () => openCharEdit(c.id);
    tb.appendChild(tr);
  }
  $("modalContaErro").textContent = "";
  $("modalConta").classList.remove("hidden");
}

async function saveContaEdit() {
  $("modalContaErro").textContent = "";
  const id = parseInt($("aSalvar").dataset.id, 10);
  const payload = {
    username: $("aUser").value, mail: $("aMail").value, nation: parseInt($("aNation").value, 10),
    accountType: parseInt($("aType").value || "0", 10), accountStatus: parseInt($("aStatus").value || "0", 10),
    isActive: $("aActive").value, banDays: parseInt($("aBan").value || "0", 10),
    cash: parseInt($("aCash").value || "0", 10), storageGold: parseInt($("aStorage").value || "0", 10),
    premiumTime: $("aPremium").value, discord: $("aDiscord").value,
  };
  if ($("aNewPass").value) payload.newPassword = $("aNewPass").value;
  const { body } = await api(`/api/accounts/${id}`, { method: "POST", body: JSON.stringify(payload) });
  if (!body.sucesso) { $("modalContaErro").textContent = body.mensagem; return; }
  $("modalConta").classList.add("hidden");
  showMsg("contaMsg", `${body.mensagem} Backup: ${body.dados.backup}.`, false);
  loadContas();
}

function openContaNova() {
  $("ncUser").value = ""; $("ncPass").value = ""; $("ncMail").value = "";
  $("ncNation").value = "1"; $("ncType").value = "0"; $("ncCash").value = "0";
  $("modalContaNovaErro").textContent = "";
  $("modalContaNova").classList.remove("hidden");
}

async function criarConta() {
  $("modalContaNovaErro").textContent = "";
  const dto = {
    username: $("ncUser").value.trim(), password: $("ncPass").value, mail: $("ncMail").value.trim(),
    nation: parseInt($("ncNation").value, 10), accountType: parseInt($("ncType").value || "0", 10),
    cash: parseInt($("ncCash").value || "0", 10),
  };
  if (!dto.username || !dto.password) { $("modalContaNovaErro").textContent = "Usuário e senha são obrigatórios."; return; }
  const { body } = await api("/api/accounts", { method: "POST", body: JSON.stringify(dto) });
  if (!body.sucesso) { $("modalContaNovaErro").textContent = body.mensagem; return; }
  $("modalContaNova").classList.add("hidden");
  showMsg("contaMsg", body.mensagem, false);
  loadContas();
}

/* ---- character ---- */
const CHAR_MAP = { chName: "name", chSlot: "slot", chLevel: "level", chClass: "classInfo", chExp: "experience",
  chTitle: "activeTitle", chStr: "strength", chAgi: "agility", chInt: "intelligence", chCon: "constitution",
  chLuck: "luck", chStatus: "status", chGold: "gold", chHp: "curHp", chMp: "curMp", chHonor: "honor",
  chKill: "killpoint", chSkill: "skillpoint", chGuild: "guildIndex", chPosx: "posx", chPosy: "posy",
  chAltura: "altura", chTronco: "tronco", chPerna: "perna", chCorpo: "corpo" };

/* ---- character titles ---- */
let ALL_TITLES = null;   // Map<int, TitleData> (defs do Title.bin)
let EF_NAMES = null;     // { code: "NOME" }
let charTitles = [];     // [{ index, level, progress, name }] editável

async function ensureTitleDefs() {
  if (!ALL_TITLES) {
    const { body } = await api("/api/titles");
    ALL_TITLES = new Map();
    if (body.sucesso) for (const t of body.dados.itens) ALL_TITLES.set(t.index, t);
  }
  if (!EF_NAMES) {
    const { body } = await api("/api/titles/ef-names");
    EF_NAMES = body.sucesso ? body.dados : {};
  }
}

function efName(code) { return (EF_NAMES && EF_NAMES[code]) ? EF_NAMES[code] : ("EF " + code); }

// Resumo dos atributos de um título num dado nível (1..4). Usa as defs do Title.bin.
function attrResumo(titleDef, level) {
  if (!titleDef) return "";
  const lvl = titleDef.levels[(level || 1) - 1];
  if (!lvl) return "";
  const parts = [];
  for (let i = 0; i < 3; i++) {
    const ef = lvl.ef[i], v = lvl.efv[i];
    if (ef && ef !== 0) parts.push(`${efName(ef)} +${v}`);
  }
  return parts.join(", ") || "—";
}

async function loadCharTitles(id) {
  await ensureTitleDefs();
  const { body } = await api(`/api/characters/${id}/titles`);
  charTitles = body.sucesso ? body.dados.itens.map(t => ({ ...t })) : [];
  // popular o select "conceder" com todos os 255 títulos (idx>0 e com nome)
  const add = $("chTitleAdd");
  add.innerHTML = "";
  if (ALL_TITLES) {
    for (const [idx, t] of [...ALL_TITLES.entries()].sort((a, b) => a[0] - b[0])) {
      if (idx === 0 || !t.name) continue;
      const o = document.createElement("option");
      o.value = idx; o.textContent = `${idx} — ${t.name}`;
      add.appendChild(o);
    }
  }
  renderCharTitles();
}

function renderCharTitles() {
  const tb = $("tblCharTitles").querySelector("tbody");
  tb.innerHTML = "";
  if (!charTitles.length) {
    tb.innerHTML = '<tr><td colspan="6" class="muted">Sem títulos.</td></tr>';
  }
  for (const t of charTitles) {
    const def = ALL_TITLES ? ALL_TITLES.get(t.index) : null;
    const tr = document.createElement("tr");
    let lvlOpts = "";
    for (let l = 1; l <= 4; l++) lvlOpts += `<option value="${l}"${t.level === l ? " selected" : ""}>${l}</option>`;
    tr.innerHTML =
      `<td>${t.index}</td>` +
      `<td>${esc(t.name || (def ? def.name : "") || ("#" + t.index))}</td>` +
      `<td class="muted">${esc(attrResumo(def, t.level))}</td>` +
      `<td><select class="ct-level">${lvlOpts}</select></td>` +
      `<td><input class="ct-prog" type="number" min="0" value="${t.progress}" style="width:90px" /></td>` +
      `<td><button type="button" class="ghost danger ct-del">remover</button></td>`;
    tr.querySelector(".ct-level").onchange = e => { t.level = parseInt(e.target.value, 10); renderCharTitles(); refreshActiveTitleSelect(); };
    tr.querySelector(".ct-prog").onchange = e => { t.progress = parseInt(e.target.value || "0", 10); };
    tr.querySelector(".ct-del").onclick = () => { charTitles = charTitles.filter(x => x.index !== t.index); renderCharTitles(); refreshActiveTitleSelect(); };
    tb.appendChild(tr);
  }
  refreshActiveTitleSelect();
}

// Select "Título ativo" = (nenhum) + títulos possuídos. Preserva a seleção atual quando possível.
function refreshActiveTitleSelect() {
  const sel = $("chTitle");
  const cur = parseInt(sel.value || (charCurrent ? charCurrent.activeTitle : 0) || 0, 10);
  sel.innerHTML = '<option value="0">(nenhum)</option>';
  for (const t of charTitles) {
    const o = document.createElement("option");
    o.value = t.index; o.textContent = `${t.index} — ${esc(t.name || ("#" + t.index))}`;
    sel.appendChild(o);
  }
  // garante que o active atual apareça mesmo se não estiver na lista possuída
  if (cur && !charTitles.some(t => t.index === cur)) {
    const o = document.createElement("option");
    o.value = cur; o.textContent = `${cur} — (não possuído)`;
    sel.appendChild(o);
  }
  sel.value = String(cur);
}

function addCharTitle() {
  const idx = parseInt($("chTitleAdd").value || "0", 10);
  if (idx <= 0) return;
  if (charTitles.some(t => t.index === idx)) { renderCharTitles(); return; }
  const def = ALL_TITLES ? ALL_TITLES.get(idx) : null;
  charTitles.push({ index: idx, level: 1, progress: 0, name: def ? def.name : ("#" + idx) });
  charTitles.sort((a, b) => a.index - b.index);
  renderCharTitles();
}

async function saveCharTitles() {
  if (!charCurrent) return;
  const id = parseInt($("chSalvar").dataset.id, 10);
  const payload = charTitles.map(t => ({ index: t.index, level: t.level, progress: t.progress }));
  const { body } = await api(`/api/characters/${id}/titles`, { method: "POST", body: JSON.stringify(payload) });
  if (!body.sucesso) { $("modalCharErro").textContent = body.mensagem; return; }
  showMsg("contaMsg", `${body.mensagem} Backup: ${body.dados.backup}.`, false);
  await loadCharTitles(id);
}

async function openCharEdit(id) {
  const { body } = await api(`/api/characters/${id}`);
  if (!body.sucesso) { showMsg("contaMsg", body.mensagem, true); return; }
  charCurrent = body.dados;
  $("chSalvar").dataset.id = id;
  $("chTitulo").textContent = `[${charCurrent.id}] ${charCurrent.name}`;
  await loadCharTitles(id);                 // popula #chTitle (select) e a tabela de títulos
  for (const [el, key] of Object.entries(CHAR_MAP)) $(el).value = charCurrent[key];
  refreshActiveTitleSelect();               // re-seleciona o active após o set genérico
  $("chPk").value = charCurrent.playerKill ? "1" : "0";
  $("chDeleted").value = charCurrent.deleted ? "1" : "0";
  await loadCharItems(id);
  $("modalCharErro").textContent = "";
  $("modalChar").classList.remove("hidden");
}

async function loadCharItems(id) {
  const { body } = await api(`/api/characters/${id}/items`);
  charItems = body.sucesso ? body.dados.itens : [];
  renderPaperDoll();
  renderInvGrid();
}

function itemAt(slotType, slot) { return charItems.find(i => i.slotType === slotType && i.slot === slot); }

function renderPaperDoll() {
  const pd = $("paperDoll"); pd.innerHTML = "";
  for (let s = 0; s < 16; s++) {
    const it = itemAt(0, s);
    const cell = document.createElement("div");
    cell.className = "doll-slot" + (it ? " filled" : "");
    cell.innerHTML = `<div class="doll-label">${EQUIP_LABELS[s]}</div>` +
      (it ? `<div class="doll-item" title="ID ${it.itemId}${it.refine ? " +ref " + it.refine : ""}">${esc(it.itemName || ("#" + it.itemId))}</div>`
          : `<div class="doll-empty">vazio</div>`);
    cell.onclick = () => openCharItem(0, s, it);
    pd.appendChild(cell);
  }
}

function renderInvGrid() {
  const g = $("invGrid"); g.innerHTML = "";
  const rows = charItems.filter(i => i.slotType === invTab).sort((a, b) => a.slot - b.slot);
  const addBtn = document.createElement("button");
  addBtn.className = "primary inv-add"; addBtn.textContent = "+ adicionar item";
  addBtn.onclick = () => {
    let free = 0; const used = new Set(rows.map(r => r.slot));
    while (used.has(free)) free++;
    openCharItem(invTab, free, null);
  };
  g.appendChild(addBtn);
  if (!rows.length) { const e = document.createElement("div"); e.className = "muted"; e.textContent = " nenhum item."; g.appendChild(e); }
  for (const it of rows) {
    const card = document.createElement("div");
    card.className = "inv-card";
    card.innerHTML = `<div class="inv-slot">#${it.slot}</div>
      <div class="inv-name" title="ID ${it.itemId}">${esc(it.itemName || ("#" + it.itemId))}</div>
      <div class="inv-meta">id ${it.itemId}${it.refine ? " · +" + it.refine : ""}</div>`;
    card.onclick = () => openCharItem(invTab, it.slot, it);
    g.appendChild(card);
  }
}

async function saveCharEdit() {
  if (!charCurrent) return;
  $("modalCharErro").textContent = "";
  const id = parseInt($("chSalvar").dataset.id, 10);
  const payload = {};
  for (const [el, key] of Object.entries(CHAR_MAP)) {
    const v = $(el).value;
    payload[key === "classInfo" ? "classinfo" : key === "activeTitle" ? "active_title" : key === "curHp" ? "curhp" :
      key === "curMp" ? "curmp" : key === "guildIndex" ? "guildindex" : key.toLowerCase()] =
      (el === "chName") ? v : parseInt(v || "0", 10);
  }
  payload.playerkill = parseInt($("chPk").value, 10);
  payload.deleted = parseInt($("chDeleted").value, 10);
  const { body } = await api(`/api/characters/${id}`, { method: "POST", body: JSON.stringify(payload) });
  if (!body.sucesso) { $("modalCharErro").textContent = body.mensagem; return; }
  showMsg("contaMsg", `${body.mensagem} Backup: ${body.dados.backup}.`, false);
  $("modalChar").classList.add("hidden");
}

/* ---- item edit ---- */
function openCharItem(slotType, slot, it) {
  const tname = slotType === 0 ? EQUIP_LABELS[slot] : (slotType === 1 ? "Inventário" : "Armazém");
  $("ciSalvar").dataset.st = slotType; $("ciSalvar").dataset.slot = slot;
  $("ciLoc").textContent = `${tname} · slot ${slot}`;
  $("ciItemId").value = it ? it.itemId : 0;
  $("ciApp").value = it ? it.app : 0;
  $("ciRefine").value = it ? it.refine : 1;
  $("ciE1i").value = it ? it.effect1Index : 0; $("ciE1v").value = it ? it.effect1Value : 0;
  $("ciE2i").value = it ? it.effect2Index : 0; $("ciE2v").value = it ? it.effect2Value : 0;
  $("ciE3i").value = it ? it.effect3Index : 0; $("ciE3v").value = it ? it.effect3Value : 0;
  $("ciMin").value = it ? it.min : 0; $("ciMax").value = it ? it.max : 0; $("ciTime").value = it ? it.time : 0;
  $("ciName").textContent = it ? (it.itemName || "") : "";
  $("modalCharItemErro").textContent = "";
  $("modalCharItem").classList.remove("hidden");
  lookupItemName();
}

async function lookupItemName() {
  const id = parseInt($("ciItemId").value || "0", 10);
  if (id <= 0) { $("ciName").textContent = "(slot será limpo)"; return; }
  const { body } = await api(`/api/itens?q=${id}&limit=1`);
  if (body.sucesso && body.dados.itens.length) {
    const m = body.dados.itens.find(x => x.id === id) || body.dados.itens[0];
    $("ciName").textContent = m.id === id ? (m.name || m.nameEnglish || "") : "";
  }
}

async function saveCharItem() {
  $("modalCharItemErro").textContent = "";
  const u = {
    slotType: parseInt($("ciSalvar").dataset.st, 10), slot: parseInt($("ciSalvar").dataset.slot, 10),
    itemId: parseInt($("ciItemId").value || "0", 10), app: parseInt($("ciApp").value || "0", 10),
    refine: parseInt($("ciRefine").value || "1", 10), identific: 0,
    effect1Index: parseInt($("ciE1i").value || "0", 10), effect1Value: parseInt($("ciE1v").value || "0", 10),
    effect2Index: parseInt($("ciE2i").value || "0", 10), effect2Value: parseInt($("ciE2v").value || "0", 10),
    effect3Index: parseInt($("ciE3i").value || "0", 10), effect3Value: parseInt($("ciE3v").value || "0", 10),
    min: parseInt($("ciMin").value || "0", 10), max: parseInt($("ciMax").value || "0", 10),
    time: parseInt($("ciTime").value || "0", 10),
  };
  const id = parseInt($("chSalvar").dataset.id, 10);
  const { body } = await api(`/api/characters/${id}/items`, { method: "POST", body: JSON.stringify(u) });
  if (!body.sucesso) { $("modalCharItemErro").textContent = body.mensagem; return; }
  $("modalCharItem").classList.add("hidden");
  await loadCharItems(id);
}

/* ---------- ÍCONES ---------- */
let ICONMAP = {};          // { iconId: [atlas, cell] }
let ICONMETA = null;
let iconsLoaded = false;
let iconSel = null;        // { atlas, cell } selected in the browser

async function loadIconData() {
  const meta = await api("/api/iconmeta");
  if (meta.body.sucesso) ICONMETA = meta.body.dados;
  const m = await api("/api/iconmap");
  if (m.body.sucesso) ICONMAP = m.body.dados || {};
}

function spriteStyle(atlas, cell, px) {
  const col = cell % 32, row = Math.floor(cell / 32), sheet = 1024 * (px / 32);
  return `width:${px}px;height:${px}px;background-image:url(/api/iconsheet/${atlas});`
    + `background-size:${sheet}px ${sheet}px;background-position:${-col * px}px ${-row * px}px;`;
}
function iconHtml(iconId, px) {
  px = px || 32;
  const m = ICONMAP[iconId];
  if (!m) return `<span class="icon empty" title="IconID ${iconId} — sem ícone mapeado"></span>`;
  return `<span class="icon" style="${spriteStyle(m[0], m[1], px)}" title="IconID ${iconId} → atlas ${m[0]}, célula ${m[1]}"></span>`;
}

function fillAtlasSelect(sel) {
  if (!ICONMETA) return;
  sel.innerHTML = "";
  for (const a of ICONMETA.atlases) {
    const o = document.createElement("option");
    o.value = a.atlas; o.textContent = `ItemIcons${String(a.atlas).padStart(2, "0")}` + (a.exists ? "" : " (ausente)");
    o.disabled = !a.exists;
    sel.appendChild(o);
  }
}

/* --- Browser tab --- */
function initIconBrowser() {
  fillAtlasSelect($("iconAtlasSel"));
  $("iconStats").textContent = `${Object.keys(ICONMAP).length} ícones vinculados`;
  showIconSheet(parseInt($("iconAtlasSel").value || "1", 10));
}
function showIconSheet(atlas) {
  $("iconSheet").src = `/api/iconsheet/${atlas}`;
  $("iconHi").classList.add("hidden");
  iconSel = null;
  $("iconSelInfo").textContent = "Nenhuma célula selecionada.";
  $("iconLinkId").value = "";
  $("iconSheet").dataset.atlas = atlas;
}
function cellFromClick(img, ev) {
  const r = img.getBoundingClientRect();
  const x = ev.clientX - r.left, y = ev.clientY - r.top;
  const col = Math.max(0, Math.min(31, Math.floor(x / 32)));
  const row = Math.max(0, Math.min(31, Math.floor(y / 32)));
  return { col, row, cell: row * 32 + col };
}
function placeHi(hiEl, col, row) {
  hiEl.style.left = (col * 32) + "px"; hiEl.style.top = (row * 32) + "px";
  hiEl.classList.remove("hidden");
}
async function onIconSheetClick(ev) {
  const img = $("iconSheet");
  const atlas = parseInt(img.dataset.atlas, 10);
  const { col, row, cell } = cellFromClick(img, ev);
  placeHi($("iconHi"), col, row);
  iconSel = { atlas, cell };
  const rv = await api(`/api/iconcell/${atlas}/${cell}`);
  const id = rv.body.sucesso ? rv.body.dados.iconId : null;
  $("iconSelInfo").innerHTML = `Atlas <b>${atlas}</b>, célula <b>${cell}</b> (linha ${row}, coluna ${col})`
    + (id != null ? ` — vinculado ao IconID <b>${id}</b>` : " — <i>sem vínculo</i>");
  $("iconLinkId").value = id != null ? id : "";
}
async function saveIconLink() {
  if (!iconSel) { showMsg("iconMsg", "Selecione uma célula primeiro.", true); return; }
  const id = parseInt($("iconLinkId").value, 10);
  if (!Number.isFinite(id) || id < 0) { showMsg("iconMsg", "Informe um IconID válido.", true); return; }
  const { body } = await api(`/api/iconmap/${id}`, { method: "POST", body: JSON.stringify({ atlas: iconSel.atlas, cell: iconSel.cell }) });
  if (body.sucesso) {
    ICONMAP[id] = [iconSel.atlas, iconSel.cell];
    showMsg("iconMsg", `IconID ${id} vinculado a atlas ${iconSel.atlas}, célula ${iconSel.cell}.`, false);
    $("iconStats").textContent = `${Object.keys(ICONMAP).length} ícones vinculados`;
    onIconSheetClick.lastRefresh && onIconSheetClick.lastRefresh();
  } else showMsg("iconMsg", body.mensagem, true);
}
async function delIconLink() {
  const id = parseInt($("iconLinkId").value, 10);
  if (!Number.isFinite(id)) return;
  const { body } = await api(`/api/iconmap/${id}`, { method: "DELETE" });
  if (body.sucesso) {
    delete ICONMAP[id];
    showMsg("iconMsg", `Vínculo do IconID ${id} removido.`, false);
    $("iconStats").textContent = `${Object.keys(ICONMAP).length} ícones vinculados`;
  }
}

/* --- Picker overlay (escolher ícone para um item em edição) --- */
let pickForIconId = null;
function openIconPicker(currentIconId) {
  pickForIconId = currentIconId;
  $("pickFor").textContent = `item usa IconID ${currentIconId}`;
  $("pickMsg").textContent = "";
  fillAtlasSelect($("pickAtlasSel"));
  const m = ICONMAP[currentIconId];
  const atlas = m ? m[0] : parseInt($("pickAtlasSel").value || "1", 10);
  $("pickAtlasSel").value = atlas;
  showPickSheet(atlas, m ? m[1] : null);
  $("iconPicker").classList.remove("hidden");
}
function showPickSheet(atlas, cell) {
  $("pickSheet").src = `/api/iconsheet/${atlas}`;
  $("pickSheet").dataset.atlas = atlas;
  if (cell != null) placeHi($("pickHi"), cell % 32, Math.floor(cell / 32));
  else $("pickHi").classList.add("hidden");
}
async function onPickSheetClick(ev) {
  const img = $("pickSheet");
  const atlas = parseInt(img.dataset.atlas, 10);
  const { col, row, cell } = cellFromClick(img, ev);
  placeHi($("pickHi"), col, row);
  const rv = await api(`/api/iconcell/${atlas}/${cell}`);
  const id = rv.body.sucesso ? rv.body.dados.iconId : null;
  if (id != null) {
    $("iIconID").value = id;
    refreshItemIconPreview();
    $("iconPicker").classList.add("hidden");
    showMsg("itemMsg", `Ícone escolhido: o item passa a usar IconID ${id}.`, false);
  } else {
    if (confirm(`Esta célula (atlas ${atlas}, célula ${cell}) ainda não tem IconID conhecido.\n\nVincular o IconID atual do item (${pickForIconId}) a esta célula? Assim o painel passa a mostrar este ícone para esse item.`)) {
      const r = await api(`/api/iconmap/${pickForIconId}`, { method: "POST", body: JSON.stringify({ atlas, cell }) });
      if (r.body.sucesso) {
        ICONMAP[pickForIconId] = [atlas, cell];
        refreshItemIconPreview();
        $("iconPicker").classList.add("hidden");
        showMsg("itemMsg", `IconID ${pickForIconId} vinculado a atlas ${atlas}, célula ${cell}.`, false);
      } else showMsg("pickMsg", r.body.mensagem, true);
    }
  }
}
function refreshItemIconPreview() {
  const id = parseInt($("iIconID").value || "0", 10);
  const el = $("iIconePreview");
  const m = ICONMAP[id];
  if (m) { el.className = "icon"; el.style.cssText = spriteStyle(m[0], m[1], 48); el.title = `IconID ${id} → atlas ${m[0]}, célula ${m[1]}`; }
  else { el.className = "icon empty"; el.style.cssText = "width:48px;height:48px;"; el.title = `IconID ${id} — sem ícone mapeado`; }
}

/* ---------- HELPERS ---------- */
/* ---------- ITENS DE EVENTO (tecla T) ---------- */
let eventoLoaded = false;

async function loadEvento() {
  const badge = $("evtDbBadge");
  const { body } = await api("/api/db/ping");
  if (body.sucesso) { badge.className = "sync-badge ok"; badge.textContent = "✓ " + (body.dados.msg || "conectado"); }
  else { badge.className = "sync-badge off"; badge.textContent = "✗ DB offline"; }
}

async function evtBuscarItens() {
  const q = encodeURIComponent($("evtBuscaItem").value.trim());
  const { body } = await api(`/api/itens?q=${q}&page=1&limit=30`);
  const tb = $("evtTblItens").querySelector("tbody");
  tb.innerHTML = "";
  if (!body.sucesso) { showMsg("evtMsg", body.mensagem, true); return; }
  for (const it of body.dados.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
      <td>${iconHtml(it.iconID)}</td>
      <td>${it.id}</td>
      <td>${esc(it.name)}</td>
      <td>${esc(it.nameEnglish)}</td>
      <td>${it.level}</td>
      <td><button class="primary">Selecionar</button></td>`;
    tr.querySelector("button").onclick = () => evtSelecionar(it);
    tb.appendChild(tr);
  }
}

function evtSelecionar(it) {
  $("evtItemId").value = it.id;
  $("evtItemNome").value = it.name || it.nameEnglish || "";
}

async function evtEnviar() {
  const itemId = parseInt($("evtItemId").value || "0", 10);
  const qty = parseInt($("evtQtd").value || "1", 10);
  const todos = $("evtTodos").checked;
  const target = $("evtTarget").value.trim();
  if (!itemId || itemId <= 0) { showMsg("evtMsg", "Escolha um item válido.", true); return; }
  if (todos) {
    if (!confirm(`Enviar o item ${itemId} (x${qty}) para TODOS os personagens? Não há desfazer fácil.`)) return;
  } else if (!target) {
    showMsg("evtMsg", "Informe o nome do personagem ou marque 'Todos'.", true); return;
  }
  $("evtEnviar").disabled = true;
  const { body } = await api("/api/eventitem", {
    method: "POST",
    body: JSON.stringify({ target, itemId, qty, toAll: todos })
  });
  $("evtEnviar").disabled = false;
  showMsg("evtMsg", body.mensagem, !body.sucesso);
}

function esc(s) { return String(s).replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;"); }
function showMsg(id, txt, isErr) { const e = $(id); e.textContent = txt; e.className = "msg " + (isErr ? "err" : "ok"); }

/* ---------- WIRING ---------- */
$("btnLogin").onclick = login;
$("senha").addEventListener("keydown", e => { if (e.key === "Enter") login(); });
$("btnSair").onclick = () => location.reload();
document.querySelectorAll(".tab").forEach(t => t.onclick = () => setTab(t.dataset.tab));
$("btnBuscar").onclick = () => { cashPage = 1; loadCash(); };
$("busca").addEventListener("keydown", e => { if (e.key === "Enter") { cashPage = 1; loadCash(); } });
$("soVisiveis").onchange = () => { cashPage = 1; loadCash(); };
$("cashPrev").onclick = () => { if (cashPage > 1) { cashPage--; loadCash(); } };
$("cashNext").onclick = () => { cashPage++; loadCash(); };
$("btnSync").onclick = syncClient;
$("mCancelar").onclick = () => $("modal").classList.add("hidden");
$("mSalvar").onclick = saveEdit;
$("btnAddQuest").onclick = addQuest;
$("btnSalvarQuests").onclick = saveQuests;
$("buscaQuest").addEventListener("input", renderQuests);
$("btnBuscarItem").onclick = () => { itemPage = 1; loadItens(); };
$("btnLimparItem").onclick = () => { $("buscaItem").value = ""; $("itemMinLevel").value = ""; $("itemMaxLevel").value = ""; itemPage = 1; loadItens(); };
$("buscaItem").addEventListener("keydown", e => { if (e.key === "Enter") { itemPage = 1; loadItens(); } });
$("itemMinLevel").addEventListener("keydown", e => { if (e.key === "Enter") { itemPage = 1; loadItens(); } });
$("itemMaxLevel").addEventListener("keydown", e => { if (e.key === "Enter") { itemPage = 1; loadItens(); } });
$("itemPrev").onclick = () => { if (itemPage > 1) { itemPage--; loadItens(); } };
$("itemNext").onclick = () => { itemPage++; loadItens(); };
$("iCancelar").onclick = () => $("modalItem").classList.add("hidden");
$("iSalvar").onclick = saveItemEdit;
$("btnItemSync").onclick = () => syncV4("/api/itens/sync", "/api/itens/syncstatus", "itemSyncBadge", "btnItemSync", "itemMsg");
$("btnBuscarSkill").onclick = () => { skillPage = 1; loadSkills(); };
$("buscaSkill").addEventListener("keydown", e => { if (e.key === "Enter") { skillPage = 1; loadSkills(); } });
$("skillPrev").onclick = () => { if (skillPage > 1) { skillPage--; loadSkills(); } };
$("skillNext").onclick = () => { skillPage++; loadSkills(); };
$("sCancelar").onclick = () => $("modalSkill").classList.add("hidden");
$("sSalvar").onclick = saveSkillEdit;
$("btnSkillSync").onclick = () => syncV4("/api/skills/sync", "/api/skills/syncstatus", "skillSyncBadge", "btnSkillSync", "skillMsg");
/* NPCs */
$("btnBuscarNpc").onclick = () => loadNpcs();
$("buscaNpc").addEventListener("keydown", e => { if (e.key === "Enter") loadNpcs(); });
$("btnNovoNpc").onclick = openNpcCreate;
$("nCancelar").onclick = () => $("modalNpc").classList.add("hidden");
$("nSalvar").onclick = saveNpcEdit;
$("nExcluir").onclick = npcDelete;
$("nAddItem").onclick = npcAddItem;
$("nFreeSpot").onclick = npcFreeSpot;
$("nCidade").onchange = e => { if (e.target.value) { const [x, y] = e.target.value.split(","); $("nX").value = x; $("nY").value = y; } };
$("nPertoDe").onchange = e => { if (e.target.value) { const [x, y] = e.target.value.split(","); $("nX").value = x; $("nY").value = y; } };
$("cCancelar").onclick = () => $("modalNpcNovo").classList.add("hidden");
$("cCriar").onclick = npcCreate;
$("cCidade").onchange = e => { if (e.target.value) { const [x, y] = e.target.value.split(","); $("cX").value = x; $("cY").value = y; } };
/* Contas / Personagens */
$("btnBuscarConta").onclick = () => { contaPage = 1; loadContas(); };
$("buscaConta").addEventListener("keydown", e => { if (e.key === "Enter") { contaPage = 1; loadContas(); } });
$("btnNovaConta").onclick = openContaNova;
$("contaPrev").onclick = () => { if (contaPage > 1) { contaPage--; loadContas(); } };
$("contaNext").onclick = () => { contaPage++; loadContas(); };
$("aCancelar").onclick = () => $("modalConta").classList.add("hidden");
$("aSalvar").onclick = saveContaEdit;
$("ncCancelar").onclick = () => $("modalContaNova").classList.add("hidden");
$("ncCriar").onclick = criarConta;
$("chCancelar").onclick = () => $("modalChar").classList.add("hidden");
$("chSalvar").onclick = saveCharEdit;
$("chTitleAddBtn").onclick = addCharTitle;
$("chTitlesSalvar").onclick = saveCharTitles;
document.querySelectorAll(".inv-tab").forEach(t => t.onclick = () => {
  document.querySelectorAll(".inv-tab").forEach(x => x.classList.toggle("ativo", x === t));
  invTab = parseInt(t.dataset.inv, 10); renderInvGrid();
});
$("ciCancelar").onclick = () => $("modalCharItem").classList.add("hidden");
$("ciSalvar").onclick = saveCharItem;
$("ciItemId").addEventListener("change", lookupItemName);
/* Ícones */
$("iconAtlasSel").onchange = e => showIconSheet(parseInt(e.target.value, 10));
$("iconSheet").addEventListener("click", onIconSheetClick);
$("iconLinkSave").onclick = saveIconLink;
$("iconLinkDel").onclick = delIconLink;
$("iEscolherIcone").onclick = () => openIconPicker(parseInt($("iIconID").value || "0", 10));
$("iIconID").addEventListener("change", refreshItemIconPreview);
$("pickAtlasSel").onchange = e => showPickSheet(parseInt(e.target.value, 10), null);
$("pickSheet").addEventListener("click", onPickSheetClick);
$("pickCancelar").onclick = () => $("iconPicker").classList.add("hidden");

/* itens de evento (tecla T) */
$("evtBtnBuscar").onclick = evtBuscarItens;
$("evtBuscaItem").addEventListener("keydown", e => { if (e.key === "Enter") evtBuscarItens(); });
$("evtEnviar").onclick = evtEnviar;
$("evtTodos").addEventListener("change", e => { $("evtTarget").disabled = e.target.checked; });
