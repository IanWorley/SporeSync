// @vitest-environment jsdom
import "@testing-library/jest-dom/vitest";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import {
  createMemoryHistory,
  createRootRoute,
  createRoute,
  createRouter,
  RouterProvider,
} from "@tanstack/react-router";
import {
  cleanup,
  fireEvent,
  render,
  screen,
  waitFor,
} from "@testing-library/react";
import { afterEach, describe, expect, it, vi } from "vitest";
import { queryKeys } from "../api/queryKeys";
import type { AuthSession } from "../api/types";
import { LoginPage } from "./LoginPage";

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

async function renderLoginPage() {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false } },
  });
  const rootRoute = createRootRoute();
  const loginRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/",
    component: LoginPage,
  });
  const dashboardRoute = createRoute({
    getParentRoute: () => rootRoute,
    path: "/dashboard",
    component: () => <div>Dashboard stub</div>,
  });
  const router = createRouter({
    routeTree: rootRoute.addChildren([loginRoute, dashboardRoute]),
    history: createMemoryHistory({ initialEntries: ["/"] }),
    context: { queryClient },
  });

  render(
    <QueryClientProvider client={queryClient}>
      <RouterProvider router={router} />
    </QueryClientProvider>,
  );
  await screen.findByRole("button", { name: /sign in/i });

  return queryClient;
}

function submitCredentials(username: string, password: string) {
  fireEvent.change(screen.getByLabelText(/username/i), {
    target: { value: username },
  });
  fireEvent.change(screen.getByLabelText(/password/i), {
    target: { value: password },
  });
  fireEvent.click(screen.getByRole("button", { name: /sign in/i }));
}

afterEach(() => {
  cleanup();
  vi.unstubAllGlobals();
});

describe("LoginPage", () => {
  it("renders the credential form", async () => {
    await renderLoginPage();

    expect(screen.getByLabelText(/username/i)).toBeInTheDocument();
    expect(screen.getByLabelText(/password/i)).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: /sign in/i }),
    ).toBeInTheDocument();
  });

  it("stores the session and navigates to the dashboard on success", async () => {
    const session: AuthSession = {
      authRequired: true,
      authenticated: true,
      username: "admin",
    };
    vi.stubGlobal("fetch", vi.fn().mockResolvedValue(jsonResponse(session)));

    const queryClient = await renderLoginPage();
    submitCredentials("admin", "s3cret");

    await waitFor(() =>
      expect(screen.getByText("Dashboard stub")).toBeInTheDocument(),
    );
    expect(queryClient.getQueryData(queryKeys.session)).toEqual(session);
  });

  it("shows an error message when login fails", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse({ message: "Invalid username or password." }, 401),
        ),
    );

    await renderLoginPage();
    submitCredentials("admin", "wrong");

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Invalid username or password.");
  });
});
