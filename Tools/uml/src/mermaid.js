"use strict";

const VIS_SYMBOL = {
  public: "+",
  private: "-",
  protected: "#",
  internal: "~"
};

// Past this many members a box stops being readable. Truncation is announced
// rather than silent - GrabInteract alone parses to 49 fields.
const MEMBER_CAP = 25;

function sanitize(name) {
  // Mermaid chokes on generics/brackets in identifiers; keep display type but
  // sanitize the node id.
  return name.replace(/[^\w]/g, "_");
}

// Mermaid spells generics with tildes (List~int~), not angle brackets, and any
// space inside a member's type splits the line at the wrong place.
function formatType(type) {
  return type.replace(/\s+/g, "").replace(/</g, "~").replace(/>/g, "~");
}

// Every identifier in a type, so element types count too: List<ObjectPhysics>
// yields List and ObjectPhysics, and the caller keeps only names it knows.
function typeNames(type) {
  return type.match(/[A-Za-z_]\w*/g) || [];
}

function toMermaid(classes) {
  const lines = ["classDiagram"];

  for (const c of classes) {
    lines.push(`  class ${sanitize(c.name)}["${c.name}"] {`);
    if (c.kind === "interface") lines.push(`    <<interface>>`);
    if (c.kind === "enum") lines.push(`    <<enumeration>>`);
    if (c.kind === "struct") lines.push(`    <<struct>>`);

    for (const f of c.fields.slice(0, MEMBER_CAP)) {
      const sym = VIS_SYMBOL[f.visibility] || "~";
      lines.push(`    ${sym}${formatType(f.type)} ${f.name}`);
    }
    if (c.fields.length > MEMBER_CAP) {
      lines.push(`    +and_${c.fields.length - MEMBER_CAP}_more_fields`);
    }

    for (const m of c.methods.slice(0, MEMBER_CAP)) {
      const sym = VIS_SYMBOL[m.visibility] || "~";
      lines.push(`    ${sym}${m.name}(${m.params ? "..." : ""}) ${formatType(m.returnType)}`);
    }
    if (c.methods.length > MEMBER_CAP) {
      lines.push(`    +and_${c.methods.length - MEMBER_CAP}_more_methods()`);
    }

    lines.push(`  }`);
  }

  // Relationships
  const known = new Set(classes.map((c) => c.name));
  const byName = new Map(classes.map((c) => [c.name, c]));
  const drawn = new Set();
  const relatedPairs = new Set();

  function edge(from, arrow, to) {
    const key = `${from} ${arrow} ${to}`;
    if (drawn.has(key)) return;
    drawn.add(key);
    relatedPairs.add(`${from}>${to}`);
    lines.push(`  ${sanitize(from)} ${arrow} ${sanitize(to)}`);
  }

  for (const c of classes) {
    for (const parent of c.heritage) {
      if (!known.has(parent)) continue; // skip MonoBehaviour, IEnumerator, etc - noisy, not useful
      // Heuristic: if the parent name starts with "I" + uppercase, treat as interface implementation.
      const isInterface =
        /^I[A-Z]/.test(parent) && byName.get(parent)?.kind === "interface";
      edge(c.name, isInterface ? "..|>" : "--|>", parent);
    }
  }

  // Association edges. Without these the diagram is a field of disconnected
  // boxes: every gameplay class extends MonoBehaviour, which lives outside the
  // repo and is dropped just above, so inheritance alone draws nothing at all.
  // A field whose type is another type in the repo is where the real structure
  // lives - GrabInteract holding a PlayerCamera is the thing worth seeing.
  for (const c of classes) {
    for (const f of c.fields) {
      for (const target of typeNames(f.type)) {
        if (target === c.name || !known.has(target)) continue;
        // An inheritance arrow already says more than an association would.
        if (relatedPairs.has(`${c.name}>${target}`)) continue;
        edge(c.name, "-->", target);
      }
    }
  }

  return lines.join("\n");
}

module.exports = { toMermaid };
