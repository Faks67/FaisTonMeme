// Affichage d'un mème (modèle + légendes), partagé par le jeu et les pages annexes.

export const COLORS = ['#6d4cff', '#ff6b5b', '#1f9d6b', '#e0a100', '#2f80ed', '#d63384',
  '#00a3a3', '#8a5a2b', '#5b6b7f', '#b02fd6', '#e8590c', '#3b8f2f'];

/** Petit utilitaire pour créer des éléments sans innerHTML (pas d'injection possible). */
export function el(tag, attrs = {}, ...children) {
  const node = document.createElement(tag);
  for (const [k, v] of Object.entries(attrs)) {
    if (v === undefined || v === null || v === false) continue;
    if (k === 'class') node.className = v;
    else if (k === 'text') node.textContent = v;
    else if (k.startsWith('on')) node.addEventListener(k.slice(2), v);
    else if (k === 'style' && typeof v === 'object') Object.assign(node.style, v);
    else node.setAttribute(k, v === true ? '' : v);
  }
  for (const c of children.flat()) if (c != null) node.append(c);
  return node;
}

export function avatar(player, size) {
  const a = el('span', { class: 'avatar', 'aria-hidden': 'true', text: (player.name || '?').trim().charAt(0).toUpperCase() });
  a.style.background = COLORS[player.color % COLORS.length];
  if (size) { a.style.width = a.style.height = size + 'px'; }
  return a;
}

export const SOURCE_LABELS = { imgflip: 'Imgflip', memegen: 'memegen.link', openverse: 'Openverse', youtube: 'YouTube', klipy: 'KLIPY', communaute: 'Communauté' };
export const KIND_LABELS = { Image: 'Image', Gif: 'GIF', Video: 'Vidéo' };

/** Ligne de crédit sous le mème : source, auteur, lien d'origine. */
export function credit(t) {
  const parts = [];
  if (t.isNew) parts.push(el('span', { class: 'badge-new', text: 'Nouveau' }));
  if (t.source === 'klipy') {
    parts.push(el('span', { class: 'powered', text: 'Powered by KLIPY' }));
  } else {
    let label = 'Source : ' + (SOURCE_LABELS[t.source] || t.source);
    if (t.author) label += ' · ' + t.author;
    parts.push(el('span', { text: label }));
    if (t.license) {
      parts.push(t.licenseUrl
        ? el('a', { href: t.licenseUrl, target: '_blank', rel: 'noopener noreferrer license', text: t.license })
        : el('span', { text: t.license }));
    }
  }
  if (t.pageUrl) parts.push(el('a', { href: t.pageUrl, target: '_blank', rel: 'noopener noreferrer', text: 'Voir l\'original' }));
  return el('p', { class: 'credit' }, parts);
}

/**
 * Construit le mème.
 * - Image : légendes posées sur l'image, déplaçables si editable.
 * - GIF et vidéo : légende dans un bandeau blanc au-dessus (format classique des mèmes vidéo ;
 *   le lecteur YouTube ne doit pas être recouvert).
 */
export function renderMeme(t, captions, { editable = false, onMove, autoplay = true, placeholder = 'Ton texte ici' } = {}) {
  const isYouTube = !!t.youTubeId;
  const root = el('div', { class: `meme ${t.kind.toLowerCase()}${isYouTube ? ' youtube' : ''}${editable ? ' editable' : ''}` });
  const texts = [];

  if (t.kind !== 'Image') {
    const bar = el('div', { class: 'meme-bar' });
    root.append(bar);
    texts.push(bar);
  }

  let media;
  if (isYouTube) {
    const params = new URLSearchParams({ playsinline: '1', rel: '0', loop: '1', playlist: t.youTubeId, mute: '1' });
    if (autoplay) params.set('autoplay', '1');
    media = el('iframe', {
      src: `https://www.youtube-nocookie.com/embed/${encodeURIComponent(t.youTubeId)}?${params}`,
      title: t.title || 'Vidéo YouTube', allow: 'autoplay; encrypted-media; picture-in-picture',
      allowfullscreen: true, loading: 'lazy', referrerpolicy: 'strict-origin-when-cross-origin',
    });
  } else if (t.mediaUrl && /\.(mp4|webm)(\?|$)/i.test(t.mediaUrl)) {
    media = el('video', {
      class: 'meme-media', src: t.mediaUrl, poster: t.thumbnailUrl, muted: true, loop: true,
      playsinline: true, autoplay: autoplay, preload: 'metadata', controls: t.kind === 'Video',
    });
    media.muted = true; // l'attribut seul ne suffit pas pour l'autoplay sur certains navigateurs
  } else {
    media = el('img', { class: 'meme-media', src: t.mediaUrl, alt: t.title || 'Modèle de mème', draggable: 'false' });
  }
  root.append(media);

  if (t.kind === 'Image') {
    captions.forEach((c, i) => {
      const box = el('div', { class: 'meme-text', style: { left: c.x + '%', top: c.y + '%' } });
      if (editable) enableDrag(box, root, (x, y) => onMove?.(i, x, y));
      root.append(box);
      texts.push(box);
    });
  }

  const setText = (i, text) => {
    const node = texts[i];
    if (!node) return;
    const empty = !text || !text.trim();
    node.textContent = empty && editable ? (texts.length > 1 ? `Texte ${i + 1}` : placeholder) : text;
    node.classList.toggle('placeholder', empty && editable);
  };
  captions.forEach((c, i) => setText(i, c.text));

  return { root, setText };
}

function enableDrag(box, frame, onMove) {
  box.addEventListener('pointerdown', (e) => {
    e.preventDefault();
    box.setPointerCapture(e.pointerId);
    box.classList.add('dragging');
    const rect = frame.getBoundingClientRect();
    const start = { x: e.clientX, y: e.clientY, left: parseFloat(box.style.left), top: parseFloat(box.style.top) };
    const move = (ev) => {
      const x = clamp(start.left + (ev.clientX - start.x) / rect.width * 100, 4, 96);
      const y = clamp(start.top + (ev.clientY - start.y) / rect.height * 100, 3, 97);
      box.style.left = x + '%';
      box.style.top = y + '%';
      onMove(x, y);
    };
    const up = () => {
      box.classList.remove('dragging');
      box.removeEventListener('pointermove', move);
      box.removeEventListener('pointerup', up);
      box.removeEventListener('pointercancel', up);
    };
    box.addEventListener('pointermove', move);
    box.addEventListener('pointerup', up);
    box.addEventListener('pointercancel', up);
  });
}

const clamp = (v, a, b) => Math.min(b, Math.max(a, v));
