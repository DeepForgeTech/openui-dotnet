import "@openuidev/react-ui/styles/index.css";

import {
  AgentInterface,
  fetchLLM,
  openAIMessageFormat,
  openAIReadableStreamAdapter,
} from "@openuidev/react-ui";
import { openuiLibrary } from "@openuidev/react-ui/genui-lib";
import { useEffect, useState } from "react";
import DataSample from "./DataSample";
import { localStorageChat } from "./lib/local-storage";

const { llm, storage } = localStorageChat(
  fetchLLM({
    url: "/api/chat",
    streamAdapter: openAIReadableStreamAdapter(),
    messageFormat: openAIMessageFormat,
  }),
  "openui-dotnet",
);

const TABS = [
  { id: "chat", label: "Chat", hash: "#/" },
  { id: "data", label: "Query and Mutation", hash: "#/data" },
];

const tabFromHash = () => (window.location.hash === "#/data" ? "data" : "chat");

export default function App() {
  const [tab, setTab] = useState(tabFromHash);
  useEffect(() => {
    const onHashChange = () => setTab(tabFromHash());
    window.addEventListener("hashchange", onHashChange);
    return () => window.removeEventListener("hashchange", onHashChange);
  }, []);

  const [fakeNotice, setFakeNotice] = useState(null);
  useEffect(() => {
    fetch("/api/model")
      .then((response) => response.json())
      .then((model) => setFakeNotice(model.notice))
      .catch(() => {});
  }, []);

  return (
    <div style={{ display: "flex", flexDirection: "column", height: "100vh", width: "100vw", overflow: "hidden" }}>
      <nav style={{ display: "flex", gap: 8, padding: "8px 16px", borderBottom: "1px solid #ddd" }}>
        {TABS.map(({ id, label, hash }) => (
          <a
            key={id}
            href={hash}
            aria-current={tab === id ? "page" : undefined}
            style={{ padding: "4px 12px", borderRadius: 6, fontWeight: tab === id ? 600 : 400, background: tab === id ? "#eee" : "none" }}
          >
            {label}
          </a>
        ))}
      </nav>
      {tab === "chat" && fakeNotice && (
        <div role="status" style={{ padding: "8px 16px", background: "#fff6d6", color: "#5c4600", borderBottom: "1px solid #d9a400", textAlign: "center" }}>
          {fakeNotice}
        </div>
      )}
      <div className={tab === "chat" ? "chat-slot" : undefined} style={{ flex: 1, minHeight: 0 }}>
        {tab === "chat" ? (
          // The ASP.NET Core API builds the system prompt from src/generated/spec.json.
          <AgentInterface llm={llm} storage={storage} componentLibrary={openuiLibrary} agentName="OpenUI Chat" />
        ) : (
          <DataSample />
        )}
      </div>
    </div>
  );
}
