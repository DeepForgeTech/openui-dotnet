# Samples

The Blazor samples use `OpenUI.Lang.Blazor` and `OpenUI.Lang.Blazor.Mud`, which are not packaged yet.

## Widget playground (Blazor)

An experiment: dashboard widgets written in OpenUI Lang instead of C#, and whole pages written in OpenUI Lang too. A page is a grid of widgets plus page variables that connect them. Widgets never talk to each other: they read the variables they're bound to and write changes back.

```sh
dotnet run --project samples/OpenUI.Lang.Samples.WidgetPlayground
```

Open http://localhost:5290. It listens on localhost only; `OPENUI_PLAYGROUND_PORT` changes the port. The data comes from a fake tool that makes the same synthetic readings every time. The prompt box answers with canned programs by default, so the whole loop runs without a model. To ask a real one, set `OPENUI_LLM_PROVIDER` exactly as for the chat API below. Set `OPENUI_PLAYGROUND_TRANSCRIPT` to a file path to append every finished prompt there as one JSON line (the request, the reply, the resulting program and its errors).

What to try:

- **Prompt a widget.** Describe a widget and press **Generate**. With a program already in the editor the page offers **Refine** and **Start over** instead. The system prompt comes from `SystemPrompt.Generate` on the widget library, with the tools and the page variables (`$location`, `$metric`, `$days` and their values). The reply streams into the editor and into an `OpenUISession`, so the widget renders while it's being written; **Stop** cancels and puts the editor back. Then keep going: type a change ("make it a bar chart", "add humidity") and press **Refine**, as many times as you like. Each refine sends the editor's program as it is now, hand edits included, with edit mode on (spec §7): the model answers with only the changed statements, which render as they arrive and are merged with `ProgramMerger.Merge` when the reply ends. The list under the prompt shows every request and what came of it, and **Undo** steps back to the program from before the last applied prompt.
- **Widget** (`/`). The editor holds a custom widget program, and the middle pane renders it as you type. Typing counts as a stream: the session renders the partial program, and queries wait until you pause. **Stream it** replays the program a few characters at a time. Change `$location`, `$metric` or `$days` on the right, and the widget re-runs its `getSeries` query. Pick a location in the widget's own Select, and the variable on the right follows. Errors appear under the editor.
- **Page** (`/page`). The page program mixes catalog widgets (`LocationPicker`, `DateRangePicker`, `MapView`, `MetricTile`, `TimeSeriesChart`) with a `CustomWidget` whose program sits inline in a single-quoted string. Pick a location in the picker or on the map, and every widget bound to `$location` updates. The right-hand panel shows the `PageDocument` JSON the program translates to. That JSON is the stored form, and the page renders from it, not from the program. The same Generate and Refine loop works here, with the page library's prompt.
- **Prompt** (`/prompt`). This is the system prompt each library teaches a model, and the edit-mode version Refine uses. Both are built with `SystemPrompt.Generate` from the same LibrarySpec the parser reads.

How it fits together:

- `Catalog/WidgetCatalog.cs` defines every component once: props, types, read-only or read-write, descriptions, and the Blazor component that draws it. `Catalog/PlaygroundLibraries.cs` turns that into a `LibraryDefinition` from `OpenUI.Lang.Blazor`, which feeds both the parser and `SystemPrompt.Generate` from one LibrarySpec.
- `OpenUI.Lang.Blazor` is the renderer: `OpenUIView` draws a session's snapshot and re-renders on `Invalidated`, and each `OuiComponent` is drawn by `OpenUINode`, which looks its type up in a `ComponentRegistry` and opens that Blazor component with `OpenComponent`. `Widgets/ProgramSession.cs` holds the session behind each editor: it feeds it text and records host events. Bound props arrive already holding the variable's value, and a write goes back through the prop's binding.
- `Generation/` is the prompt loop. `ProgramDesigner` builds each request, `PromptBar` runs it and keeps the history and undo, and `FakeReplies` holds the canned programs.
- The model providers live in `OpenUI.Lang.Samples.Llm/`, which the chat API uses too.
- The components are plain HTML and CSS placeholders.

