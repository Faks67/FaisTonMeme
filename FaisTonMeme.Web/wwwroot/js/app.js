// Fais ton mème : client du jeu. Le serveur (SignalR) fait autorité, ce fichier ne fait qu'afficher
// l'état reçu et envoyer les actions du joueur.
import { el, avatar, renderMeme, credit } from './meme.js';
import { loadKlipyTrending } from './klipy.js';

const $ = (id) => document.getElementById(id);
const SESSION_KEY = 'faistonmeme-session'; // par onglet : un onglet = un joueur
const NAME_KEY = 'faistonmeme-name';
const ROUND_RESULTS_SECONDS = 9;

let conn;
let config = { klipy: null, youtube: false };
let state = null;
let clockOffset = 0;
let klipyRequested = false;
let libraryStats = null;
const editor = { key: null, captions: [], meme: null, saveTimer: null, inputs: [] };
let voteKey = null;
let roundKey = null;
let finalKey = null;

// ---------- Utilitaires ----------

const store = {
  get(k, s = localStorage) { try { return s.getItem(k); } catch { return null; } },
  set(k, v, s = localStorage) { try { v == null ? s.removeItem(k) : s.setItem(k, v); } catch { /* ignoré */ } },
};
const session = {
  get: () => { try { return JSON.parse(store.get(SESSION_KEY, sessionStorage)); } catch { return null; } },
  set: (v) => store.set(SESSION_KEY, v ? JSON.stringify(v) : null, sessionStorage),
};

let toastTimer;
function toast(msg) {
  const t = $('toast');
  t.textContent = msg;
  t.classList.add('show');
  clearTimeout(toastTimer);
  toastTimer = setTimeout(() => t.classList.remove('show'), 3200);
}

function cleanError(e) {
  const m = String(e?.message || e);
  const i = m.indexOf('HubException: ');
  return i >= 0 ? m.slice(i + 14) : 'Connexion au serveur impossible. Réessaie dans un instant.';
}

async function call(method, ...args) {
  try { return await conn.invoke(method, ...args); }
  catch (e) { toast(cleanError(e)); throw e; }
}

function showScreen(id) {
  for (const s of document.querySelectorAll('.screen')) s.hidden = s.id !== id;
}

const isHost = () => state && state.hostId === state.meId;
const ranked = () => [...state.players].sort((a, b) => b.score - a.score || a.name.localeCompare(b.name));

// ---------- Connexion ----------

async function connect() {
  conn = new signalR.HubConnectionBuilder()
    .withUrl('/hub/jeu')
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000, 10000, 30000])
    .build();

  conn.on('state', onState);
  conn.onreconnecting(() => toast('Connexion perdue, reconnexion…'));
  conn.onreconnected(() => { toast('Reconnecté'); rejoin(); });
  conn.onclose(() => toast('Déconnecté du serveur. Recharge la page.'));

  await conn.start();
}

async function rejoin() {
  const s = session.get();
  if (!s) return false;
  try {
    await conn.invoke('Rejoin', s.code, s.playerId, s.token);
    return true;
  } catch (e) {
    session.set(null);
    toast(cleanError(e));
    showHome();
    return false;
  }
}

function joined(res) {
  session.set(res);
  history.replaceState(null, '', `/?salle=${res.code}`);
}

// ---------- Accueil ----------

function showHome() {
  state = null;
  $('room-chip').hidden = true;
  showScreen('screen-home');
}

function readName() {
  const name = $('name').value.trim();
  if (!name) { $('home-error').textContent = 'Choisis un pseudo pour jouer.'; $('name').focus(); return null; }
  store.set(NAME_KEY, name);
  $('home-error').textContent = '';
  return name;
}

$('btn-create').addEventListener('click', async () => {
  const name = readName();
  if (!name) return;
  try { joined(await conn.invoke('CreateRoom', name)); }
  catch (e) { $('home-error').textContent = cleanError(e); }
});

async function joinFromForm() {
  const name = readName();
  if (!name) return;
  const code = $('code').value.trim().toUpperCase();
  if (code.length !== 4) { $('home-error').textContent = 'Le code de la salle fait 4 lettres.'; return; }
  try { joined(await conn.invoke('JoinRoom', code, name)); }
  catch (e) { $('home-error').textContent = cleanError(e); }
}
$('btn-join').addEventListener('click', joinFromForm);
$('code').addEventListener('keydown', (e) => { if (e.key === 'Enter') joinFromForm(); });
$('code').addEventListener('input', (e) => { e.target.value = e.target.value.toUpperCase().replace(/[^A-Z]/g, ''); });
$('name').addEventListener('keydown', (e) => { if (e.key === 'Enter') ($('code').value ? joinFromForm() : $('btn-create').click()); });

