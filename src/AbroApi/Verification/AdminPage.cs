namespace AbroApi.Verification;

/// The ID review page served at /admin. Plain HTML + JS so it needs no build
/// step; it asks for the admin key and sends it with every request.
public static class AdminPage
{
    public const string Html = """
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="robots" content="noindex">
<title>TravelMate Admin</title>
<style>
  :root { --bg:#080D1C; --surface:#111A33; --muted:#1A2547; --border:#25315A; --text:#F1F5F9; --sub:#9AA7C0; --gold:#FBBF24; --navy:#172554; --ok:#10B981; --bad:#EF4444; }
  * { box-sizing:border-box; }
  body { margin:0; background:var(--bg); color:var(--text); font:15px/1.5 system-ui,-apple-system,Segoe UI,Roboto,sans-serif; }
  header { display:flex; align-items:center; gap:12px; padding:14px 20px; background:var(--navy); border-bottom:1px solid var(--border); }
  header h1 { font-size:18px; margin:0; flex:1; }
  header h1 span { color:var(--gold); }
  main { max-width:1100px; margin:0 auto; padding:20px; }
  button { font:inherit; cursor:pointer; border-radius:999px; border:1px solid var(--border); background:var(--muted); color:var(--text); padding:8px 18px; }
  button.primary { background:var(--gold); color:var(--navy); border-color:var(--gold); font-weight:600; }
  button.bad { border-color:var(--bad); color:#fca5a5; }
  button:disabled { opacity:.5; cursor:default; }
  input { font:inherit; padding:10px 14px; border-radius:12px; border:1px solid var(--border); background:var(--surface); color:var(--text); width:100%; }
  .tabs { display:flex; gap:8px; margin-bottom:16px; }
  .tabs button.on { background:var(--gold); color:var(--navy); border-color:var(--gold); font-weight:600; }
  .card { background:var(--surface); border:1px solid var(--border); border-radius:20px; padding:16px; margin-bottom:16px; }
  .head { display:flex; flex-wrap:wrap; gap:8px 16px; align-items:baseline; margin-bottom:12px; }
  .head b { font-size:17px; }
  .sub { color:var(--sub); font-size:13px; }
  .photos { display:grid; grid-template-columns:repeat(auto-fit,minmax(220px,1fr)); gap:12px; }
  .photo { background:var(--muted); border-radius:14px; overflow:hidden; min-height:140px; display:flex; flex-direction:column; }
  .photo span { padding:6px 10px; font-size:12px; color:var(--sub); }
  .photo img { width:100%; max-height:340px; object-fit:contain; background:#000; cursor:zoom-in; }
  .actions { display:flex; gap:10px; margin-top:14px; }
  .empty, .err { text-align:center; color:var(--sub); padding:48px 0; }
  .err { color:#fca5a5; }
  #login { max-width:380px; margin:12vh auto; padding:0 20px; }
  #login h2 { margin:0 0 6px; }
  .pill { font-size:12px; padding:2px 10px; border-radius:999px; background:var(--muted); }
  dialog { background:var(--surface); color:var(--text); border:1px solid var(--border); border-radius:20px; padding:20px; max-width:90vw; }
  dialog::backdrop { background:rgba(0,0,0,.7); }
  dialog img { max-width:88vw; max-height:80vh; display:block; }
</style>
</head>
<body>
<header><h1>TravelMate <span>Admin</span> · ID review</h1><button id="out" hidden>Sign out</button></header>

<div id="login">
  <h2>Admin key</h2>
  <p class="sub">Enter the key set as <code>Admin__Key</code> on the server.</p>
  <input id="key" type="password" placeholder="Admin key" autocomplete="off">
  <p id="loginErr" class="sub" style="color:#fca5a5"></p>
  <button class="primary" id="go">Open</button>
</div>

<main id="app" hidden>
  <div class="tabs" id="tabs"></div>
  <div id="list"></div>
</main>

<dialog id="zoom"><img id="zoomImg" alt=""></dialog>

<script>
const $ = (id) => document.getElementById(id);
let key = sessionStorage.getItem('adminKey') || '';
let status = 'pending';
const urls = [];

async function api(path, opts = {}) {
  const res = await fetch('/admin/api/verifications' + path, {
    ...opts,
    headers: { 'X-Admin-Key': key, 'Content-Type': 'application/json', ...(opts.headers || {}) },
  });
  if (res.status === 401) throw new Error('unauthorized');
  return res;
}

function el(tag, props = {}, ...kids) {
  const e = Object.assign(document.createElement(tag), props);
  for (const k of kids) e.append(k);
  return e;
}

function fmt(iso) { return new Date(iso).toLocaleString(); }
const DOC = { nationalId: 'National ID (Fayda)', passport: 'Passport', driverLicence: 'Driver licence' };

async function photo(id, slot, label) {
  const box = el('div', { className: 'photo' }, el('span', { textContent: label }));
  try {
    const res = await api(`/${id}/${slot}`);
    if (!res.ok) throw new Error();
    const url = URL.createObjectURL(await res.blob());
    urls.push(url);
    const img = el('img', { src: url, alt: label });
    img.onclick = () => { $('zoomImg').src = url; $('zoom').showModal(); };
    box.append(img);
  } catch { box.append(el('span', { textContent: 'Could not load' })); }
  return box;
}

async function decide(v, st, card) {
  let reason = null;
  if (st === 'rejected') {
    reason = prompt('Tell the user what to fix (they will see this):', 'Photo is blurry or the ID is not fully visible.');
    if (!reason) return;
  } else if (!confirm(`Approve ${v.name || v.phone}?`)) return;
  card.querySelectorAll('button').forEach((b) => (b.disabled = true));
  const res = await api(`/${v.id}/decision`, { method: 'POST', body: JSON.stringify({ status: st, reason }) });
  if (res.ok || res.status === 409) load(); else { alert('Failed: ' + (await res.text())); load(); }
}

async function load() {
  urls.splice(0).forEach(URL.revokeObjectURL);
  const list = $('list');
  list.replaceChildren(el('div', { className: 'empty', textContent: 'Loading…' }));
  $('tabs').replaceChildren(...['pending', 'approved', 'rejected'].map((s) =>
    el('button', { className: s === status ? 'on' : '', textContent: s[0].toUpperCase() + s.slice(1), onclick: () => { status = s; load(); } })));
  let rows;
  try {
    const res = await api('?status=' + status);
    if (!res.ok) throw new Error();
    rows = await res.json();
  } catch (e) {
    if (e.message === 'unauthorized') return signOut('Wrong key.');
    return list.replaceChildren(el('div', { className: 'err', textContent: 'Could not load submissions.' }));
  }
  if (!rows.length) return list.replaceChildren(el('div', { className: 'empty', textContent: `No ${status} submissions.` }));
  const cards = [];
  for (const v of rows) {
    const card = el('div', { className: 'card' });
    card.append(el('div', { className: 'head' },
      el('b', { textContent: v.name || '(no name yet)' }),
      el('span', { className: 'sub', textContent: v.phone }),
      el('span', { className: 'pill', textContent: DOC[v.docType] || v.docType }),
      el('span', { className: 'sub', textContent: 'Submitted ' + fmt(v.submittedAt) })));
    const photos = el('div', { className: 'photos' });
    card.append(photos);
    Promise.all([photo(v.id, 'front', 'Front'), photo(v.id, 'back', 'Back'), photo(v.id, 'selfie', 'Selfie')])
      .then((p) => photos.append(...p));
    if (v.status === 'pending') {
      card.append(el('div', { className: 'actions' },
        el('button', { className: 'primary', textContent: 'Approve', onclick: () => decide(v, 'approved', card) }),
        el('button', { className: 'bad', textContent: 'Reject…', onclick: () => decide(v, 'rejected', card) })));
    } else {
      card.append(el('p', { className: 'sub', textContent: `${v.status} ${v.reviewedAt ? fmt(v.reviewedAt) : ''}${v.reason ? ' · ' + v.reason : ''}` }));
    }
    cards.push(card);
  }
  list.replaceChildren(...cards);
}

function signOut(msg = '') {
  key = ''; sessionStorage.removeItem('adminKey');
  $('app').hidden = true; $('out').hidden = true; $('login').hidden = false;
  $('loginErr').textContent = msg;
}

function enter() {
  $('login').hidden = true; $('app').hidden = false; $('out').hidden = false;
  load();
}

$('go').onclick = () => { key = $('key').value.trim(); if (!key) return; sessionStorage.setItem('adminKey', key); enter(); };
$('key').onkeydown = (e) => { if (e.key === 'Enter') $('go').click(); };
$('out').onclick = () => signOut();
$('zoom').onclick = () => $('zoom').close();
if (key) enter();
</script>
</body>
</html>
""";
}