## Blazor chat and spray planner (MudBlazor)

`OpenUI.Lang.Samples.BlazorApp` is what a Blazor Server app looks like with `OpenUI.Lang.Blazor` and the `OpenUI.Lang.Blazor.Mud` component set. It works with no model. It needs the chat API running too, because the tools in its programs call that API's weather endpoints over HTTP.

```sh
OPENUI_API_PORT=5381 dotnet run --project samples/OpenUI.Lang.Samples.ChatApi
dotnet run --project samples/OpenUI.Lang.Samples.BlazorApp
```

Open http://localhost:5380. To run both in tmux, from the repository root:

```sh
tmux new-session -d -s openui-blazor -c "$PWD" 'OPENUI_API_PORT=5381 dotnet run --project samples/OpenUI.Lang.Samples.ChatApi'
tmux split-window -h -t openui-blazor -c "$PWD" 'dotnet run --project samples/OpenUI.Lang.Samples.BlazorApp'
tmux attach -t openui-blazor
```

`tmux kill-session -t openui-blazor` stops both. For a real model, set the provider on the second command exactly as for the chat API below. `OPENUI_BLAZOR_PORT` changes the port, `OPENUI_WEATHER_API` says where the API is (`http://localhost:5381` by default), and `OPENUI_BLAZOR_DATA` says where conversations are kept (under your local application data folder by default).

What to try:

- **Chat** (`/`). A reply is a program, drawn with Mud components while it streams. Type in the reply's **Field** box while it's still arriving: the input keeps its focus and what you typed. The stop button ends a reply and keeps what came. Sliders and presets run in the page; **Show the forecast** and **Plan a spray job** go back to the model with the page's values, as prompt.md §6.3 describes. **Plan a spray job** draws a form with validation rules. **Read about drift** asks before it opens the link. Errors in a reply show under it and go back to the model with the next message. Conversations are JSON files on the server, listed on the left, and they come back after a reload or a restart.
- **Spray planner** (`/planner`). A fixed program, `Programs/spray-planner.oui`, with `Query` and `Mutation` over real HTTP. **Schedule spray** posts the job, then fetches the jobs again. Pick a day with a "no" beside it in the forecast and the API rejects the job with a 422, whose sentence shows in the form. The panel on the right lists every call the page made, and the program's current errors.

How it fits together:

- `OpenUIProgram` is all a page needs: give it `Library`, and `Program` with `IsStreaming` (or `Chunks`), and it owns a session and disposes of it with the page. `Tools` and `OnHostEvent` connect it to the app, `OnErrors` tells the page what went wrong, and `PendingContent`, `ErrorContent`, `UnknownContent` and `FailedContent` replace the defaults.
- A host that owns its session uses `OpenUIView` with a `ComponentRegistry` instead, as the playground does.
- A component derives from `OpenUIComponent`, which reads props with `Text`, `Number`, `Items` and `Input`, writes back with `Write`, and runs actions with `RunAsync`. Register it in a `LibraryDefinition`, which gives both the parser's library and the system prompt, as `src/OpenUI.Lang.Blazor.Mud/MudLibrary.cs` does.
- `Weather/WeatherTools.cs` is one `IOpenUIToolProvider` with four tools, each an endpoint of the API. Reads are declared `Read` and the post is `Mutating`, and an error response fails the call with the API's sentence.
- `Chat/` holds the rest of the chat: `ConversationStore` keeps the files, `ChatTurns` builds what the model reads, and `FakeReplies` picks a program from `Programs/` for the fake provider.

## React chat over an ASP.NET Core API

The stock OpenUI React chat (`@openuidev/react-ui` and `@openuidev/react-lang`, unchanged) talking to an ASP.NET Core API built on `OpenUI.Lang.Core`. You type a message in the browser, and the model's OpenUI Lang reply renders as live components while it streams.

