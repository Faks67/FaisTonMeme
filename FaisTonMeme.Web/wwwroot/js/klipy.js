// KLIPY (GIF et clips tendance). Leurs conditions imposent que les appels partent du navigateur
// et que les médias soient chargés directement depuis leurs liens, sans copie sur notre serveur.
// Le navigateur de l'hôte récupère donc les tendances au moment de la partie et transmet
// seulement les liens au serveur, qui les garde en mémoire pour cette partie.

const API = 'https://api.klipy.com/api/v1';

function customerId() {
  const KEY = 'faistonmeme-klipy-id';
  try {
    let id = localStorage.getItem(KEY);
    if (!id) { id = crypto.randomUUID(); localStorage.setItem(KEY, id); }
    return id;
  } catch { return 'faistonmeme'; }
}

async function trending(cfg, kind) {
  const params = new URLSearchParams({
    page: '1', per_page: '24', customer_id: customerId(),
    locale: cfg.locale || 'fr', content_filter: cfg.contentFilter || 'high',
  });
  const res = await fetch(`${API}/${encodeURIComponent(cfg.key)}/${kind}/trending?${params}`);
  if (!res.ok) throw new Error(`KLIPY ${kind} : ${res.status}`);
  const json = await res.json();
  return json?.data?.data ?? [];
}

function fromGif(g) {
  const f = g.file?.md ?? g.file?.hd ?? g.file?.sm;
  if (!f?.mp4?.url) return null;
  return {
    id: String(g.id ?? g.slug), type: 'gif', title: g.title,
    mp4: f.mp4.url, poster: f.jpg?.url ?? null,
    page: g.slug ? `https://klipy.com/gifs/${g.slug}` : null,
    width: f.mp4.width ?? 0, height: f.mp4.height ?? 0,
  };
}

function fromClip(c) {
  if (!c.file?.mp4) return null;
  return {
    id: c.slug, type: 'clip', title: c.title,
    mp4: c.file.mp4, poster: c.file.webp ?? null, page: c.url ?? null,
    width: c.file_meta?.mp4?.width ?? 0, height: c.file_meta?.mp4?.height ?? 0,
  };
}

/** Renvoie les GIF puis les clips tendance, dans l'ordre fourni par KLIPY. */
export async function loadKlipyTrending(cfg) {
  const [gifs, clips] = await Promise.allSettled([trending(cfg, 'gifs'), trending(cfg, 'clips')]);
  const items = [];
  if (gifs.status === 'fulfilled') items.push(...gifs.value.filter(x => x.type !== 'ad').map(fromGif));
  if (clips.status === 'fulfilled') items.push(...clips.value.filter(x => x.type !== 'ad').map(fromClip));
  if (gifs.status === 'rejected' && clips.status === 'rejected') throw gifs.reason;
  return items.filter(Boolean);
}
