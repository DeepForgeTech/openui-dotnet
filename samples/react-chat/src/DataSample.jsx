import { Renderer } from "@openuidev/react-lang";
import { ThemeProvider } from "@openuidev/react-ui";
import { openuiLibrary } from "@openuidev/react-ui/genui-lib";
import { useMemo, useState } from "react";
import program from "./samples/spray-planner.oui?raw";
import { createWeatherTools } from "./weather-tools";

const MAX_LOG = 12;

/**
 * Renders a fixed OpenUI Lang program that uses Query and Mutation, with no model involved. The
 * program's tools are the sample API's weather endpoints (see weather-tools.js).
 */
export default function DataSample() {
  const [calls, setCalls] = useState([]);
  const [errors, setErrors] = useState([]);
  const tools = useMemo(
    () => createWeatherTools((call) => setCalls((previous) => [call, ...previous].slice(0, MAX_LOG))),
    [],
  );

  return (
    <div style={{ display: "flex", height: "100%", minHeight: 0 }}>
      <main style={{ flex: 1, overflow: "auto", padding: 24 }}>
        <ThemeProvider>
          <Renderer
            response={program}
            library={openuiLibrary}
            isStreaming={false}
            toolProvider={tools}
            onError={setErrors}
          />
        </ThemeProvider>
      </main>
      <aside style={{ width: 380, borderLeft: "1px solid #ddd", overflow: "auto", padding: 16, fontSize: 13 }}>
        <h3 style={{ fontWeight: 600 }}>Tool calls</h3>
        <p style={{ color: "#666" }}>What the program's Query and Mutation sent to the API, newest first.</p>
        <ol id="tool-calls" style={{ listStyle: "none", padding: 0 }}>
          {calls.map((call, index) => (
            <li key={`${call.at.getTime()}-${index}`} style={{ margin: "6px 0" }}>
              <code>
                {call.ok ? "ok" : "failed"} {call.method} {call.path}
              </code>
              <div style={{ color: "#444" }}>
                {call.tool}({JSON.stringify(call.args)})
              </div>
              {call.error && <div style={{ color: "#b42318" }}>{call.error}</div>}
            </li>
          ))}
        </ol>
        <h3 style={{ fontWeight: 600, marginTop: 16 }}>Program errors</h3>
        <pre id="program-errors">{errors.length === 0 ? "none" : JSON.stringify(errors, null, 2)}</pre>
        <details style={{ marginTop: 16 }}>
          <summary>Program</summary>
          <pre style={{ whiteSpace: "pre-wrap" }}>{program}</pre>
        </details>
      </aside>
    </div>
  );
}
