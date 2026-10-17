import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { createBrowserHistory, RouterProvider } from "@tanstack/react-router";
import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { createAppRouter } from "./router";
import "./index.css";

const rootElement = document.querySelector("#root");
if (rootElement === null) {
  throw new Error("index.html is missing the #root element");
}

const queryClient = new QueryClient();
const router = createAppRouter(createBrowserHistory());

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>
  </StrictMode>,
);
