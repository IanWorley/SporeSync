/* eslint-disable react-refresh/only-export-components */
import { createRootRouteWithContext, createRoute, redirect, useParams } from "@tanstack/react-router";
import type { QueryClient } from "@tanstack/react-query";
import { useQuery } from "@tanstack/react-query";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import { AppShell } from "../components/AppShell";
import { SectionHeader } from "../components/SectionHeader";
import { StatusBadge } from "../components/StatusBadge";
import { Button } from "../components/Button";
import { formatBytes, formatLocalDateTime, formatRate, formatRelativeTime } from "../lib/format";

interface RouterContext {
  queryClient: QueryClient;
}

const rootRoute = createRootRouteWithContext<RouterContext>()({
  component: AppShell
});

const indexRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/",
  beforeLoad: () => {
    throw redirect({ to: "/dashboard" });
  }
});

const dashboardRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/dashboard",
  component: DashboardPage
});

const dashboardRunRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/dashboard/runs/$runId",
  component: RunDetailPage
});

const runsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/runs",
  component: RunsPage
});

const runDetailRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/runs/$runId",
  component: RunDetailPage
});

const jobsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/jobs",
  component: JobsPage
});

const profilesRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/profiles",
  component: ProfilesPage
});

const settingsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/settings",
  component: SettingsPage
});

const logsRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/logs",
  component: LogsPage
});

const aboutRoute = createRoute({
  getParentRoute: () => rootRoute,
  path: "/about",
  component: AboutPage
});

export const routeTree = rootRoute.addChildren([
  indexRoute,
  dashboardRoute,
  dashboardRunRoute,
  runsRoute,
  runDetailRoute,
  jobsRoute,
  profilesRoute,
  settingsRoute,
  logsRoute,
  aboutRoute
]);

function DashboardPage() {
  const runsQuery = useQuery({
    queryKey: queryKeys.runs({ pageNumber: 1, pageSize: 10 }),
    queryFn: () => api.runs({ pageNumber: 1, pageSize: 10, sortBy: "startedAt", sortDirection: "desc" })
  });
  const activeRun = runsQuery.data?.items.find((run) => ["Running", "Pending"].includes(run.status)) ?? runsQuery.data?.items[0];
  const queueQuery = useQuery({
    queryKey: queryKeys.queueItems(activeRun?.id ?? "none", { pageNumber: 1, pageSize: 8 }),
    queryFn: () => api.queueItems(activeRun!.id, { pageNumber: 1, pageSize: 8, sortBy: "queuedAt", sortDirection: "desc" }),
    enabled: Boolean(activeRun)
  });

  return (
    <div>
      <SectionHeader title="Operational Dashboard" description="Live run state and queue progress will converge here as the next milestone adds SignalR updates and table controls." />
      <div className="grid gap-4 lg:grid-cols-[minmax(0,0.9fr)_minmax(0,1.3fr)]">
        <section className="rounded-lg border border-border bg-panel">
          <div className="border-b border-border px-4 py-3">
            <h3 className="text-sm font-semibold">Recent Runs</h3>
          </div>
          <div className="divide-y divide-border">
            {runsQuery.isLoading && <SkeletonRows />}
            {runsQuery.data?.items.map((run) => (
              <div key={run.id} className="px-4 py-3">
                <div className="flex items-center justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">{run.jobName}</p>
                    <p className="text-xs text-muted-foreground" title={formatLocalDateTime(run.startedAt)}>
                      {formatRelativeTime(run.startedAt)}
                    </p>
                  </div>
                  <StatusBadge status={run.status} />
                </div>
                <Progress value={run.downloadedBytes} total={run.totalBytes} />
              </div>
            ))}
          </div>
        </section>
        <section className="rounded-lg border border-border bg-panel">
          <div className="border-b border-border px-4 py-3">
            <h3 className="text-sm font-semibold">{activeRun ? `${activeRun.jobName} Queue` : "Queue"}</h3>
          </div>
          <QueuePreview items={queueQuery.data?.items ?? []} isLoading={queueQuery.isLoading} />
        </section>
      </div>
    </div>
  );
}

