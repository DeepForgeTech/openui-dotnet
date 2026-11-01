// The real pieces the checks run against: the chat API with its fake provider, Vite and headless
// Chrome, each on a free port and stopped when the script exits. `dotnet` and bun come from PATH;
// CHROME names the browser if it isn't one of the usual ones.
import { mkdtempSync, readFileSync, rmSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

const reactChat = join(import.meta.dir, "..");
const chatApi = join(reactChat, "..", "OpenUI.Lang.Samples.ChatApi");
const scratch = mkdtempSync(join(tmpdir(), "openui-check-"));
const children: { kill(): void }[] = [];

process.on("exit", () => {
  for (const child of children) child.kill();
  rmSync(scratch, { recursive: true, force: true });
});
process.on("SIGINT", () => process.exit(130));
process.on("SIGTERM", () => process.exit(143));

export const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms));

/** Polls until the probe gives something truthy. A loaded machine can take a minute to start Chrome. */
export async function waitFor<T>(label: string, probe: () => Promise<T> | T, timeoutMs = 180_000): Promise<NonNullable<T>> {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    const value = await Promise.resolve().then(probe).catch(() => null);
    if (value) return value;
    await sleep(250);
  }
  throw new Error(`Timed out waiting for ${label}`);
}

function freePort(): number {
  const listener = Bun.listen({ hostname: "127.0.0.1", port: 0, socket: { data() {} } });
  const { port } = listener;
  listener.stop(true);
  return port;
}

/** What FakeReply.Text holds, dedented from its raw string literal. */
export function cannedReply(): string {
  const source = readFileSync(join(chatApi, "FakeReply.cs"), "utf8");
  const literal = source.split('Text = """\n')[1].split('\n        """;')[0];
  return literal.split("\n").map((line) => line.replace(/^ {8}/, "")).join("\n");
}

/** Builds and starts the API in Development. Whatever setReply last wrote is its next reply. */
export async function startApi() {
  const build = Bun.spawnSync(["dotnet", "build", "--nologo", "-v", "q", chatApi], { stdout: "inherit", stderr: "inherit" });
  if (!build.success) throw new Error("The chat API did not build.");

  const port = freePort();
  const replyFile = join(scratch, "reply.oui");
  const setReply = (program: string) => writeFileSync(replyFile, program);
  setReply(cannedReply());
  children.push(Bun.spawn(["dotnet", "bin/Debug/net10.0/OpenUI.Lang.Samples.ChatApi.dll"], {
    cwd: chatApi,
    stdout: "ignore",
    stderr: "inherit",
    env: {
      ...process.env,
      ASPNETCORE_ENVIRONMENT: "Development",
      OPENUI_LLM_PROVIDER: "fake",
      OPENUI_API_PORT: String(port),
      OPENUI_FAKE_REPLY_FILE: replyFile,
    },
  }));

  const base = `http://localhost:${port}`;
  await waitFor("the chat API", async () => (await fetch(`${base}/api/locations`)).ok);
  return { base, port, setReply };
}

/** Starts Vite in front of the API, the way `bun run dev` does. */
export async function startVite(apiPort: number): Promise<string> {
  const port = freePort();
  children.push(Bun.spawn(["bun", "node_modules/vite/bin/vite.js", "--port", String(port), "--strictPort"], {
    cwd: reactChat,
    stdout: "ignore",
    stderr: "inherit",
    env: { ...process.env, OPENUI_API_PORT: String(apiPort) },
  }));
  const base = `http://localhost:${port}`;
  await waitFor("Vite", async () => (await fetch(base)).ok);
  return base;
}

/** One tab of headless Chrome, driven over the DevTools protocol with real mouse and keyboard input. */
export class Browser {
  private ws!: WebSocket;
  private nextId = 0;
  private readonly pending = new Map<number, (message: any) => void>();
  private pageId = "";
  readonly exceptions: string[] = [];

  private constructor(private readonly debugBase: string) {}

  static async start(): Promise<Browser> {
    const executable = process.env.CHROME ?? ["google-chrome-stable", "google-chrome", "chromium", "brave"].find((name) => Bun.which(name));
    if (!executable) throw new Error("No Chrome found. Set CHROME to its path.");

    const port = freePort();
    children.push(Bun.spawn([
      executable, "--headless=new", "--no-sandbox", "--disable-gpu", "--window-size=1500,950",
      `--remote-debugging-port=${port}`, `--user-data-dir=${join(scratch, "chrome")}`, "about:blank",
    ], { stdout: "ignore", stderr: "ignore" }));

    const browser = new Browser(`http://127.0.0.1:${port}`);
    const page = await waitFor("Chrome", async () => (await browser.pages()).find((target) => target.url === "about:blank"));
    browser.pageId = page.id;
    browser.ws = new WebSocket(page.webSocketDebuggerUrl);
    await new Promise((resolve) => (browser.ws.onopen = resolve));
    browser.ws.onmessage = (event) => {
      const message = JSON.parse(String(event.data));
      if (message.id) browser.pending.get(message.id)?.(message);
      if (message.method === "Runtime.exceptionThrown") browser.exceptions.push(message.params.exceptionDetails.exception?.description ?? message.params.exceptionDetails.text);
    };
    await browser.send("Page.enable");
    await browser.send("Runtime.enable");
    return browser;
  }

  async pages(): Promise<{ id: string; type: string; url: string; webSocketDebuggerUrl: string }[]> {
    return (await (await fetch(`${this.debugBase}/json/list`)).json()).filter((target: { type: string }) => target.type === "page");
  }

  /** Closes any window the page opened. A tab left in the background stops drawing and ignores the mouse. */
  async closeOtherPages() {
    for (const { id } of await this.pages()) {
      if (id !== this.pageId) await fetch(`${this.debugBase}/json/close/${id}`);
    }
    await this.send("Page.bringToFront");
  }

  send(method: string, params: object = {}): Promise<any> {
    return new Promise((resolve) => {
      const id = ++this.nextId;
      this.pending.set(id, resolve);
      this.ws.send(JSON.stringify({ id, method, params }));
    });
  }

  async evaluate(expression: string): Promise<any> {
    return (await this.send("Runtime.evaluate", { expression, returnByValue: true, awaitPromise: true })).result?.result?.value;
  }

  navigate(url: string) {
    return this.send("Page.navigate", { url });
  }

  /** A real mouse click in the middle of what the expression finds. False if it finds nothing. */
  async click(element: string): Promise<boolean> {
    const point = await this.evaluate(`(() => {
      const element = (${element});
      if (!element) return null;
      element.scrollIntoView({ block: "center" });
      const box = element.getBoundingClientRect();
      return { x: box.x + box.width / 2, y: box.y + box.height / 2 };
    })()`);
    if (!point) return false;
    await this.send("Input.dispatchMouseEvent", { type: "mouseMoved", ...point });
    await this.send("Input.dispatchMouseEvent", { type: "mousePressed", ...point, button: "left", clickCount: 1 });
    await this.send("Input.dispatchMouseEvent", { type: "mouseReleased", ...point, button: "left", clickCount: 1 });
    return true;
  }

  async typeAndSubmit(text: string) {
    await this.send("Input.insertText", { text });
    const enter = { key: "Enter", code: "Enter", windowsVirtualKeyCode: 13 };
    await this.send("Input.dispatchKeyEvent", { type: "keyDown", ...enter, text: "\r" });
    await this.send("Input.dispatchKeyEvent", { type: "keyUp", ...enter });
  }

  async bodyText(): Promise<string> {
    return (await this.evaluate("document.body.innerText")) ?? "";
  }
}
