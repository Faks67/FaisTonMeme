// Vignettes de la bibliothèque, partagées par la page Bibliothèque et la page de modération.
import { el, KIND_LABELS } from './meme.js';

const SOURCE_NAMES = { imgflip: 'Imgflip', youtube: 'YouTube', communaute: 'Communauté', klipy: 'KLIPY' };

export function sourceName(s) { return SOURCE_NAMES[s] || s; }

export function formatDate(iso) {
  if (!iso) return 'jamais';
  return new Date(iso).toLocaleString('fr-FR', { dateStyle: 'short', timeStyle: 'short' });
}

export function tile(t, actions = []) {
  const thumbUrl = t.thumbnailUrl || t.mediaUrl;
  const isVideoFile = thumbUrl && /\.(mp4|webm)(\?|$)/i.test(thumbUrl);
  const media = isVideoFile
    ? el('video', { src: thumbUrl, muted: true, loop: true, playsinline: true, preload: 'metadata' })
    : el('img', { src: thumbUrl, alt: '', loading: 'lazy' });
  return el('article', { class: 'tile' },
    el('div', { class: 'thumb' }, media,
      t.isNew ? el('span', { class: 'badge-new', text: 'Nouveau' }) : null,
      el('span', { class: 'kind', text: KIND_LABELS[t.kind] || t.kind })),
    el('div', { class: 'info' },
      el('strong', { text: t.title || 'Sans titre' }),
      el('span', { text: sourceName(t.source) + (t.author ? ' · ' + t.author : '') }),
      t.pageUrl ? el('a', { href: t.pageUrl, target: '_blank', rel: 'noopener noreferrer', text: 'Voir l\'original' }) : null),
    actions.length ? el('div', { class: 'actions' }, actions) : null);
}