for (const b of document.querySelectorAll('[data-leave]')) {
  b.addEventListener('click', async () => {
    try { await conn.invoke('Leave'); } catch { /* on part quand même */ }
    session.set(null);
    history.replaceState(null, '', '/');
    showHome();
  });
}

// ---------- Réception de l'état ----------

function onState(s) {
  const previous = state;
  state = s;
  clockOffset = s.serverNow - Date.now();
  if (s.phase !== 'Lobby') klipyRequested = false;

  const chip = $('room-chip');
  chip.hidden = false;
  chip.textContent = s.code;

  if (previous?.phase !== s.phase) window.scrollTo({ top: 0, behavior: 'smooth' });

  switch (s.phase) {
    case 'Lobby': renderLobby(); break;
    case 'Captioning': renderCaption(); break;
    case 'Voting': renderVote(); break;
    case 'RoundResults': renderRound(); break;
    case 'Final': renderFinal(); break;
  }
  tick();
}

// ---------- Salon ----------

function playerRow(p, { showDone = false } = {}) {
  const tags = [];
  if (!showDone && p.id === state.hostId) tags.push(el('span', { class: 'tag', text: 'Hôte' }));
  if (!showDone && p.id === state.meId) tags.push(el('span', { class: 'tag you', text: 'Toi' }));
  if (!p.connected) tags.push(el('span', { class: 'tag off', text: 'Hors ligne' }));
  if (showDone && p.done) tags.push(el('span', { class: 'tag done', text: 'Prêt' }));
  return el('li', { class: 'player' + (p.connected ? '' : ' offline') }, avatar(p), el('span', { class: 'name', text: p.name }), tags);
}

function renderLobby() {
  showScreen('screen-lobby');
  $('lobby-code').textContent = state.code;
  $('lobby-count').textContent = `${state.players.length} / 12`;
  $('lobby-players').replaceChildren(...state.players.map(p => playerRow(p)));

  const form = $('settings');
  const host = isHost();
  for (const input of form.elements) input.disabled = !host;
  if (!form.contains(document.activeElement)) {
    const st = state.settings;
    form.rounds.value = st.rounds;
    form.captionSeconds.value = st.captionSeconds;
    form.voteSeconds.value = st.voteSeconds;
    form.rerolls.value = st.rerolls;
    form.images.checked = st.images;
    form.gifs.checked = st.gifs;
    form.videos.checked = st.videos;
  }
  $('btn-start').hidden = !host;
  $('lobby-wait').hidden = host;
  const enough = state.players.filter(p => p.connected).length >= 2;
  $('btn-start').disabled = !enough;
  $('btn-start').textContent = enough ? 'Lancer la partie' : 'En attente d\'un 2e joueur…';

  renderSources();
  if (host && config.klipy && state.klipyCount === 0 && !klipyRequested) fetchKlipy();
}

function renderSources() {
  const parts = [];
  if (libraryStats) {
    const k = libraryStats.byKind;
    parts.push(`Bibliothèque : ${k.Image ?? 0} images, ${k.Gif ?? 0} GIF, ${k.Video ?? 0} vidéos.`);
  }
  if (config.klipy) {
    parts.push(state.klipyCount > 0
      ? `KLIPY : ${state.klipyCount} GIF et clips tendance chargés pour cette partie.`
      : isHost() ? 'KLIPY : chargement des tendances…' : 'KLIPY : l\'hôte charge les tendances.');
  }
  $('lobby-sources').textContent = parts.join(' ');
}

async function fetchKlipy() {
  klipyRequested = true;
  try {
    const items = await loadKlipyTrending(config.klipy);
    if (items.length) await call('SetKlipyPool', items);
  } catch (e) {
    console.warn(e);
    $('lobby-sources').textContent += ' KLIPY indisponible pour le moment.';
  }
}

$('settings').addEventListener('change', () => {
  if (!isHost()) return;
  const f = $('settings');
  call('UpdateSettings', {
    rounds: +f.rounds.value, captionSeconds: +f.captionSeconds.value, voteSeconds: +f.voteSeconds.value,
    rerolls: +f.rerolls.value, images: f.images.checked, gifs: f.gifs.checked, videos: f.videos.checked,
  }).catch(() => {});
});

$('btn-start').addEventListener('click', () => {
  $('lobby-error').textContent = '';
  call('StartGame').catch(e => { $('lobby-error').textContent = cleanError(e); });
});

$('btn-copy').addEventListener('click', async () => {
  const link = `${location.origin}/?salle=${state.code}`;
  try { await navigator.clipboard.writeText(link); toast('Lien copié : ' + link); }
  catch { toast(link); }
});