function RunsPage() {
  const runsQuery = useQuery({
    queryKey: queryKeys.runs({ pageNumber: 1, pageSize: 25 }),
    queryFn: () => api.runs({ pageNumber: 1, pageSize: 25, sortBy: "startedAt", sortDirection: "desc" })
  });

  return (
    <div>
      <SectionHeader title="Runs" description="Historical and active sync runs from the backend API." />
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
                <td className="px-4 py-3"><StatusBadge status={run.status} /></td>
                <td className="px-4 py-3">{formatBytes(run.downloadedBytes)} / {formatBytes(run.totalBytes)}</td>
                <td className="px-4 py-3">{formatRate(run.currentBytesPerSecond)}</td>
                <td className="px-4 py-3">{formatLocalDateTime(run.startedAt)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </div>
  );
}

function RunDetailPage() {
  const params = useParams({ strict: false }) as { runId?: string };
  return (
    <div>
      <SectionHeader title="Run Detail" description={`Run ${params.runId ?? "unknown"}`} />
      <div className="rounded-lg border border-border bg-panel p-4 text-sm text-muted-foreground">
        Queue item table and selected item details panel land in the dashboard milestone.
      </div>
    </div>
  );
}

function JobsPage() {
  const jobsQuery = useQuery({ queryKey: queryKeys.jobs, queryFn: api.jobs });

  return (
    <div>
      <SectionHeader title="Jobs" description="Configured sync jobs. Create and edit workflows are planned for the admin milestone." actions={<Button>New Job</Button>} />
      <SimpleList
        items={jobsQuery.data?.map((job) => ({
          id: job.id,
          title: job.name,
          detail: `${job.sourcePath} -> ${job.destinationPath}`,
          meta: job.isEnabled ? "Enabled" : "Disabled"
        }))}
      />
    </div>
  );
}

function ProfilesPage() {
  const profilesQuery = useQuery({ queryKey: queryKeys.profiles, queryFn: api.profiles });

  return (
    <div>
      <SectionHeader title="Profiles" description="SFTP connection profiles with write-only secret replacement fields coming in the admin milestone." actions={<Button>New Profile</Button>} />
      <SimpleList
        items={profilesQuery.data?.map((profile) => ({
          id: profile.id,
          title: profile.name,
          detail: `${profile.username}@${profile.host}:${profile.port}`,
          meta: profile.isDefault ? "Default" : "Profile"
        }))}
      />
    </div>
  );
}

function SettingsPage() {
  return (
    <div>
      <SectionHeader title="Settings" description="Local UI preferences are active now. Backend system property editing lands with the admin milestone." />
      <div className="rounded-lg border border-border bg-panel p-4 text-sm text-muted-foreground">Theme and sidebar preferences are persisted locally.</div>
    </div>
  );
}

function LogsPage() {
  return (
    <div>
      <SectionHeader title="Logs" description="Placeholder for the LogAppended SignalR contract and persisted log views." />
      <div className="rounded-lg border border-border bg-panel p-4 font-mono text-sm text-muted-foreground">No logs loaded.</div>
    </div>
  );
}

function AboutPage() {
  const statusQuery = useQuery({ queryKey: queryKeys.status, queryFn: api.status });

  return (
    <div>
      <SectionHeader title="About" description="Runtime status from the backend." />
      <dl className="grid gap-3 rounded-lg border border-border bg-panel p-4 text-sm sm:grid-cols-2">
        <InfoRow label="API Status" value={statusQuery.data?.status ?? "Loading"} />
        <InfoRow label="Environment" value={statusQuery.data?.environment ?? "Loading"} />
        <InfoRow label="Database" value={statusQuery.data?.databaseAvailable ? "Available" : "Unavailable"} />
        <InfoRow label="Current Time" value={formatLocalDateTime(statusQuery.data?.currentTime)} />
      </dl>
    </div>
  );
}

function QueuePreview({ items, isLoading }: { items: Array<{ id: string; remotePath: string; status: string; bytesDownloaded: number; fileSizeBytes: number }>; isLoading: boolean }) {
  if (isLoading) {
    return <SkeletonRows />;
  }

  if (items.length === 0) {
    return <div className="p-4 text-sm text-muted-foreground">No queue items found.</div>;
  }

  return (
    <div className="divide-y divide-border">
      {items.map((item) => (
        <div key={item.id} className="px-4 py-3">
          <div className="flex items-center justify-between gap-3">
            <p className="min-w-0 truncate text-sm font-medium">{item.remotePath.split("/").pop() ?? item.remotePath}</p>
            <StatusBadge status={item.status} />
          </div>
          <Progress value={item.bytesDownloaded} total={item.fileSizeBytes} />
        </div>
      ))}
    </div>
  );
}

function Progress({ value, total }: { value: number; total: number }) {
  const percent = total > 0 ? Math.min(100, Math.round((value / total) * 100)) : 0;
  return (
    <div className="mt-3">
      <div className="h-2 overflow-hidden rounded-full bg-muted">
        <div className="h-full rounded-full bg-accent" style={{ width: `${percent}%` }} />
      </div>
      <p className="mt-1 text-xs text-muted-foreground">{formatBytes(value)} / {formatBytes(total)}</p>
    </div>
  );
}

function SimpleList({ items }: { items?: Array<{ id: string; title: string; detail: string; meta: string }> }) {
  return (
    <div className="divide-y divide-border rounded-lg border border-border bg-panel">
      {items?.map((item) => (
        <div key={item.id} className="flex items-center justify-between gap-4 px-4 py-3">
          <div className="min-w-0">
            <p className="truncate text-sm font-medium">{item.title}</p>
            <p className="truncate text-xs text-muted-foreground">{item.detail}</p>
          </div>
          <span className="text-xs font-medium text-muted-foreground">{item.meta}</span>
        </div>
      )) ?? <SkeletonRows />}
    </div>
  );
}

function InfoRow({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <dt className="text-xs uppercase text-muted-foreground">{label}</dt>
      <dd className="mt-1 font-medium">{value}</dd>
    </div>
  );
}

function SkeletonRows() {
  return (
    <div className="space-y-3 p-4">
      <div className="h-4 w-3/5 animate-pulse rounded bg-muted" />
      <div className="h-4 w-4/5 animate-pulse rounded bg-muted" />
      <div className="h-4 w-2/5 animate-pulse rounded bg-muted" />
    </div>
  );
}
