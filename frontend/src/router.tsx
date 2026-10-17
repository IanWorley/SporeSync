import {
  createRootRoute,
  createRoute,
  createRouter,
  Outlet,
  type RouterHistory,
} from "@tanstack/react-router";

const rootRoute = createRootRoute({
  component: () => (
    <main className="min-h-screen bg-white p-6 text-gray-900 dark:bg-gray-950 dark:text-gray-100">
      <Outlet />
    </main>
  ),
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: () => <h1 className="text-2xl font-semibold">SporeSync</h1>,
});

const routeTree = rootRoute.addChildren([indexRoute]);

export function createAppRouter(history: RouterHistory) {
  return createRouter({ routeTree, history });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: ReturnType<typeof createAppRouter>;
  }
}