- `react-chat/` is the frontend. It's upstream's FastAPI example frontend with the Vite proxy pointed at the .NET API.
- `OpenUI.Lang.Samples.ChatApi/` is the API, and little more than `AddOpenUI` and `MapOpenUIChat` from `OpenUI.Lang.AspNetCore` (see the root README). `POST /api/chat` builds the system prompt with `SystemPrompt.Generate` from the react-ui library spec, streams the model's reply as OpenAI chunk NDJSON (what `openAIReadableStreamAdapter` reads), and hands the finished reply's parse to a hook that logs it and serves it at `/api/debug/last`. The providers below sit behind it through `ChatModelChatClient`, a small `IChatClient` adapter.

Upstream's example uses Thesys cloud mode, where the prompt is built on Thesys's servers. Here the prompt comes from this library. Core's tests check its prompts against ones produced by upstream's generator.

### What you need

- The .NET 10 SDK
- [bun](https://bun.sh)

### 1. Start the API

From the repository root:

```sh
dotnet run --project samples/OpenUI.Lang.Samples.ChatApi
```

It listens on `http://localhost:5280`, on localhost only, and all three web samples keep it that way against other sites: `AllowedHosts` in `appsettings.json` refuses any other `Host` header, so a site can't rebind its name to your machine, and `UseLocalOriginsOnly` refuses a POST from a page on another site. Copy both if you copy a sample. It picks a model provider from `OPENUI_LLM_PROVIDER`:

| Provider | What it does | Settings |
|---|---|---|
| `openai` | Any OpenAI-compatible `/chat/completions` endpoint. | `OPENUI_LLM_BASE_URL` (such as `https://api.openai.com/v1`), `OPENUI_LLM_MODEL`, `OPENUI_LLM_API_KEY` |
| `fake` (default) | Streams a canned reply and reads nothing you type; each app says so by its prompt box and with each reply. No model needed. | `OPENUI_FAKE_REPLY_FILE` (a file holding the reply, read for each one) |

The providers are in `OpenUI.Lang.Samples.Llm/`, shared with the widget playground, and each one is an `IChatClient`. A provider failure reaches the browser as a fixed message, since the package never sends exception text: 429 when the model is rate limited, 504 when it times out and 502 otherwise. The reason is in the API's log.

```sh
# An OpenAI-compatible endpoint
OPENUI_LLM_PROVIDER=openai \
OPENUI_LLM_BASE_URL=https://api.openai.com/v1 \
OPENUI_LLM_MODEL=gpt-4.1-mini \
OPENUI_LLM_API_KEY=sk-... \
dotnet run --project samples/OpenUI.Lang.Samples.ChatApi
```

`OPENUI_API_PORT` changes the port; set it for `bun run dev` too, so the Vite proxy follows. `OPENUI_SPEC_PATH` points at a different library spec.

### 2. Start the frontend

```sh
cd samples/react-chat
bun install
bun run dev
```

Open http://localhost:5173 and ask for something, such as "show me a card with 3 metrics".

`bun run dev` and `bun run build` first run `bun run generate`, which uses upstream's `openui generate` CLI to write `src/generated/spec.json` (the library spec the API reads) and `src/generated/prompt.txt` (the prompt upstream's own generator makes for the same library). Both are committed. The scripts set `DO_NOT_TRACK=1` to turn off the CLI's telemetry, and bun doesn't run `@openuidev/lang-core`'s telemetry postinstall unless you trust it.

### Running both in tmux

```sh
tmux new-session -d -s openui -c "$PWD" 'dotnet run --project samples/OpenUI.Lang.Samples.ChatApi'
tmux split-window -h -t openui -c "$PWD/samples/react-chat" 'bun install && bun run dev'
tmux attach -t openui
```

Run it from the repository root. `tmux kill-session -t openui` stops both.

### Checking the whole sample

Run these from `samples/react-chat` after `bun install`. Each one starts what it needs on free ports with the fake provider and stops it again.

- `bun run contract-check` reads the real API's reply through upstream's own stream adapter, and checks the text, the event order and the server's report of it.
- `bun run browser-check` drives the chat, `@OpenUrl` and the spray planner in headless Chrome with real mouse and keyboard input: a `javascript:` link does nothing and an `https:` one opens. Set `CHROME` if it can't find your browser.

