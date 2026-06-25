"use strict";
let TOKEN = "";
let cashPage = 1;
const PAGE = 50;
let questCols = [];
let questRows = [];

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
  $("tab-quests").classList.toggle("hidden", name !== "quests");
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
    showMsg("cashMsg", `Salvo. Backup: ${body.dados.backup}. Reinicie o servidor para aplicar.`, false);
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
$("mCancelar").onclick = () => $("modal").classList.add("hidden");
$("mSalvar").onclick = saveEdit;
$("btnAddQuest").onclick = addQuest;
$("btnSalvarQuests").onclick = saveQuests;
$("buscaQuest").addEventListener("input", renderQuests);