// ---------- Légende ----------

function renderCaption() {
  showScreen('screen-caption');
  const task = state.task;
  $('caption-round').textContent = `Manche ${state.round} sur ${state.totalRounds}`;
  $('caption-progress').replaceChildren(...state.players.map(p => playerRow(p, { showDone: true })));
  if (!task) return;

  const key = `${state.round}:${task.template.id}`;
  if (editor.key !== key) buildEditor(task, key);

  const submitted = task.submitted;
  document.querySelector('#screen-caption .editor').classList.toggle('submitted', submitted);
  for (const input of editor.inputs) input.disabled = submitted;
  $('btn-done').textContent = submitted ? 'Modifier' : 'J\'ai fini';
  $('btn-done').className = 'btn ' + (submitted ? 'btn-ghost' : 'btn-primary');
  const reroll = $('btn-reroll');
  reroll.textContent = `Changer de modèle (${task.rerollsLeft})`;
  reroll.disabled = submitted || task.rerollsLeft <= 0;
}

function buildEditor(task, key) {
  editor.key = key;
  editor.captions = task.captions.map(c => ({ ...c }));
  const t = task.template;

  const meme = renderMeme(t, editor.captions, {
    editable: true,
    onMove: (i, x, y) => { editor.captions[i].x = x; editor.captions[i].y = y; scheduleSave(); },
  });
  editor.meme = meme;
  $('caption-stage').replaceChildren(meme.root, credit(t));

  const multi = editor.captions.length > 1;
  editor.inputs = editor.captions.map((c, i) =>
    el('input', { maxlength: '140', value: c.text, placeholder: multi ? `Texte ${i + 1}` : 'Ta légende', autocomplete: 'off' }));
  const fields = editor.inputs.map((input, i) => {
    input.addEventListener('input', () => {
      editor.captions[i].text = input.value;
      meme.setText(i, input.value);
      scheduleSave();
    });
    input.addEventListener('keydown', (e) => {
      if (e.key !== 'Enter') return;
      e.preventDefault();
      const next = editor.inputs[i + 1];
      next ? next.focus() : $('btn-done').click();
    });
    return el('label', { class: 'field' }, el('span', { text: multi ? `Texte ${i + 1}` : 'Légende' }), input);
  });
  $('caption-inputs').replaceChildren(...fields);

  $('caption-hint').textContent = t.kind === 'Image'
    ? (multi ? 'Glisse les textes sur l\'image pour les placer.' : 'Glisse le texte pour le placer.')
    : 'Ta légende s\'affiche dans le bandeau au-dessus de la vidéo.';
  if (matchMedia('(pointer: fine)').matches) editor.inputs[0]?.focus();
}

function scheduleSave() {
  clearTimeout(editor.saveTimer);
  editor.saveTimer = setTimeout(saveDraft, 500);
}

function saveDraft() {
  clearTimeout(editor.saveTimer);
  editor.saveTimer = null;
  if (state?.phase !== 'Captioning') return;
  conn.invoke('SaveCaptions', editor.captions, false).catch(() => {});
}

$('btn-done').addEventListener('click', () => {
  const submitted = state?.task?.submitted;
  if (!submitted && !editor.captions.some(c => c.text.trim())) { toast('Écris au moins une légende.'); return; }
  clearTimeout(editor.saveTimer);
  call('SaveCaptions', editor.captions, !submitted).catch(() => {});
});

$('btn-reroll').addEventListener('click', () => call('Reroll').catch(() => {}));

// ---------- Vote ----------

function renderVote() {
  showScreen('screen-vote');
  const v = state.vote;
  if (!v) return;
  $('vote-count').textContent = `Manche ${state.round} · mème ${v.index + 1} sur ${v.count}`;

  const key = `${state.round}:${v.index}`;
  if (voteKey !== key) {
    voteKey = key;
    const m = renderMeme(v.meme.template, v.meme.captions);
    $('vote-stage').replaceChildren(m.root, credit(v.meme.template));
  }
  $('vote-mine').hidden = !v.isMine;
  $('vote-buttons').hidden = v.isMine;
  for (const b of document.querySelectorAll('.vote-btn')) {
    const score = +b.dataset.score;
    b.classList.toggle('selected', v.myVote === score);
    b.disabled = v.isMine;
  }
  const voters = state.players.filter(p => p.connected).length - 1;
  $('vote-progress').textContent = `${v.votesIn} vote${v.votesIn > 1 ? 's' : ''} sur ${Math.max(voters, 0)}`;
}

for (const b of document.querySelectorAll('.vote-btn')) {
  b.addEventListener('click', () => {
    if (!state?.vote) return;
    call('Vote', state.vote.index, +b.dataset.score).catch(() => {});
  });
}

