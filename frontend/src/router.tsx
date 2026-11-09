import {
  createRootRoute,
  createRoute,
  createRouter,
  Link,
  Outlet,
  type RouterHistory,
} from "@tanstack/react-router";

type AppRouter = ReturnType<typeof createAppRouter>;

type NavItem = {
  readonly to: keyof AppRouter["routesByPath"];
  readonly label: string;
};

const NAV_ITEMS: readonly NavItem[] = [
  { to: "/", label: "Files" },
  { to: "/settings", label: "Settings" },
  { to: "/autoqueue", label: "Autoqueue" },
  { to: "/about", label: "About" },
];

function PageTitle({ title }: { readonly title: string }) {
  return <h1 className="text-2xl font-semibold">{title}</h1>;
}

const rootRoute = createRootRoute({
  component: () => (
    <div className="min-h-screen bg-white text-gray-900 dark:bg-gray-950 dark:text-gray-100">
      <nav
        aria-label="Main"
        className="flex gap-4 border-b border-gray-200 px-6 py-3 dark:border-gray-800"
      >
        {NAV_ITEMS.map((item) => (
          <Link
            key={item.to}
            to={item.to}
            activeOptions={{ exact: true }}
            activeProps={{ className: "font-semibold" }}
            className="hover:underline"
          >
            {item.label}
          </Link>
        ))}
      </nav>
      <main className="p-6">
        <Outlet />
      </main>
    </div>
  ),
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  component: () => <PageTitle title="SporeSync" />,
});

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/settings",
  component: () => <PageTitle title="Settings" />,
});

const autoqueueRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/autoqueue",
  component: () => <PageTitle title="Autoqueue" />,
});

const aboutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/about",
  component: () => <PageTitle title="About" />,
});

const routeTree = rootRoute.addChildren([indexRoute, settingsRoute, autoqueueRoute, aboutRoute]);

export function createAppRouter(history: RouterHistory) {
  return createRouter({ routeTree, history });
}

declare module "@tanstack/react-router" {
  interface Register {
    router: AppRouter;
  }
}