### Query and Mutation: fetching data and acting on it

The React app has a second tab, **Query and Mutation** (`http://localhost:5173/#/data`), that works with no model. It renders a fixed OpenUI Lang program, `react-chat/src/samples/spray-planner.oui`, with `Renderer` from `@openuidev/react-lang` and the react-ui library. The program shows what the language does with data:

- `forecast = Query("getForecast", {location: $location, days: 7}, {days: []}, 30)` fetches a field's forecast when the page loads, again when `$location` changes, and every 30 seconds.
- `jobs = Query("listSprayJobs", {}, {jobs: []}, 15)` lists the spray jobs, refreshed every 15 seconds.
- `schedule = Mutation("scheduleSprayJob", {location: $location, product: $product, date: $date})` runs only from the **Schedule spray** button, whose action is `Action([@Run(schedule), @Run(jobs), @Reset($date)])`: schedule, then fetch the jobs again, then clear the day. A rejected job stops the action, and the program shows `schedule.error` in a callout.

`Renderer` runs the tools through its `toolProvider` prop, a map of async functions keyed by tool name (language.md §6.4). `react-chat/src/weather-tools.js` builds that map, one tool per API endpoint, and the page lists every call it makes beside the program. The chat tab can't run tools, because react-ui's chat doesn't pass a `toolProvider`.

The tools are endpoints of the ChatApi (`Weather/`), over a small in-memory model of fields, forecasts and spray jobs. There is no database, and jobs last until the API restarts.

| Tool | Endpoint | Arguments | Result |
|---|---|---|---|
| `listLocations` | `GET /api/locations` | none | `{ locations: [{ id, name, crop }] }` |
| `getForecast` | `GET /api/forecast` | `location`, `days` (1 to 14, default 7) | `{ location, days: [{ date, highF, lowF, rainChance, windMph, sprayable }] }` |
| `listSprayJobs` | `GET /api/spray-jobs` | `status` (optional, `scheduled`) | `{ jobs: [{ id, location, product, date, status, createdAt }] }` |
| `scheduleSprayJob` | `POST /api/spray-jobs` | `location`, `product`, `date` (`yyyy-MM-dd`) in the JSON body | `201` and the new job |

An error is `{ "error": "...", "fields": { ... } }`, where `error` is one sentence for a person and `fields` names the request members at fault, when it is those. `scheduleSprayJob` answers `400` for a missing or unknown member or a day outside the 14-day forecast, `422` for a day with wind over 15 mph or rain at 60% or more, and `409` when the field already has a job that day.

Try it from a shell (the dates move with today, so read one from the forecast):

```sh
curl -s 'http://localhost:5280/api/forecast?location=north-field&days=7'
curl -s -X POST http://localhost:5280/api/spray-jobs -H 'Content-Type: application/json' \
  -d '{"location":"north-field","product":"Fungicide","date":"2026-11-04"}'
curl -s http://localhost:5280/api/spray-jobs
```

`tests/OpenUI.Lang.Samples.ChatApi.Tests` starts the real app, covers the endpoints, and runs `spray-planner.oui` on Core against them: the queries fill in, the mutation changes what the next fetch returns, and a rejected job reaches the program as an error.

### Checking what Core made of a reply

After each reply the API logs one line with the parse result:

```text
openui_parse root=Card statements=8 incomplete=False errors=[] unresolved=[]
```

In Development, `GET /api/debug/last` returns the same report for the last reply, with each error and the full reply text. Start the API with `ASPNETCORE_ENVIRONMENT=Development` to get it:

```sh
curl -s http://localhost:5280/api/debug/last
```

### Credit

`react-chat/` is adapted from upstream's [FastAPI example frontend](https://github.com/thesysdev/openui/tree/main/examples/app-frameworks/fastapi) by Thesys, used under the MIT License. See [THIRD_PARTY_NOTICES.md](../THIRD_PARTY_NOTICES.md).