// ---------- Résultats ----------

function resultCard(m, best) {
  const author = state.players.find(p => p.id === m.authorId) ?? { name: 'Joueur parti', color: 0 };
  const meme = renderMeme(m.template, m.captions, { autoplay: false });
  return el('article', { class: 'result card' + (best ? ' best' : '') },
    meme.root,
    el('div', { class: 'result-meta' }, avatar(author), el('span', { class: 'name', text: author.name }),
      el('span', { class: 'points', text: `${m.points} pt${m.points > 1 ? 's' : ''}` })),
    credit(m.template));
}

function scoreboard() {
  return ranked().map((p, i) => el('li', { class: p.id === state.meId ? 'me' : '' },
    el('span', { class: 'rank', text: String(i + 1) }), avatar(p), el('span', { class: 'name', text: p.name }),
    el('span', { class: 'points', text: `${p.score} pts` })));
}

function renderRound() {
  showScreen('screen-round');
  $('round-title').textContent = `Manche ${state.round} sur ${state.totalRounds}`;
  const key = String(state.round);
  if (roundKey !== key) {
    roundKey = key;
    const results = state.roundResults ?? [];
    $('round-results').replaceChildren(...(results.length
      ? results.map((m, i) => resultCard(m, i === 0 && m.points > 0))
      : [el('p', { class: 'empty', text: 'Personne n\'a écrit de légende cette manche.' })]));
  }
  $('round-scoreboard').replaceChildren(...scoreboard());
}

function renderFinal() {
  showScreen('screen-final');
  const top = ranked();
  const key = top.map(p => p.id + p.score).join();
  if (finalKey !== key) {
    finalKey = key;
    $('podium').replaceChildren(...top.slice(0, 3).map((p, i) => el('div', { class: `step p${i + 1}` },
      avatar(p), el('span', { class: 'name', text: p.name }), el('span', { class: 'points', text: `${p.score} pts` }),
      el('div', { class: 'block', text: String(i + 1) }))));
    const hl = state.highlights ?? [];
    $('highlights-title').hidden = hl.length === 0;
    $('highlights').replaceChildren(...hl.map(m => resultCard(m, false)));
  }
  $('final-scoreboard').replaceChildren(...scoreboard());
  const host = isHost();
  $('btn-replay').hidden = !host;
  $('btn-lobby').hidden = !host;
  $('final-wait').hidden = host;
}

$('btn-replay').addEventListener('click', () => call('StartGame').catch(() => {}));
$('btn-lobby').addEventListener('click', () => call('BackToLobby').catch(() => {}));

// ---------- Chronomètre ----------

function phaseTotal() {
  switch (state.phase) {
    case 'Captioning': return state.settings.captionSeconds;
    case 'Voting': return state.settings.voteSeconds + (state.vote?.meme.template.kind === 'Video' ? 6 : 0);
    case 'RoundResults': return ROUND_RESULTS_SECONDS;
    default: return 1;
  }
}

function tick() {
  if (!state?.deadline) return;
  const remaining = Math.max(0, (state.deadline - (Date.now() + clockOffset)) / 1000);
  const frac = Math.min(1, remaining / phaseTotal());
  for (const t of document.querySelectorAll('.screen:not([hidden]) [data-timer]')) {
    t.querySelector('span').textContent = Math.ceil(remaining);
    t.querySelector('.timer-fill').style.strokeDashoffset = String(100.5 * (1 - frac));
    t.classList.toggle('low', remaining <= 10);
  }
  // Juste avant la fin du chrono, on envoie le brouillon en attente pour ne rien perdre.
  if (state.phase === 'Captioning' && editor.saveTimer && remaining < 1.5) saveDraft();
}
setInterval(tick, 200);

// ---------- Démarrage ----------

async function main() {
  $('name').value = store.get(NAME_KEY) ?? '';
  const code = new URLSearchParams(location.search).get('salle');
  if (code) $('code').value = code.toUpperCase().slice(0, 4);

  fetch('/api/config').then(r => r.json()).then(c => { config = c; if (state) renderSources(); }).catch(() => {});
  fetch('/api/library/status').then(r => r.json()).then(s => { libraryStats = s; if (state?.phase === 'Lobby') renderSources(); }).catch(() => {});

  try { await connect(); }
  catch { $('home-error').textContent = 'Impossible de joindre le serveur. Est-il bien lancé ?'; return; }

  const s = session.get();
  if (s && (!code || code.toUpperCase() === s.code) && await rejoin()) return;
  showHome();
  if (code && $('name').value) $('btn-join').focus();
}

main();
