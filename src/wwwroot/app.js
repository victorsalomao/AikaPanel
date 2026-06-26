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
  $("tab-quests").classList.toggle("hidden", name !== "quests");
  if (name === "itens" && !itensLoaded) { itensLoaded = true; loadItens(); }
  if (name === "skills" && !skillsLoaded) { skillsLoaded = true; loadSkills(); }
  if (name === "quests" && questRows.length === 0) loadQuests();
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
  const { body } = await api(`/api/itens?q=${q}&page=${itemPage}&limit=${PAGE}`);
  if (!body.sucesso) { showMsg("itemMsg", body.mensagem, true); return; }
  const d = body.dados;
  $("itemStats").textContent = `${d.total} registros — ${d.matched} na lista`;
  const tb = $("tblItens").querySelector("tbody");
  tb.innerHTML = "";
  for (const it of d.itens) {
    const tr = document.createElement("tr");
    tr.innerHTML = `
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

/* ---------- HELPERS ---------- */
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
$("buscaItem").addEventListener("keydown", e => { if (e.key === "Enter") { itemPage = 1; loadItens(); } });
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
