// Feeds the real API's HTTP response through upstream's stream adapter, with the request body built
// the way fetchLLM and openAIMessageFormat build it. It's the one check that runs upstream's own parser.
import { EventType, getResponseErrorMessage, openAIMessageFormat, openAIReadableStreamAdapter } from "@openuidev/react-headless";
import { cannedReply, startApi } from "./stack";

const { base } = await startApi();
const post = (body: object) =>
  fetch(`${base}/api/chat`, { method: "POST", headers: { "Content-Type": "application/json" }, body: JSON.stringify(body) });

const failures: string[] = [];
const check = (label: string, ok: boolean) => {
  console.log(`${ok ? "ok  " : "FAIL"} ${label}`);
  if (!ok) failures.push(label);
};

const messages = [
  { id: "1", role: "user", content: "hello" },
  { id: "2", role: "assistant", content: "hi" },
  { id: "3", role: "user", content: [{ type: "text", text: "show me a card" }] },
];
const response = await post({ tools: [], context: [], threadId: "t", runId: "r", messages: openAIMessageFormat.toApi(messages) });
check("the reply is 200 NDJSON", response.status === 200 && response.headers.get("content-type") === "application/x-ndjson");

const events: { type: string; delta?: string }[] = [];
for await (const event of openAIReadableStreamAdapter().parse(response)) events.push(event as { type: string; delta?: string });
const types = events.map((event) => event.type);
const text = events.filter((event) => event.type === EventType.TEXT_MESSAGE_CONTENT).map((event) => event.delta).join("");
check("the adapter's text is the whole reply", text === cannedReply());
check("the message starts and ends once", types[0] === EventType.TEXT_MESSAGE_START && types.at(-1) === EventType.TEXT_MESSAGE_END
  && types.filter((type) => type === EventType.TEXT_MESSAGE_START).length === 1 && types.filter((type) => type === EventType.TEXT_MESSAGE_END).length === 1);

const last = await (await fetch(`${base}/api/debug/last`)).json();
check("the server read the same reply as a valid Card", last.reply === text && last.root === "Card" && last.errors.length === 0);

const rejected = await post({ messages: [] });
check("a rejected request has a message the chat can show", rejected.status === 400 && (await getResponseErrorMessage(rejected)).includes("messages"));

console.log(failures.length === 0 ? "CONTRACT OK" : "CONTRACT FAILED");
process.exit(failures.length === 0 ? 0 : 1);
