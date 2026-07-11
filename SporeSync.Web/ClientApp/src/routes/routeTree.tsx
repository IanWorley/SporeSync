import type { QueryClient } from "@tanstack/react-query";
import { useQuery } from "@tanstack/react-query";
import {
  createRootRouteWithContext,
  createRoute,
  redirect,
} from "@tanstack/react-router";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import type { AuthSession } from "../api/types";
import { AppShell } from "../components/AppShell";
import { SectionHeader } from "../components/SectionHeader";
import { StatusBadge } from "../components/StatusBadge";
import { formatBytes, formatLocalDateTime, formatRate } from "../lib/format";
import {
  AboutPage,
  JobsPage,
  LogsPage,
  ProfilesPage,
  SettingsPage,
} from "./AdminPages";
import { DashboardPage } from "./DashboardPage";
import { LoginPage } from "./LoginPage";

interface RouterContext {
  queryClient: QueryClient;
}

async function ensureSession(queryClient: QueryClient): Promise<AuthSession> {
  try {
    return await queryClient.ensureQueryData({
      queryKey: queryKeys.session,
      queryFn: api.session,
      staleTime: 60_000,
    });
  } catch {
    // If session discovery fails (e.g. backend briefly unreachable), render
    // the app anyway; the server still rejects unauthenticated API calls,
    // and those 401s route back to /login.
    return { authRequired: false, authenticated: true, username: null };
  }
}

const rootRoute = createRootRouteWithContext<RouterContext>()({});

const loginRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/login",
  beforeLoad: async ({ context }) => {
    const session = await ensureSession(context.queryClient);
    if (!session.authRequired || session.authenticated) {
      throw redirect({ to: "/dashboard" });
    }
  },
  component: LoginPage,
});

const appRoute = createRoute({
  getParentRoute: () => rootRoute,
  id: "app",
  beforeLoad: async ({ context }) => {
    const session = await ensureSession(context.queryClient);
    if (session.authRequired && !session.authenticated) {
      throw redirect({ to: "/login" });
    }
  },
  component: AppShell,
});

const indexRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/",
  beforeLoad: () => {
    throw redirect({ to: "/dashboard" });
  },
});

const dashboardRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/dashboard",
  component: DashboardPage,
});

const dashboardRunRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/dashboard/runs/$runId",
  component: DashboardPage,
});

const runsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/runs",
  component: RunsPage,
});

const runDetailRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/runs/$runId",
  component: DashboardPage,
});

const jobsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/jobs",
  component: JobsPage,
});

const profilesRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/profiles",
  component: ProfilesPage,
});

const settingsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/settings",
  component: SettingsPage,
});

const logsRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/logs",
  component: LogsPage,
});

const aboutRoute = createRoute({
  getParentRoute: () => appRoute,
  path: "/about",
  component: AboutPage,
});

export const routeTree = rootRoute.addChildren([
  loginRoute,
  appRoute.addChildren([
    indexRoute,
    dashboardRoute,
    dashboardRunRoute,
    runsRoute,
    runDetailRoute,
    jobsRoute,
    profilesRoute,
    settingsRoute,
    logsRoute,
    aboutRoute,
  ]),
]);

function RunsPage() {
  const runsQuery = useQuery({
    queryKey: queryKeys.runs({ pageNumber: 1, pageSize: 25 }),
    queryFn: () =>
      api.runs({
        pageNumber: 1,
        pageSize: 25,
        sortBy: "startedAt",
        sortDirection: "desc",
      }),
  });

  return (
    <div>
      <SectionHeader
        title="Runs"
        description="Historical and active sync runs from the backend API."
      />
      <div className="overflow-hidden rounded-lg border border-border bg-panel">
        <table className="w-full min-w-[760px] text-left text-sm">
          <thead className="bg-muted text-xs uppercase text-muted-foreground">
            <tr>
              <th className="px-4 py-3">Job</th>
              <th className="px-4 py-3">Status</th>
              <th className="px-4 py-3">Progress</th>
              <th className="px-4 py-3">Rate</th>
              <th className="px-4 py-3">Started</th>
            </tr>
          </thead>
          <tbody className="divide-y divide-border">
            {runsQuery.data?.items.map((run) => (
              <tr key={run.id}>
                <td className="px-4 py-3 font-medium">{run.jobName}</td>
                <td className="px-4 py-3">
                  <StatusBadge status={run.status} />
                </td>
                <td className="px-4 py-3">
                  {formatBytes(run.downloadedBytes)} /{" "}
                  {formatBytes(run.totalBytes)}
                </td>
                <td className="px-4 py-3">
                  {formatRate(run.currentBytesPerSecond)}
                </td>
                <td className="px-4 py-3">
                  {formatLocalDateTime(run.startedAt)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}
