import { el } from './meme.js';
import { tile, sourceName, formatDate } from './library-view.js';

const $ = (id) => document.getElementById(id);
let all = [];

async function loadStatus() {
  const [status, config] = await Promise.all([
    fetch('/api/library/status').then(r => r.json()),
    fetch('/api/config').then(r => r.json()),
  ]);
  const cards = status.sources.map(s => el('div', { class: 'card source-card' },
    el('h3', {}, sourceName(s.source), el('span', { class: 'dot ' + (s.ok ? 'ok' : s.lastRun ? 'ko' : ''), title: s.ok ? 'OK' : 'Inactive ou en erreur' })),
    el('p', { text: s.message }),
    el('p', { text: 'Dernière mise à jour : ' + formatDate(s.lastRun) })));
  cards.push(el('div', { class: 'card source-card' },
    el('h3', {}, 'KLIPY', el('span', { class: 'dot ' + (config.klipy ? 'ok' : '') })),
    el('p', { text: config.klipy ? 'GIF et clips tendance chargés en direct à chaque partie.' : 'Aucune clé API : renseigne FaisTonMeme:Klipy:ApiKey' })));
  cards.push(el('div', { class: 'card source-card' },
    el('h3', { text: 'Total' }),
    el('p', { text: `${status.total} modèles · ${status.pending} proposition(s) en attente` }),
    el('p', { text: 'Prochaine mise à jour : ' + formatDate(status.nextRun) })));
  $('sources').replaceChildren(...cards);
  $('klipy-note').hidden = !config.klipy;
}

async function loadLibrary() {
  all = await fetch('/api/library').then(r => r.json());
  render();
}

const PAGE = 120; // la bibliothèque compte des milliers de modèles : on les affiche par paquets
let shown = PAGE;

function render() {
  const q = $('q').value.trim().toLowerCase();
  const kind = $('kind').value;
  const source = $('source').value;
  const list = all.map(x => x.template).filter(t =>
    (!q || t.title.toLowerCase().includes(q)) && (!kind || t.kind === kind) && (!source || t.source === source));
  $('count').textContent = `${list.length.toLocaleString('fr-FR')} modèle${list.length > 1 ? 's' : ''}`;
  const nodes = list.length ? list.slice(0, shown).map(t => tile(t)) : [el('p', { class: 'empty', text: 'Aucun modèle ne correspond.' })];
  $('tiles').replaceChildren(...nodes);
  $('more').hidden = list.length <= shown;
}

for (const id of ['q', 'kind', 'source']) $(id).addEventListener('input', () => { shown = PAGE; render(); });
$('more').addEventListener('click', () => { shown += PAGE; render(); });
loadStatus().catch(() => {});
loadLibrary().catch(() => { $('count').textContent = 'Impossible de charger la bibliothèque.'; });
