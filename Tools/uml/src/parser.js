"use strict";

const fs = require("fs");
const path = require("path");

/**
 * Very deliberately regex-based, not Roslyn-based.
 * Tradeoff: it won't handle every insane C# edge case (nested generics inside
 * nested generics inside a tuple, partial classes split across 6 files, etc).
 * What it WILL do: run on any machine with just Node installed, no .NET SDK,
 * no MSBuild, no project-loading headaches. For Unity MonoBehaviours and
 * regular gameplay classes this covers the real-world 95% case.
 */

// Tooling junk, ignored wherever it turns up.
const VCS_AND_TOOLING = ["node_modules", ".git", ".vs", ".idea"];

// Unity's build caches, which only ever sit at the PROJECT ROOT. Matching these
// by name at any depth is wrong: Assets/Scripts/Temp/GUIstats.cs is the user's
// own code and was being dropped silently because the folder is called Temp.
const UNITY_ARTIFACTS = [
  "Library", "Temp", "obj", "bin", "Logs", "UserSettings", "Build", "Builds"
];

const IGNORE_DIRS = new Set([...VCS_AND_TOOLING, ...UNITY_ARTIFACTS]);

// Used when the scan is already scoped inside Assets/Scripts, where none of the
// Unity artifact names can be a build cache.
const IGNORE_DIRS_SCOPED = new Set(VCS_AND_TOOLING);

function walk(dir, acc = [], ignore = IGNORE_DIRS) {
  let entries;
  try {
    entries = fs.readdirSync(dir, { withFileTypes: true });
  } catch {
    return acc;
  }
  for (const entry of entries) {
    if (ignore.has(entry.name)) continue;
    const full = path.join(dir, entry.name);
    if (entry.isDirectory()) {
      walk(full, acc, ignore);
    } else if (entry.isFile() && entry.name.endsWith(".cs")) {
      acc.push(full);
    }
  }
  return acc;
}

// Strip comments and string literals so they don't confuse the regexes below.
function stripNoise(src) {
  return src
    .replace(/\/\*[\s\S]*?\*\//g, " ")
    .replace(/\/\/.*$/gm, " ")
    .replace(/"(?:[^"\\]|\\.)*"/g, '""')
    .replace(/@"(?:[^"]|"")*"/g, '""');
}

const TYPE_RE =
  /\b(?:public|private|protected|internal|static|sealed|abstract|partial|\s)*\b(class|interface|struct|enum)\s+([A-Za-z_]\w*)\s*(<[^>{]+>)?\s*(:\s*([^{]+))?\s*\{/g;

const FIELD_RE =
  /^\s*(?:\[[^\]]*\]\s*)*(public|private|protected|internal)\s+(?:static\s+|readonly\s+|const\s+|event\s+|volatile\s+)*([\w<>\[\],\.\s]+?)\s+([A-Za-z_]\w*)\s*(=|;)/gm;

// Auto-properties -- 'public bool LookEnabled { get; set; }' -- never match
// FIELD_RE, which needs a '=' or ';' right after the name; the '{' blocks it, so
// they were being dropped silently. Match them separately rather than loosening
// FIELD_RE: relaxing that anchor pulls in false positives from method bodies.
const PROPERTY_RE =
  /^\s*(?:\[[^\]]*\]\s*)*(public|private|protected|internal)\s+(?:static\s+|virtual\s+|override\s+|abstract\s+|readonly\s+|new\s+)*([\w<>\[\],\.\s]+?)\s+([A-Za-z_]\w*)\s*\{\s*(?:\[[^\]]*\]\s*)*(?:get|set|add|remove)\b/gm;

// A modifier that slips past the groups above lands in the type slot and puts a
// space in the Mermaid member line, which breaks the render. 'event Action<float>
// Progress' was doing exactly that.
const TYPE_MODIFIERS = new Set([
  "event", "readonly", "const", "static", "volatile", "extern",
  "virtual", "override", "abstract", "new", "sealed", "async", "partial", "ref"
]);

function cleanType(raw) {
  const parts = raw.replace(/\s+/g, " ").trim().split(" ");
  while (parts.length > 1 && TYPE_MODIFIERS.has(parts[0])) parts.shift();
  return parts.join(" ");
}

const METHOD_RE =
  /^\s*(?:\[[^\]]*\]\s*)*(public|private|protected|internal)\s+(?:static\s+|virtual\s+|override\s+|abstract\s+|async\s+)*([\w<>\[\],\.\s]+?)\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*(\{|=>|;)/gm;

function parseFile(filePath) {
  const raw = fs.readFileSync(filePath, "utf8");
  const src = stripNoise(raw);
  const classes = [];

  let m;
  TYPE_RE.lastIndex = 0;
  while ((m = TYPE_RE.exec(src)) !== null) {
    const kind = m[1]; // class | interface | struct | enum
    const name = m[2];
    const heritageRaw = m[5] || "";
    const heritage = heritageRaw
      .split(",")
      .map((s) => s.trim().split("<")[0].trim())
      .filter(Boolean);

    // Find the matching closing brace by scanning from the opening one,
    // so field/method regexes only run inside THIS type's body.
    const openIdx = m.index + m[0].length - 1;
    const body = extractBody(src, openIdx);

    const fields = [];
    const methods = [];

    if (kind !== "enum") {
      // C# forbids two members sharing a name, so deduping by name is safe and
      // stops a member being listed twice when both regexes reach it.
      const seenMembers = new Set();
      let fm;
      FIELD_RE.lastIndex = 0;
      while ((fm = FIELD_RE.exec(body)) !== null) {
        if (seenMembers.has(fm[3])) continue;
        seenMembers.add(fm[3]);
        fields.push({
          visibility: fm[1],
          type: cleanType(fm[2]),
          name: fm[3]
        });
      }
      let pm;
      PROPERTY_RE.lastIndex = 0;
      while ((pm = PROPERTY_RE.exec(body)) !== null) {
        if (seenMembers.has(pm[3])) continue;
        seenMembers.add(pm[3]);
        fields.push({
          visibility: pm[1],
          type: cleanType(pm[2]),
          name: pm[3]
        });
      }
      let mm;
      METHOD_RE.lastIndex = 0;
      while ((mm = METHOD_RE.exec(body)) !== null) {
        // Filter out constructors misparsed as methods with no return type,
        // and skip anything that looks like a field assignment caught by accident.
        if (mm[3] === name) continue; // constructor
        methods.push({
          visibility: mm[1],
          returnType: cleanType(mm[2]),
          name: mm[3],
          params: mm[4].trim()
        });
      }
    }

    classes.push({
      kind,
      name,
      heritage,
      fields,
      methods,
      file: filePath
    });
  }

  return classes;
}

function extractBody(src, openBraceIdx) {
  let depth = 0;
  for (let i = openBraceIdx; i < src.length; i++) {
    if (src[i] === "{") depth++;
    else if (src[i] === "}") {
      depth--;
      if (depth === 0) return src.slice(openBraceIdx + 1, i);
    }
  }
  return src.slice(openBraceIdx + 1);
}

function parseRepo(rootDir, ignore = IGNORE_DIRS) {
  const files = walk(rootDir, [], ignore);
  const classes = [];
  for (const f of files) {
    try {
      classes.push(...parseFile(f));
    } catch (err) {
      console.warn(`[parser] skipped ${f}: ${err.message}`);
    }
  }
  return classes;
}

module.exports = { parseRepo, parseFile, walk, IGNORE_DIRS, IGNORE_DIRS_SCOPED };
