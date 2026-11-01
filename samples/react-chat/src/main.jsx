import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./index.css";
import App from "./App.jsx";

// react-ui's chat runs a program's @OpenUrl through a bare window.open, so a javascript: URL from
// the model would run in this page's origin, next to the chat history. Only ordinary links get out.
const openWindow = window.open.bind(window);
window.open = (url, ...rest) =>
  URL.canParse(url, location.href) && ["http:", "https:"].includes(new URL(url, location.href).protocol) ? openWindow(url, ...rest) : null;

createRoot(document.getElementById("root")).render(
  <StrictMode>
    <App />
  </StrictMode>,
);
