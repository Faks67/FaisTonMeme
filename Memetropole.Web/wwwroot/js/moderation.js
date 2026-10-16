import { el, KIND_LABELS } from './meme.js';
import { tile, formatDate } from './library-view.js';

const $ = (id) => document.getElementById(id);
const PASS_KEY = 'memetropole-admin';
let password = '';
let status = 'Pending';
let library = [];
const blobUrls = [];

function toast(msg) {
  const t = $('toast');
  t.textContent = msg;
  t.classList.add('show');
  setTimeout(() => t.classList.remove('show'), 3000);
}

async function api(path, options = {}) {
  const res = await fetch('/api/admin' + path, { ...options, headers: { 'X-Admin-Password': password } });
  if (res.status === 401) { logout(); throw new Error('Mot de passe incorrect.'); }
  if (!res.ok) throw new Error('Erreur ' + res.status);
  return res;
}

function logout() {
  try { sessionStorage.removeItem(PASS_KEY); } catch { /* ignoré */ }
  $('panel').hidden = true;
  $('login').hidden = false;
}

async function login(pw) {
  password = pw;
  await api('/check');
  try { sessionStorage.setItem(PASS_KEY, pw); } catch { /* ignoré */ }
  $('login').hidden = true;
  $('panel').hidden = false;
  await Promise.all([loadSubmissions(), loadLibrary()]);
}

$('login').addEventListener('submit', async (e) => {
  e.preventDefault();
  $('login-error').textContent = '';
  try { await login($('password').value); }
  catch (ex) { $('login-error').textContent = ex.message; }
});

// ---------- Propositions ----------

/** Aperçu d'une proposition. Les fichiers en attente ne sont pas publics : on les charge avec le mot de passe. */
async function preview(s) {
  if (s.youTubeId) return el('img', { src: `https://i.ytimg.com/vi/${s.youTubeId}/hqdefault.jpg`, alt: '' });
  let src = s.externalUrl;
  if (s.fileName) {
    const blob = await (await api(`/submissions/${s.id}/file`)).blob();
    src = URL.createObjectURL(blob);
    blobUrls.push(src);
  }
  if (!src) return el('div');
  return s.kind === 'Video' && !s.youTubeId
    ? el('video', { src, muted: true, loop: true, autoplay: true, playsinline: true, controls: true })
    : el('img', { src, alt: '' });
}

async function loadSubmissions() {
  blobUrls.splice(0).forEach(URL.revokeObjectURL);
  const list = await (await api('/submissions?status=' + status)).json();
  if (!list.length) {
    $('submissions').replaceChildren(el('p', { class: 'empty', text: 'Rien ici pour le moment.' }));
    return;
  }
  const cards = await Promise.all(list.map(async (s) => {
    const actions = [];
    if (s.status !== 'Approved') actions.push(el('button', { class: 'btn btn-ok', type: 'button', text: 'Valider', onclick: () => review(s.id, 'approve') }));
    if (s.status !== 'Rejected') actions.push(el('button', { class: 'btn btn-danger', type: 'button', text: 'Refuser', onclick: () => review(s.id, 'reject') }));
    const link = s.youTubeId ? `https://www.youtube.com/watch?v=${s.youTubeId}` : s.externalUrl;
    return el('article', { class: 'tile' },
      el('div', { class: 'thumb' }, await preview(s).catch(() => el('div')), el('span', { class: 'kind', text: KIND_LABELS[s.kind] })),
      el('div', { class: 'info' },
        el('strong', { text: s.title }),
        el('span', { text: `Par ${s.submittedBy} · ${formatDate(s.submittedAt)}` }),
        s.kind === 'Image' ? el('span', { text: `${s.boxCount} zone(s) de texte` }) : null,
        link ? el('a', { href: link, target: '_blank', rel: 'noopener noreferrer', text: 'Ouvrir le lien' }) : null),
      el('div', { class: 'actions' }, actions));
  }));
  $('submissions').replaceChildren(...cards);
}

async function review(id, action) {
  try {
    await api(`/submissions/${id}/${action}`, { method: 'POST' });
    toast(action === 'approve' ? 'Modèle ajouté à la bibliothèque.' : 'Proposition refusée.');
    await Promise.all([loadSubmissions(), loadLibrary()]);
  } catch (ex) { toast(ex.message); }
}

for (const b of document.querySelectorAll('#tabs [data-status]')) {
  b.addEventListener('click', () => {
    status = b.dataset.status;
    for (const x of document.querySelectorAll('#tabs [data-status]')) x.setAttribute('aria-pressed', String(x === b));
    loadSubmissions().catch(ex => toast(ex.message));
  });
}

$('refresh').addEventListener('click', async (e) => {
  e.target.disabled = true;
  try {
    const statuses = await (await api('/refresh', { method: 'POST' })).json();
    toast(statuses.map(s => `${s.source} : ${s.message}`).join(' · '));
    await loadLibrary();
  } catch (ex) { toast(ex.message); }
  finally { e.target.disabled = false; }
});

// ---------- Bibliothèque ----------

async function loadLibrary() {
  library = (await fetch('/api/library').then(r => r.json())).map(x => x.template);
  renderLibrary();
}

function renderLibrary() {
  const q = $('q').value.trim().toLowerCase();
  const kind = $('kind').value;
  const source = $('source').value;
  const list = library.filter(t =>
    (!q || t.title.toLowerCase().includes(q)) && (!kind || t.kind === kind) && (!source || t.source === source)).slice(0, 60);
  $('library').replaceChildren(...list.map(t => tile(t, [
    el('button', { class: 'btn btn-danger', type: 'button', text: 'Retirer', onclick: () => remove(t) }),
  ])));
}

async function remove(t) {
  if (!confirm(`Retirer « ${t.title} » de la bibliothèque ?`)) return;
  try {
    await api('/library/' + encodeURIComponent(t.id), { method: 'DELETE' });
    library = library.filter(x => x.id !== t.id);
    renderLibrary();
    toast('Modèle retiré.');
  } catch (ex) { toast(ex.message); }
}

for (const id of ['q', 'kind', 'source']) $(id).addEventListener('input', renderLibrary);

let saved = null;
try { saved = sessionStorage.getItem(PASS_KEY); } catch { /* ignoré */ }
if (saved) login(saved).catch(() => logout());
