// Regenerates the differential test data under tests/OpenUI.Lang.Core.Tests/Ecma/Data.
//
//   bun tools/ecma-expected/generate.ts
//
// Expected values come from running real JavaScript. engine-probe.mjs runs under node (the engine
// the spec names, so its answers are committed) and under bun; any disagreement between the two
// is written to Data/engine-differences.json instead of being hidden.
import { mkdirSync } from "node:fs";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath } from "node:url";
import { numbers, strings, type Descriptor } from "./corpora.ts";
import { toBits } from "./bits.mjs";

const here = dirname(fileURLToPath(import.meta.url));
const repo = resolve(here, "../..");
const ecmaData = join(repo, "tests/OpenUI.Lang.Core.Tests/Ecma/Data");

// ---------------------------------------------------------------- seeded randomness

function mulberry32(seed: number) {
  let a = seed;
  return () => {
    a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

const random = mulberry32(0x0e1c0de);
const pick = <T>(items: readonly T[]) => items[Math.floor(random() * items.length)];
const randomInt = (min: number, max: number) => min + Math.floor(random() * (max - min + 1));

function randomDouble(): number {
  const view = new DataView(new ArrayBuffer(8));
  view.setUint32(0, Math.floor(random() * 2 ** 32));
  view.setUint32(4, Math.floor(random() * 2 ** 32));
  return view.getFloat64(0);
}

// ---------------------------------------------------------------- operands

function numberOperands(): number[] {
  const out = [...numbers, ...numbers.map((n) => -n)];
  for (let i = 0; i < 1500; i++) out.push(randomDouble());
  for (let i = 0; i < 1000; i++) out.push(Number(`${randomInt(1, 10 ** 15) * randomInt(1, 9999)}e${randomInt(-40, 40)}`));
  for (let i = 0; i < 300; i++) out.push(randomInt(0, 2 ** 53) * 2 ** randomInt(0, 20));
  for (let i = 0; i < 300; i++) out.push(random() * 10 ** randomInt(-9, 25));
  for (let e = -330; e <= 310; e += 7) out.push(10 ** e, 9.999999999999999 * 10 ** e);
  return out;
}

function roundOperands(): number[] {
  const out = [-0.5, 0.5, 1.5, 2.5, -1.5, -2.5, -0.49999999999999994, 0.49999999999999994, 0.5000000000000001,
    -0.5000000000000001, 4503599627370495.5, 4503599627370497, -4503599627370495.5, 9007199254740991, 2 ** 53, 1e21,
    -1e21, 1e300, 5e-324, -5e-324, 0, -0, NaN, Infinity, -Infinity, 0.1, -0.1, 0.9, -0.9, 1.4999999999999998, 2.4999999999999996,
    -2.5000000000000004, 123.456, -123.456, 1.005 * 1000, 0.285 * 100, 1.255 * 100];
  for (let i = 0; i < 400; i++) out.push(randomInt(-100000, 100000) + 0.5, randomInt(-1000, 1000) + random());
  for (let i = 0; i < 300; i++) out.push(randomDouble());
  for (let i = 0; i < 200; i++) out.push((randomInt(-(2 ** 51), 2 ** 51) + 0.5) * 2 ** randomInt(-3, 0));
  return out;
}

function stringOperands(): string[] {
  const alphabet = [..." \t\n+-.eE0123456789xXbBoO_aAfF"];
  const out = [...strings];
  for (let i = 0; i < 4000; i++) {
    out.push(Array.from({ length: randomInt(0, 9) }, () => pick(alphabet)).join(""));
  }
  const digits = "0123456789";
  for (let i = 0; i < 600; i++) {
    const sign = pick(["", "", "-", "+"]);
    const int = Array.from({ length: randomInt(0, 25) }, () => pick(digits)).join("");
    const frac = random() < 0.6 ? "." + Array.from({ length: randomInt(0, 25) }, () => pick(digits)).join("") : "";
    const exp = random() < 0.4 ? pick(["e", "E"]) + pick(["", "+", "-"]) + randomInt(0, 400) : "";
    out.push(sign + int + frac + exp);
  }
  for (let i = 0; i < 300; i++) {
    const prefix = pick(["0x", "0X", "0b", "0o", "-0x", "+0b"]);
    const radixDigits = prefix.includes("x") || prefix.includes("X") ? "0123456789abcdefABCDEF" : prefix.includes("b") ? "01" : "01234567";
    out.push(prefix + Array.from({ length: randomInt(0, 30) }, () => pick([...radixDigits])).join(""));
  }
  return [...new Set(out)];
}

const num = (value: number): Descriptor => ({ t: "number", bits: toBits(value) });
const str = (v: string): Descriptor => ({ t: "string", v });
const bool = (v: boolean): Descriptor => ({ t: "bool", v });
const nul: Descriptor = { t: "null" };
const arr = (...items: Descriptor[]): Descriptor => ({ t: "array", items });
const obj = (fields: Record<string, Descriptor> = {}): Descriptor => ({ t: "object", fields });

/** The matrix operands: every JS type, with the awkward members of each. */
const valueOperands: Descriptor[] = [
  nul, bool(true), bool(false),
  str(""), str("0"), str("1"), str("-1"), str("abc"), str(" "), str(" 1 "), str("1e3"), str("0x10"), str("true"), str("false"),
  str("null"), str("undefined"), str("NaN"), str("Infinity"), str("[object Object]"), str("1,2"), str("\n"), str("1\u{00a0}"), str("0b1"),
  num(0), num(-0), num(1), num(-1), num(1.5), num(NaN), num(Infinity), num(-Infinity), num(1000), num(2 ** 53), num(0.1 + 0.2), num(0.3),
  arr(), arr(num(0)), arr(num(1)), arr(arr()), arr(arr(num(1))), arr(num(1), num(2)), arr(nul), arr(arr(nul)), arr(nul, nul),
  arr(str("a")), arr(str("1")), arr(str("")), arr(bool(true)), arr(bool(false)), arr(str("1,2")), arr(obj()), arr(arr(), arr()),
  arr(nul, num(1)), arr(num(1), nul), arr(str(" 1 ")), arr(num(NaN)), arr(num(-0)),
  obj(), obj({ a: num(1) }), obj(), obj({ valueOf: num(1) }),
];

// ---------------------------------------------------------------- collation

const collationStrings: string[] = [
  "", " ", "  ", "\t", "\n", "\u{00a0}", "\u{2003}", "a", "A", "b", "B", "z", "Z", "ab", "Ab", "aB", "AB", "abc", "abd", "a b", "a-b", "a_b", "a.b",
  "ab ", " ab", "a1", "a2", "a10", "a01", "A1", "1", "2", "9", "10", "11", "100", "0", "00", "007", "-1", "1-", "+1",
  ..."!\"#$%&'()*+,-./:;<=>?@[\\]^_`{|}~".split(""),
  "á", "à", "â", "ä", "ã", "å", "æ", "Æ", "ae", "AE", "ç", "c", "č", "d", "ð", "Ð",
  "e", "é", "É", "e\u{0301}", "E\u{0301}", "è", "ê", "ë", "f", "ß", "ss", "SS", "ẞ", "i", "I", "ı", "İ",
  "í", "n", "ñ", "Ñ", "n\u{0303}", "o", "ó", "ö", "ø", "Ø", "œ", "oe", "u", "ü", "ú", "y", "ý", "ÿ",
  "résumé", "resume", "Resume", "RESUME", "résume", "resumé", "cote", "coté", "côte", "côté", "coôte",
  "ﬁ", "fi", "ﬃ", "ffi", "½", "1/2", "²", "2", "①", "Ⅳ", "IV", "Ａ", "ａ", "１", "Ω", "Ω", "ω", "α", "Α", "ά", "ς", "σ",
  "Ж", "ж", "б", "А", "а", "ё", "е", "א", "ב", "ا", "ب", "َا", "ก", "ข", "अ", "क",
  "あ", "ア", "ぁ", "ー", "中", "日", "本", "一", "二", "ㄱ", "가", "한",
  "😀", "😁", "a😀", "😀a", "𝐀", "𝐚", "𐀀",
  "©", "™", "€", "$", "£", "¥", "∑", "∞", "→", "•", "…", "–", "—", "‘", "’", "\"",
  "\u{00ad}", "a\u{00ad}b", "\u{200b}", "a\u{200b}b", "\u{200d}", "\u0000", "a\u0000b", "\u0001", "\u{feff}", "\u{fffd}", "\u{0301}", "a\u{0301}", "\u{0301}a",
  "hello", "Hello", "HELLO", "hello world", "hello-world", "hello_world", "helloWorld", "Hello World", "hello  world", "hello, world",
  "apple", "Apple", "APPLE", "banana", "cherry", "äpfel", "zebra", "Ångström", "angstrom", "Zoë", "Zoe", "zoë",
  "The quick brown fox", "the quick brown fox", "x".repeat(100), "x".repeat(100) + "y", "x".repeat(99) + "y",
];

// ---------------------------------------------------------------- engine probing

interface Probe {
  runtime: string;
  icu: string;
  locale: string;
  numberToString: string[];
  stringToNumber: string[];
  parseFloat: string[];
  mathRound: string[];
  values: { string: string[]; number: string[]; looseEquals: string[] };
  collation: { matrix: string[]; sorted: string[] };
}

const numberOps = numberOperands();
const roundOps = roundOperands();
const stringOps = stringOperands();

const request = {
  numberToString: numberOps.map(toBits),
  stringToNumber: stringOps,
  parseFloat: stringOps,
  mathRound: roundOps.map(toBits),
  values: valueOperands,
  collation: collationStrings,
};

function runProbe(command: string): Probe | null {
  const result = Bun.spawnSync([command, join(here, "engine-probe.mjs")], {
    stdin: new TextEncoder().encode(JSON.stringify(request)),
    stdout: "pipe",
    stderr: "pipe",
  });
  if (!result.success) {
    console.error(`${command}: ${result.stderr.toString().slice(0, 400)}`);
    return null;
  }
  return JSON.parse(result.stdout.toString());
}

const bun = runProbe("bun")!;
const node = runProbe("node");

// The spec names Node as the reference engine, so node's answers are the expectations.
// bun is only compared against it. Without a node binary the bun answers are used.
const expected = node ?? bun;

// ---------------------------------------------------------------- engine differences

const differences: Record<string, unknown[]> = {};

function compareLists<T>(name: string, bunValues: T[], nodeValues: T[], describe: (i: number) => unknown) {
  const diffs = bunValues.flatMap((b, i) =>
    JSON.stringify(b) === JSON.stringify(nodeValues[i]) ? [] : [{ input: describe(i), bun: b, node: nodeValues[i] }],
  );
  if (diffs.length > 0) differences[name] = diffs;
}

if (node) {
  compareLists("numberToString", bun.numberToString, node.numberToString, (i) => request.numberToString[i]);
  compareLists("stringToNumber", bun.stringToNumber, node.stringToNumber, (i) => stringOps[i]);
  compareLists("parseFloat", bun.parseFloat, node.parseFloat, (i) => stringOps[i]);
  compareLists("mathRound", bun.mathRound, node.mathRound, (i) => request.mathRound[i]);
  compareLists("valueString", bun.values.string, node.values.string, (i) => valueOperands[i]);
  compareLists("valueNumber", bun.values.number, node.values.number, (i) => valueOperands[i]);
  compareLists("looseEquals", bun.values.looseEquals, node.values.looseEquals, (i) => i);
  const collationDiffs: unknown[] = [];
  bun.collation.matrix.forEach((row, i) =>
    [...row].forEach((sign, j) => {
      const other = node.collation.matrix[i][j];
      if (sign !== other) collationDiffs.push({ left: collationStrings[i], right: collationStrings[j], bun: sign, node: other });
    }),
  );
  if (collationDiffs.length > 0) differences.collation = collationDiffs;
}

// ---------------------------------------------------------------- write

function write(dir: string, name: string, data: unknown) {
  mkdirSync(dir, { recursive: true });
  Bun.write(join(dir, name), JSON.stringify(data) + "\n");
}

write(ecmaData, "number-to-string.json", {
  cases: numberOps.map((n, i) => ({ bits: toBits(n), expected: expected.numberToString[i] })),
});
write(ecmaData, "string-to-number.json", {
  cases: stringOps.map((input, i) => ({ input, bits: expected.stringToNumber[i] })),
});
write(ecmaData, "parse-float.json", {
  cases: stringOps.map((input, i) => ({ input, bits: expected.parseFloat[i] })),
});
write(ecmaData, "math-round.json", {
  cases: roundOps.map((n, i) => ({ bits: toBits(n), expected: expected.mathRound[i] })),
});
write(ecmaData, "values.json", {
  values: valueOperands,
  toString: expected.values.string,
  toNumber: expected.values.number,
  looseEquals: expected.values.looseEquals,
});
write(ecmaData, "locale-compare.json", {
  runtime: expected.runtime,
  icu: expected.icu,
  locale: expected.locale,
  strings: collationStrings,
  matrix: expected.collation.matrix,
  sorted: expected.collation.sorted,
});

// ---------------------------------------------------------------- report

write(ecmaData, "engine-differences.json", { bun: bun.runtime, node: node?.runtime ?? null, differences });

console.log(
  `bun ${bun.runtime} icu ${bun.icu} locale ${bun.locale}; node ${node ? `${node.runtime} icu ${node.icu}` : "not available"}`,
);
for (const [name, diffs] of Object.entries(differences)) console.log(`  bun/node differ: ${name} (${diffs.length})`);
if (Object.keys(differences).length === 0) console.log("  bun and node agree on every case");
