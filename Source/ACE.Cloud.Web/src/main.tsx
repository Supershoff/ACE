import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { BrowserRouter } from "react-router-dom";
import { App } from "./App";
import { SessionProvider } from "./session/SessionContext";
import "./design-system/tokens.css";

const container = document.getElementById("root");
if (!container) {
  throw new Error("Missing #root element.");
}

createRoot(container).render(
  <StrictMode>
    <SessionProvider>
      <BrowserRouter>
        <App />
      </BrowserRouter>
    </SessionProvider>
  </StrictMode>,
);
