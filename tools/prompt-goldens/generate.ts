// Runs upstream's generateSystemPrompt over every case and writes the goldens.
// Use run.sh: it prepares the scratch copy of upstream this script imports from.

import { mkdirSync, readdirSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { join, resolve } from "node:path";
import { parseArgs } from "node:util";
import { buildLibraries, cases } from "./cases";

const { values } = parseArgs({
  options: {
    upstream: { type: "string" }, // the copy of packages/lang-core/src
    library: { type: "string" }, // tests/fixtures/1.0/library.json
    out: { type: "string" }, // the Goldens directory
  },
});
if (!values.upstream || !values.library || !values.out) {
  throw new Error("usage: generate.ts --upstream <lang-core/src> --library <library.json> --out <dir>");
}
// Prompt generation records telemetry unless it is switched off.
if (!process.env.OPENUI_TELEMETRY_DISABLED || !process.env.DO_NOT_TRACK) {
  throw new Error("set OPENUI_TELEMETRY_DISABLED=1 and DO_NOT_TRACK=1 before generating");
}

const { generateSystemPrompt } = await import(join(resolve(values.upstream), "parser/prompt.ts"));

const out = resolve(values.out);
const librariesDir = join(out, "libraries");
for (const file of readdirSync(out)) {
  if (file === "cases.json" || file.endsWith(".prompt.txt")) rmSync(join(out, file));
}
rmSync(librariesDir, { recursive: true, force: true });
mkdirSync(librariesDir, { recursive: true });

const fixtures = JSON.parse(readFileSync(values.library, "utf8"));
const libraries = buildLibraries(fixtures);
for (const [name, library] of Object.entries(libraries)) {
  if (name !== "fixtures") writeFileSync(join(librariesDir, `${name}.json`), json(library));
}

const seen = new Set<string>();
for (const { name, library, options } of cases) {
  if (seen.has(name)) throw new Error(`duplicate case name: ${name}`);
  seen.add(name);
  const prompt = generateSystemPrompt({ library: libraries[library], promptOptions: options });
  writeFileSync(join(out, `${name}.prompt.txt`), prompt);
}
writeFileSync(join(out, "cases.json"), json(cases));
console.log(`wrote ${cases.length} goldens to ${out}`);

function json(value: unknown): string {
  return `${JSON.stringify(value, null, 2)}\n`;
}
