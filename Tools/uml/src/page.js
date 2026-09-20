"use strict";

/**
 * One renderer for both modes, so the styling lives in a single place.
 *
 *   live: true   - served by server.js, pulls the diagram over a websocket and
 *                  redraws on every file save.
 *   live: false  - written to disk by build.js, diagram baked in. No sockets,
 *                  nothing to connect to, works as a plain static file on
 *                  Cloudflare Pages with the authoring machine switched off.
 */

// Embedding untrusted-ish text in a <script> is only safe if "</script>" can
// never appear in it. Escaping the angle brackets guarantees that, and JSON
// survives it unchanged.
function jsonForScript(value) {
  return JSON.stringify(value)
    .replace(/</g, "\\u003c")
    .replace(/>/g, "\\u003e")
    .replace(/\u2028/g, "\\u2028")
    .replace(/\u2029/g, "\\u2029");
}

function escapeHtml(text) {
  return String(text)
    .replace(/&/g, "&amp;")
    .replace(/</g, "&lt;")
    .replace(/>/g, "&gt;")
    .replace(/"/g, "&quot;");
}

const STYLES = `
  :root {
    --bg: #0f1115;
    --panel: #171a21;
    --border: #2a2e37;
    --text: #e6e8eb;
    --muted: #8a8f98;
    --accent: #6ee7b7;
  }
  * { box-sizing: border-box; }
  body {
    margin: 0;
    background: var(--bg);
    color: var(--text);
    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif;
  }
  header {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 16px;
    padding: 14px 20px;
    background: var(--panel);
    border-bottom: 1px solid var(--border);
    position: sticky;
    top: 0;
    z-index: 10;
  }
  header h1 {
    font-size: 15px;
    margin: 0;
    font-weight: 600;
    color: var(--text);
    white-space: nowrap;
  }
  .status {
    display: flex;
    align-items: center;
    gap: 14px;
    font-size: 12px;
    color: var(--muted);
    flex-wrap: wrap;
    justify-content: flex-end;
  }
  .dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    background: #555;
    display: inline-block;
    margin-right: 6px;
  }
  .dot.live { background: var(--accent); box-shadow: 0 0 6px var(--accent); }
  .dot.down { background: #f87171; }
  code.sha {
    font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
    color: var(--text);
  }
  a.export {
    color: var(--accent);
    text-decoration: none;
    border: 1px solid var(--border);
    padding: 6px 12px;
    border-radius: 6px;
    font-size: 12px;
    white-space: nowrap;
  }
  a.export:hover { border-color: var(--accent); }
  #wrap {
    overflow: auto;
    padding: 30px;
    min-height: calc(100vh - 56px);
  }
  #diagram { min-width: 100%; }
  .empty {
    color: var(--muted);
    text-align: center;
    margin-top: 80px;
    font-size: 14px;
  }
  @media (max-width: 640px) {
    header { flex-direction: column; align-items: flex-start; }
    .status { justify-content: flex-start; }
    #wrap { padding: 16px; }
  }
`;

// Shared by both modes: turn Mermaid source into SVG, report failures visibly
// rather than leaving a blank page.
const RENDER_JS = `
mermaid.initialize({ startOnLoad: false, theme: "dark", securityLevel: "loose" });

const diagramEl = document.getElementById("diagram");
let renderSeq = 0;

async function render(source) {
  const id = "d" + (renderSeq++);
  diagramEl.classList.remove("empty");
  try {
    const { svg } = await mermaid.render(id, source);
    diagramEl.innerHTML = svg;
  } catch (err) {
    diagramEl.innerHTML =
      '<div class="empty">Diagram failed to render (likely a parse edge case).</div>';
    console.error(err);
  }
}
`;

const LIVE_JS = `
const dot = document.getElementById("dot");
const conn = document.getElementById("conn");
const statsEl = document.getElementById("stats");

function applyStats(stats) {
  if (!stats || !stats.generatedAt) return;
  const t = new Date(stats.generatedAt).toLocaleTimeString();
  statsEl.textContent = \`\${stats.classCount} types · \${stats.fileCount} files · updated \${t}\`;
}

function connect() {
  const proto = location.protocol === "https:" ? "wss:" : "ws:";
  const ws = new WebSocket(\`\${proto}//\${location.host}\`);

  ws.onopen = () => {
    dot.className = "dot live";
    conn.textContent = "live";
  };
  ws.onclose = () => {
    dot.className = "dot down";
    conn.textContent = "reconnecting…";
    setTimeout(connect, 1500);
  };
  ws.onerror = () => ws.close();
  ws.onmessage = (evt) => {
    const msg = JSON.parse(evt.data);
    if (msg.type === "update") {
      render(msg.diagram);
      applyStats(msg.stats);
    }
  };
}

// Initial load via REST in case WS is slow to open
fetch("/api/diagram")
  .then((r) => r.json())
  .then((data) => {
    render(data.diagram);
    applyStats(data.stats);
  })
  .catch(() => {});

connect();
`;

/**
 * @param {object}  opts
 * @param {string} [opts.diagram]  Mermaid source. Required when live is false.
 * @param {object} [opts.stats]    { classCount, fileCount }
 * @param {boolean} opts.live
 * @param {object} [opts.build]    { branch, sha, builtAt } for the static stamp.
 */
function renderPage({ diagram = "", stats = {}, live = false, build = {} } = {}) {
  const exportHref = live ? "/api/export/mermaid" : "diagram.mmd";

  const statusHtml = live
    ? `<span id="stats"></span>
    <span><span class="dot" id="dot"></span><span id="conn">connecting…</span></span>`
    : `<span>${escapeHtml(
        `${stats.classCount ?? 0} types · ${stats.fileCount ?? 0} files`
      )}</span>
    <span>${buildStamp(build)}</span>`;

  const bodyHtml = live
    ? `<div id="diagram" class="empty">Waiting for first diagram…</div>`
    : `<div id="diagram"></div>`;

  const script = live
    ? RENDER_JS + LIVE_JS
    : RENDER_JS + `\nrender(${jsonForScript(diagram)});\n`;

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8" />
<meta name="viewport" content="width=device-width, initial-scale=1" />
<title>${live ? "Live Unity UML" : "Unity UML"}</title>
<script src="https://cdnjs.cloudflare.com/ajax/libs/mermaid/10.9.1/mermaid.min.js"></script>
<style>${STYLES}</style>
</head>
<body>
<header>
  <h1>${live ? "Unity Live UML" : "Unity UML"}</h1>
  <div class="status">
    ${statusHtml}
    <a class="export" href="${exportHref}" download>Export .mmd</a>
  </div>
</header>
<div id="wrap">
  ${bodyHtml}
</div>

<script>
${script}
</script>
</body>
</html>
`;
}

// Tells a viewer how stale the page is, which matters once it is published and
// nobody can see the build that produced it.
function buildStamp({ branch, sha, builtAt }) {
  const parts = [];
  if (branch) parts.push(escapeHtml(branch));
  if (sha) parts.push(`<code class="sha">${escapeHtml(sha)}</code>`);
  const where = parts.length ? parts.join(" @ ") : "local build";
  const when = builtAt ? new Date(builtAt).toUTCString() : "";
  return when ? `${where} · built ${escapeHtml(when)}` : where;
}

module.exports = { renderPage };
