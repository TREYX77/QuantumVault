# UML diagram generator

Generates a UML class diagram from `Assets/Scripts` and publishes it as a web
page. Regex-based C# parsing — no Roslyn, no .NET SDK, just Node.

## The published page

The diagram is hosted on Cloudflare Pages and **rebuilds itself on every push to
`dev`**. Nobody's PC needs to be on.

Cloudflare project settings:

| Setting | Value |
| --- | --- |
| Production branch | `dev` |
| Framework preset | None |
| Build command | `node Tools/uml/build.js` |
| Build output directory | `dist` |
| Root directory | `/` |

There is deliberately **no `package.json` at the repo root**, so Cloudflare runs
no install step — `build.js`, `parser.js`, `mermaid.js` and `page.js` use only
Node builtins. Builds take well under a second.

Other branches get their own preview URLs automatically, which is handy for
seeing a feature branch's structure before it merges.

## Local live mode

For editing: a local server that re-parses and redraws the moment you save a
`.cs` file, no refresh needed.

```bash
cd Tools/uml
npm install      # only needed for this mode
npm start        # -> http://localhost:3000
```

## Building the page by hand

```bash
node Tools/uml/build.js          # from the repo root
# or
cd Tools/uml && npm run build
```

Writes `dist/index.html` and `dist/diagram.mmd`. `dist/` is gitignored.

## What gets diagrammed

Only `Assets/Scripts`. Everything else under `Assets` — TextMesh Pro samples,
imported package examples — is third-party noise; on this project it was 47 of
59 types, which buried the 12 that matter.

Unity's build-cache folders (`Library/`, `Temp/`, `obj/`, `bin/`) are ignored,
but **only at the project root**, so `Assets/Scripts/Temp/GUIstats.cs` is still
parsed.

## What the arrows mean

- `--|>` inheritance, `..|>` interface implementation — drawn only between types
  defined in this repo. `MonoBehaviour`, `IEnumerator` and other engine types are
  left out on purpose; an arrow to `MonoBehaviour` from every class says nothing.
- `-->` association — class A has a field whose type is class B. Because nearly
  every class here extends `MonoBehaviour` and nothing else, these are the edges
  that actually carry information, and they're what shows how the systems connect.

## Known limits

It's a regex parser, not a compiler. It handles classes, interfaces, structs,
enums, fields, methods, auto-properties and events. Expect gaps on heavy
generics, partial classes split across files, and deeply nested types. Failures
are silent omissions, not crashes — if a class is missing from the diagram, that's
why.

Boxes cap at 25 fields and 25 methods, with an `and_N_more_fields` row so the
truncation is visible rather than silent.
