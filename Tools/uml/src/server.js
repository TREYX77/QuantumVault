"use strict";

const path = require("path");
const fs = require("fs");
const express = require("express");
const chokidar = require("chokidar");
const { WebSocketServer } = require("ws");
const { parseRepo, IGNORE_DIRS_SCOPED } = require("./parser");
const { toMermaid } = require("./mermaid");
const { renderPage } = require("./page");

const REPO_PATH = process.env.UML_REPO_PATH || process.argv[2];
const PORT = process.env.UML_PORT || 3000;

if (!REPO_PATH) {
  console.error(
    "\nUsage: node src/server.js /path/to/your/UnityProject\n" +
    "   or: UML_REPO_PATH=/path/to/repo npm start\n"
  );
  process.exit(1);
}

const resolvedRepo = path.resolve(REPO_PATH);
if (!fs.existsSync(resolvedRepo)) {
  console.error(`Path does not exist: ${resolvedRepo}`);
  process.exit(1);
}

// Unity keeps gameplay code in Assets/Scripts. Everything else under Assets
// (TextMesh Pro samples, imported packages) is third-party code that swamps the
// diagram - on this project it was 47 of 59 types. Scope to Assets/Scripts when
// the folder exists, and fall back to the given path for non-standard layouts.
const scriptsDir = path.join(resolvedRepo, "Assets", "Scripts");
const scopedToScripts = fs.existsSync(scriptsDir);
const scanRoot = scopedToScripts ? scriptsDir : resolvedRepo;
const ignoreDirs = scopedToScripts ? IGNORE_DIRS_SCOPED : undefined;

const app = express();
// The page comes from the same renderer the static build uses, so there is one
// copy of the markup and styling.
app.get("/", (req, res) => {
  res.type("html").send(renderPage({ live: true }));
});

let latestDiagram = "classDiagram\n  class Loading";
let latestStats = { classCount: 0, fileCount: 0, generatedAt: null };

function regenerate() {
  const start = Date.now();
  const classes = parseRepo(scanRoot, ignoreDirs);
  latestDiagram = toMermaid(classes);
  latestStats = {
    classCount: classes.length,
    fileCount: new Set(classes.map((c) => c.file)).size,
    generatedAt: new Date().toISOString(),
    tookMs: Date.now() - start
  };
  console.log(
    `[uml] regenerated: ${latestStats.classCount} types from ${latestStats.fileCount} files (${latestStats.tookMs}ms)`
  );
  broadcast();
}

app.get("/api/diagram", (req, res) => {
  res.json({ diagram: latestDiagram, stats: latestStats });
});

app.get("/api/export/mermaid", (req, res) => {
  res.setHeader("Content-Type", "text/plain");
  res.setHeader("Content-Disposition", "attachment; filename=diagram.mmd");
  res.send(latestDiagram);
});

const server = app.listen(PORT, () => {
  console.log(`\n  Unity live UML running at http://localhost:${PORT}`);
  console.log(`  Watching: ${scanRoot}`);
  if (!scopedToScripts) {
    console.log(`  (no Assets/Scripts found - scanning the whole path)`);
  }
  console.log(`  Export as .mmd any time at /api/export/mermaid`);
  console.log(`\n  To expose it publicly:`);
  console.log(`    cloudflared tunnel --url http://localhost:${PORT}\n`);
});

const wss = new WebSocketServer({ server });

function broadcast() {
  const payload = JSON.stringify({ type: "update", diagram: latestDiagram, stats: latestStats });
  for (const client of wss.clients) {
    if (client.readyState === 1) client.send(payload);
  }
}

wss.on("connection", (ws) => {
  ws.send(JSON.stringify({ type: "update", diagram: latestDiagram, stats: latestStats }));
});

regenerate();

const watcher = chokidar.watch(scanRoot, {
  // Mirror the parser's ignore set, or the watcher regenerates on a change to
  // a file the parser then refuses to read.
  ignored: scopedToScripts
    ? [/(^|[/\\])\../, /(^|[/\\])node_modules([/\\]|$)/]
    : [
        /(^|[/\\])\../,
        /(^|[/\\])(Library|Temp|obj|bin|Logs|UserSettings|\.vs|\.idea|Build|Builds|node_modules)([/\\]|$)/
      ],
  ignoreInitial: true,
  persistent: true
});

let debounceTimer = null;
watcher.on("all", (event, changedPath) => {
  if (!changedPath.endsWith(".cs")) return;
  clearTimeout(debounceTimer);
  debounceTimer = setTimeout(regenerate, 400); // debounce rapid saves
});
