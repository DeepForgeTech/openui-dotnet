// Runs ECMAScript operations inside whichever engine executes this file (bun or node) and
// prints the results as JSON. generate.ts runs it under both and compares them.
//
// stdin:  { numberToString, stringToNumber, parseFloat, mathRound, values, collation }
// stdout: the same keys with the engine's answers.
import { readFileSync } from "node:fs";
import { fromBits, toBits } from "./bits.mjs";

/** Rebuilds JS values from JSON descriptors. Objects keep identity per list slot. */
function materialize(descriptor) {
  switch (descriptor.t) {
    case "null":
      return null;
    case "string":
      return descriptor.v;
    case "number":
      return fromBits(descriptor.bits);
    case "bool":
      return descriptor.v;
    case "array":
      return descriptor.items.map(materialize);
    case "object":
      return Object.fromEntries(Object.entries(descriptor.fields ?? {}).map(([k, v]) => [k, materialize(v)]));
    default:
      throw new Error(`bad descriptor ${JSON.stringify(descriptor)}`);
  }
}

function probeValues(descriptors) {
  const values = descriptors.map(materialize);
  return {
    string: values.map(String),
    number: values.map((v) => toBits(Number(v))),
    looseEquals: values.map((a) => values.map((b) => (a == b ? "1" : "0")).join("")),
  };
}

function probeCollation(strings) {
  const sign = (n) => (n < 0 ? "<" : n > 0 ? ">" : "=");
  return {
    matrix: strings.map((a) => strings.map((b) => sign(a.localeCompare(b))).join("")),
    sorted: [...strings].sort((a, b) => a.localeCompare(b)),
  };
}

const request = JSON.parse(readFileSync(0, "utf8"));

console.log(
  JSON.stringify({
    runtime: typeof Bun !== "undefined" ? `bun ${Bun.version}` : `node ${process.version}`,
    icu: process.versions.icu,
    locale: new Intl.Collator().resolvedOptions().locale,
    numberToString: request.numberToString.map((bits) => String(fromBits(bits))),
    stringToNumber: request.stringToNumber.map((s) => toBits(Number(s))),
    parseFloat: request.parseFloat.map((s) => toBits(parseFloat(s))),
    mathRound: request.mathRound.map((bits) => toBits(Math.round(fromBits(bits)))),
    values: probeValues(request.values),
    collation: probeCollation(request.collation),
  }),
);
