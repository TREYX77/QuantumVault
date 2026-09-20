"use strict";

/**
 * Static build. Parses the project once and writes a self-contained page.
 *
 * This is what Cloudflare Pages runs on every push to dev:
 *
 *   Build command:           node Tools/uml/build.js
 *   Build output directory:  dist
 *
 * It uses only Node builtins, so there is no package.json at the repo root and
 * therefore no npm install step in the build - it finishes in milliseconds.
 */

const fs = require("fs");
const path = require("path");
const { execFileSync } = require("child_process");

const { parseRepo, IGNORE_DIRS_SCOPED } = require("./src/parser");
const { toMermaid } = require("./src/mermaid");
const { renderPage } = require("./src/page");

// Cloudflare runs the build from the repo root; a human might run it from
// anywhere, so accept an explicit path too.
const repoRoot = path.resolve(process.argv[2] || process.cwd());
const outDir = path.resolve(process.env.UML_OUT_DIR || path.join(repoRoot, "dist"));

const scriptsDir = path.join(repoRoot, "Assets", "Scripts");
const scopedToScripts = fs.existsSync(scriptsDir);
const scanRoot = scopedToScripts ? scriptsDir : repoRoot;

function gitInfo() {
  // Cloudflare exposes these; fall back to git so a local build is stamped too.
  const branch =
    process.env.CF_PAGES_BRANCH || tryGit(["rev-parse", "--abbrev-ref", "HEAD"]);
  const sha =
    (process.env.CF_PAGES_COMMIT_SHA || tryGit(["rev-parse", "HEAD"]) || "").slice(0, 7);
  return { branch, sha, builtAt: new Date().toISOString() };
}

function tryGit(args) {
  try {
    return execFileSync("git", args, {
      cwd: repoRoot,
      encoding: "utf8",
      stdio: ["ignore", "pipe", "ignore"]
    }).trim();
  } catch {
    return "";
  }
}

function main() {
  const started = Date.now();

  if (!fs.existsSync(repoRoot)) {
    console.error(`[uml] path does not exist: ${repoRoot}`);
    process.exit(1);
  }
  if (!scopedToScripts) {
    console.warn(`[uml] no Assets/Scripts under ${repoRoot} - scanning the whole path`);
  }

  const classes = parseRepo(scanRoot, scopedToScripts ? IGNORE_DIRS_SCOPED : undefined);
  const diagram = toMermaid(classes);
  const stats = {
    classCount: classes.length,
    fileCount: new Set(classes.map((c) => c.file)).size
  };

  // An empty diagram means the scan root was wrong - on a case-sensitive build
  // machine that is the difference between Assets/scripts and Assets/Scripts.
  // Failing loudly here beats publishing a blank page.
  if (classes.length === 0) {
    console.error(`[uml] parsed 0 types from ${scanRoot} - refusing to publish an empty diagram`);
    process.exit(1);
  }

  fs.mkdirSync(outDir, { recursive: true });
  fs.writeFileSync(
    path.join(outDir, "index.html"),
    renderPage({ diagram, stats, live: false, build: gitInfo() })
  );
  fs.writeFileSync(path.join(outDir, "diagram.mmd"), diagram + "\n");

  const edges = diagram.split("\n").filter((l) => /(-->|--\|>|\.\.\|>)/.test(l)).length;
  console.log(
    `[uml] ${stats.classCount} types, ${edges} edges from ${stats.fileCount} files ` +
      `-> ${outDir} (${Date.now() - started}ms)`
  );
}

main();
