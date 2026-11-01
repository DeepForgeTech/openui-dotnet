// The whole sample in headless Chrome: chat, the OpenUrl guard, and the Query and Mutation tab.
import { Browser, cannedReply, sleep, startApi, startVite, waitFor } from "./stack";

const api = await startApi();
const app = await startVite(api.port);
const browser = await Browser.start();
const say = (label: string) => console.log(`ok   ${label}`);
const fail = (label: string): never => {
  throw new Error(label);
};
const button = (text: string) => `[...document.querySelectorAll("button")].find((b) => b.innerText.trim() === ${JSON.stringify(text)})`;

async function ask(program: string) {
  api.setReply(program);
  await waitFor("the composer", () => browser.click(`document.querySelector("textarea:not([disabled])")`));
  await browser.typeAndSubmit("show me something");
  await waitFor("the reply to finish", async () => (await (await fetch(`${api.base}/api/debug/last`)).json()).reply === program);
}

// Chat: a message goes out and the card comes back rendered.
await browser.navigate(`${app}/#/`);
await waitFor("the fake model's notice", async () => (await browser.bodyText()).includes("nothing you type here is read"));
say("chat warns that nothing is read");
await ask(cannedReply());
await waitFor("the rendered card", async () => {
  const text = await browser.bodyText();
  return text.includes("Weekly summary") && text.includes("Streaming works") && text.includes("did not read your message");
});
say("chat renders the reply");

// A program must not be able to run script in this page through OpenUrl.
const title = await browser.evaluate("document.title");
const pagesBefore = (await browser.pages()).length;
await ask(`root = Card([btn])
btn = Button("Open report", @OpenUrl("javascript:opener.document.title='PWNED '+opener.location.origin+' storage='+opener.localStorage.length;void 0"))`);
await waitFor("the javascript: button", () => browser.evaluate(`!!${button("Open report")}`));
await browser.click(button("Open report"));
await sleep(3000);
const titleAfter = await browser.evaluate("document.title");
if (titleAfter !== title) fail(`A javascript: OpenUrl ran in the page: the title became "${titleAfter}"`);
if ((await browser.pages()).length !== pagesBefore) fail("A javascript: OpenUrl opened a window");
say("a javascript: OpenUrl does nothing");

// An ordinary link still opens. Nothing listens on this port, and the new window is all that counts.
const https = "https://localhost:1/report";
await ask(`root = Card([btn])\nbtn = Button("Open site", @OpenUrl("${https}"))`);
await waitFor("the https: button", () => browser.evaluate(`!!${button("Open site")}`));
await browser.click(button("Open site"));
await waitFor("the https: window", async () => (await browser.pages()).some((page) => page.url === https));
say("an https: OpenUrl opens a window");
await browser.closeOtherPages();

// Query and Mutation: real data, both refusals, then a job that lands.
await browser.navigate(`${app}/#/data`);
const main = () => browser.evaluate(`document.querySelector("main")?.innerText ?? ""`);
await waitFor("the planner's data", async () => {
  const text = await main();
  return text.includes("job-2") && text.includes("Forecast for north-field");
});
say("the queries show the API's data");

const schedule = button("Schedule spray");
await browser.click(schedule);
await waitFor("the refusal of an empty day", async () => (await main()).includes("date: Required"));
say("an empty day is refused");

async function pickDay(date: string) {
  await waitFor("the day list", () => browser.click(`[...document.querySelectorAll("main button")].find((b) => b.innerText.includes("Day") || /^\\d{4}-\\d{2}-\\d{2}:/.test(b.innerText.trim()))`));
  await waitFor(`the day ${date}`, () => browser.click(`[...document.querySelectorAll("[role=option]")].find((o) => o.innerText.startsWith(${JSON.stringify(date)}))`));
  await waitFor(`the day ${date} to be chosen`, () => browser.evaluate(`[...document.querySelectorAll("main button")].some((b) => b.innerText.trim().startsWith(${JSON.stringify(date)}))`));
  // The closing list holds pointer-events off the page for a moment, and a click in that time lands nowhere.
  await waitFor("the day list to close", () => browser.evaluate(`document.body.style.pointerEvents !== "none"`));
}

const jobs: { id: string; location: string; date: string }[] = (await (await fetch(`${api.base}/api/spray-jobs`)).json()).jobs;
const taken = jobs.find((job) => job.location === "north-field") ?? fail("No seed job on north-field");
await pickDay(taken.date);
await browser.click(schedule);
await waitFor("the refusal of a taken day", async () => (await main()).includes(`already has a job on ${taken.date}`));
say("a taken day is refused");

const forecast: { date: string; sprayable: boolean }[] = (await (await fetch(`${api.base}/api/forecast?location=north-field&days=7`)).json()).days;
const free = forecast.find((day) => day.sprayable && day.date !== taken.date) ?? fail("No free day on north-field");
await pickDay(free.date);
await browser.click(schedule);
await waitFor("the scheduled job", async () => {
  const text = await main();
  return text.includes(`job-3 on ${free.date}`) && text.includes("3 scheduled");
});
say("a free day is scheduled and the job list refetches");

const programErrors = await browser.evaluate(`document.querySelector("#program-errors")?.innerText`);
if (programErrors !== "none") fail(`The program reported errors: ${programErrors}`);
if (browser.exceptions.length > 0) fail(`The page threw: ${browser.exceptions.join(" | ")}`);
say("no program errors and no page exceptions");

console.log("BROWSER CHECK OK");
process.exit(0);
