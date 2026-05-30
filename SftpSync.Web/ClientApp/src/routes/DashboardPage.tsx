import { HubConnectionBuilder, LogLevel } from "@microsoft/signalr";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { useNavigate, useParams, useRouterState } from "@tanstack/react-router";
import {
  ArrowDown,
  ArrowUp,
  CheckCircle2,
  ChevronsLeft,
  ChevronsRight,
  FileWarning,
  Folder,
  Loader2,
  Search,
  Trash2,
  Wifi,
  WifiOff,
} from "lucide-react";
import type { ReactNode } from "react";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import {
  removeQueueItemFromPagedCaches,
  upsertQueueItemInPagedCaches,
  upsertRunInPagedCaches,
} from "../api/cacheUpdates";
import { api, type PageQuery } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import type { DownloadQueueItem, SftpSyncRun } from "../api/types";
import { Button } from "../components/Button";
import { SectionHeader } from "../components/SectionHeader";
import { StatusBadge } from "../components/StatusBadge";
import { cn } from "../lib/cn";
import {
  formatBytes,
  formatLocalDateTime,
  formatRate,
  formatRelativeTime,
} from "../lib/format";

const activeStatuses = ["Running", "Pending"];
const queueStatuses = [
  "Queued",
  "Downloading",
  "Completed",
  "Failed",
  "Skipped",
];
const pageSizeOptions = [10, 25, 50];

type SignalRState =
  | "connecting"
  | "connected"
  | "reconnecting"
  | "disconnected";
type Toast = { id: number; tone: "success" | "error"; message: string };
type SortDirection = "asc" | "desc";

export function DashboardPage() {
  const navigate = useNavigate();
  const { location } = useRouterState();
  const params = useParams({ strict: false }) as { runId?: string };
  const runIdFromRoute = params.runId;
  const isDashboardRoute = location.pathname.startsWith("/dashboard");
  const [userSelectedRunId, setUserSelectedRunId] = useState<
    string | undefined
  >();
  const [selectedItemId, setSelectedItemId] = useState<string | undefined>();
  const [search, setSearch] = useLocalState<string>(
    "sftpsync:queue-search",
    "",
  );
  const [statusFilter, setStatusFilter] = useLocalArrayState(
    "sftpsync:queue-statuses",
  );
  const [pageNumber, setPageNumber] = useState(1);
  const [pageSize, setPageSize] = useLocalNumberState(
    "sftpsync:queue-page-size",
    25,
  );
  const [sortBy, setSortBy] = useLocalState<string>(
    "sftpsync:queue-sort-by",
    "queuedAt",
  );
  const [sortDirection, setSortDirection] = useLocalState<SortDirection>(
    "sftpsync:queue-sort-direction",
    "desc",
  );
  const [compact, setCompact] = useLocalBooleanState(
    "sftpsync:queue-compact",
    true,
  );
  const [toasts, setToasts] = useState<Toast[]>([]);
  const pushToast = useCallback(
    (toast: Toast) => setToasts((items) => [toast, ...items].slice(0, 3)),
    [],
  );

  const runQuery = useQuery({
    queryKey: queryKeys.run(runIdFromRoute ?? "none"),
    queryFn: () => {
      if (!runIdFromRoute) {
        throw new Error("Run id is required.");
      }
      return api.run(runIdFromRoute);
    },
    enabled: Boolean(runIdFromRoute),
  });
  const runsQuery = useQuery({
    queryKey: queryKeys.runs({
      pageNumber: 1,
      pageSize: 20,
      sortBy: "startedAt",
      sortDirection: "desc",
    }),
    queryFn: () =>
      api.runs({
        pageNumber: 1,
        pageSize: 20,
        sortBy: "startedAt",
        sortDirection: "desc",
      }),
  });
  const _statusQuery = useQuery({
    queryKey: queryKeys.status,
    queryFn: api.status,
    refetchInterval: 30_000,
  });

  const fallbackRun =
    runsQuery.data?.items.find((run) => activeStatuses.includes(run.status)) ??
    runsQuery.data?.items[0];
  const selectedRunId = runIdFromRoute ?? userSelectedRunId ?? fallbackRun?.id;
  const selectedRun =
    runQuery.data ??
    runsQuery.data?.items.find((run) => run.id === selectedRunId);
  const effectiveRun = selectedRun ?? fallbackRun;
  const effectiveRunId = effectiveRun?.id;

  const queueQueryParams = useMemo<PageQuery>(
    () => ({
      status: statusFilter,
      search: search.trim() || undefined,
      sortBy,
      sortDirection,
      pageNumber,
      pageSize,
    }),
    [pageNumber, pageSize, search, sortBy, sortDirection, statusFilter],
  );
  const queueQuery = useQuery({
    queryKey: queryKeys.queueItems(effectiveRunId ?? "none", queueQueryParams),
    queryFn: () => {
      if (!effectiveRunId) {
        throw new Error("Run id is required.");
      }
      return api.queueItems(effectiveRunId, queueQueryParams);
    },
    enabled: Boolean(effectiveRunId),
  });
  const deleteFileMutation = useMutation({
    mutationFn: ({
      item,
      target,
    }: {
      item: DownloadQueueItem;
      target: "local" | "remote";
    }) => {
      if (!item.syncRunId) {
        throw new Error("Queue item is not associated with a run.");
      }

      return api.deleteQueueItemFile(item.syncRunId, item.id, target);
    },
    onSuccess: (result) => {
      pushToast({
        id: Date.now(),
        tone: "success",
        message: result.existed
          ? `Deleted ${result.target} file`
          : `${capitalize(result.target)} file was already missing`,
      });
    },
    onError: (error) => {
      pushToast({
        id: Date.now(),
        tone: "error",
        message:
          error instanceof Error ? error.message : "Delete request failed",
      });
    },
  });

  const selectedItem = queueQuery.data?.items.find(
    (item) => item.id === selectedItemId,
  );
  const signalRState = useDashboardSignalR(effectiveRunId, pushToast);
  const totalPages = Math.max(
    1,
    Math.ceil((queueQuery.data?.totalCount ?? 0) / pageSize),
  );
  const from = queueQuery.data?.totalCount
    ? (pageNumber - 1) * pageSize + 1
    : 0;
  const to = Math.min(pageNumber * pageSize, queueQuery.data?.totalCount ?? 0);

  useEffect(() => {
    const onKeyDown = (event: KeyboardEvent) => {
      if (
        event.key === "/" &&
        event.target instanceof HTMLElement &&
        !["INPUT", "TEXTAREA", "SELECT"].includes(event.target.tagName)
      ) {
        event.preventDefault();
        document.getElementById("queue-search")?.focus();
      }

      if (event.key === "Escape") {
        setSearch("");
        setSelectedItemId(undefined);
      }
    };

    window.addEventListener("keydown", onKeyDown);
    return () => window.removeEventListener("keydown", onKeyDown);
  }, [setSearch]);

  useEffect(() => {
    if (toasts.length === 0) {
      return;
    }

    const timer = window.setTimeout(
      () => setToasts((items) => items.slice(0, -1)),
      4_500,
    );
    return () => window.clearTimeout(timer);
  }, [toasts]);

  const chooseRun = (runId: string) => {
    setUserSelectedRunId(runId);
    setSelectedItemId(undefined);
    setPageNumber(1);
    void navigate({
      to: isDashboardRoute ? "/dashboard/runs/$runId" : "/runs/$runId",
      params: { runId },
    });
  };

  const sortQueue = (column: string) => {
    setPageNumber(1);
    if (sortBy === column) {
      setSortDirection(sortDirection === "asc" ? "desc" : "asc");
    } else {
      setSortBy(column);
      setSortDirection(
        column === "basename" || column === "status" ? "asc" : "desc",
      );
    }
  };

  const deleteSelectedFile = (
    item: DownloadQueueItem,
    target: "local" | "remote",
  ) => {
    const label = item.isGroup ? "folder group" : "file";
    const path = displayPath(item, target);
    if (
      !window.confirm(
        `Delete this ${label} ${target === "local" ? "locally" : "remotely"}?\n\n${path}`,
      )
    ) {
      return;
    }

    deleteFileMutation.mutate({ item, target });
  };

  return (
    <div className="space-y-4">
      <SectionHeader
        title={isDashboardRoute ? "Operational Dashboard" : "Run History"}
        description="Live sync runs, queue progress, filters, and selected item details (files or opaque folder groups)."
        actions={<ConnectionIndicator state={signalRState} />}
      />

      <div className="grid gap-4 xl:grid-cols-[minmax(280px,0.45fr)_minmax(0,1fr)]">
        <section className="overflow-hidden rounded-lg border border-border bg-panel">
          <div className="flex items-center justify-between border-b border-border px-4 py-3">
            <h3 className="text-sm font-semibold">Runs</h3>
            <span className="text-xs text-muted-foreground">
              {runsQuery.data?.totalCount ?? 0} total
            </span>
          </div>
          <div className="max-h-[620px] divide-y divide-border overflow-auto">
            {runsQuery.isLoading && <SkeletonRows />}
            {runsQuery.data?.items.map((run) => (
              <button
                type="button"
                key={run.id}
                className={cn(
                  "block w-full px-4 py-3 text-left transition hover:bg-muted",
                  effectiveRunId === run.id && "bg-muted",
                )}
                onClick={() => chooseRun(run.id)}
              >
                <div className="flex items-start justify-between gap-3">
                  <div className="min-w-0">
                    <p className="truncate text-sm font-medium">
                      {run.jobName}
                    </p>
                    <p
                      className="mt-0.5 text-xs text-muted-foreground"
                      title={formatLocalDateTime(run.startedAt)}
                    >
                      {formatRelativeTime(run.startedAt)}
                    </p>
                  </div>
                  <StatusBadge status={run.status} />
                </div>
                <Progress value={run.downloadedBytes} total={run.totalBytes} />
                <div className="mt-2 flex items-center justify-between text-xs text-muted-foreground">
                  <span>
                    {run.completedFileCount}/{run.totalFileCount} files
                  </span>
                  <span>{formatRate(run.currentBytesPerSecond)}</span>
                </div>
              </button>
            ))}
            {runsQuery.data?.items.length === 0 && (
              <EmptyState message="No runs found." />
            )}
          </div>
        </section>

        <div className="space-y-4">
          <RunSummary run={effectiveRun} />

          <section className="overflow-hidden rounded-lg border border-border bg-panel">
            <div className="grid gap-3 border-b border-border p-4 lg:grid-cols-[minmax(180px,1fr)_auto]">
              <label className="relative block">
                <Search
                  className="pointer-events-none absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground"
                  size={16}
                />
                <input
                  id="queue-search"
                  className="h-9 w-full rounded-md border border-border bg-background pl-9 pr-3 text-sm outline-none focus:shadow-focus"
                  value={search}
                  onChange={(event) => {
                    setSearch(event.target.value);
                    setPageNumber(1);
                  }}
                  placeholder="Search paths"
                />
              </label>
              <div className="flex flex-wrap items-center gap-2">
                <Button
                  className={compact ? "bg-muted" : ""}
                  onClick={() => setCompact(!compact)}
                >
                  {compact ? "Compact" : "Detailed"}
                </Button>
                <select
                  className="h-9 rounded-md border border-border bg-panel px-2 text-sm"
                  value={pageSize}
                  onChange={(event) => {
                    setPageSize(Number(event.target.value));
                    setPageNumber(1);
                  }}
                >
                  {pageSizeOptions.map((option) => (
                    <option key={option} value={option}>
                      {option}/page
                    </option>
                  ))}
                </select>
              </div>
              <div className="flex flex-wrap gap-2 lg:col-span-2">
                {queueStatuses.map((status) => (
                  <button
                    type="button"
                    key={status}
                    className={cn(
                      "rounded-full border border-border px-3 py-1 text-xs font-medium text-muted-foreground transition hover:bg-muted hover:text-foreground",
                      statusFilter.includes(status) &&
                        "bg-muted text-foreground",
                    )}
                    onClick={() => {
                      setStatusFilter(toggleValue(statusFilter, status));
                      setPageNumber(1);
                    }}
                  >
                    {status}
                  </button>
                ))}
              </div>
            </div>

            <QueueTable
              items={queueQuery.data?.items ?? []}
              isLoading={queueQuery.isLoading}
              selectedItemId={selectedItemId}
              compact={compact}
              sortBy={sortBy}
              sortDirection={sortDirection}
              onSelectItem={setSelectedItemId}
              onSort={sortQueue}
            />

            <div className="flex flex-wrap items-center justify-between gap-3 border-t border-border px-4 py-3 text-sm text-muted-foreground">
              <span>
                {from}-{to} of {queueQuery.data?.totalCount ?? 0}
              </span>
              <div className="flex items-center gap-2">
                <Button
                  disabled={pageNumber <= 1}
                  onClick={() =>
                    setPageNumber((value) => Math.max(1, value - 1))
                  }
                >
                  <ChevronsLeft size={16} />
                </Button>
                <span>
                  Page {pageNumber} of {totalPages}
                </span>
                <Button
                  disabled={pageNumber >= totalPages}
                  onClick={() =>
                    setPageNumber((value) => Math.min(totalPages, value + 1))
                  }
                >
                  <ChevronsRight size={16} />
                </Button>
              </div>
            </div>
          </section>
        </div>
      </div>

      <ItemDetails
        item={selectedItem}
        isDeleting={deleteFileMutation.isPending}
        onDelete={deleteSelectedFile}
        onClose={() => setSelectedItemId(undefined)}
      />
      <ToastStack toasts={toasts} />
    </div>
  );
}

function useDashboardSignalR(
  runId: string | undefined,
  pushToast: (toast: Toast) => void,
) {
  const queryClient = useQueryClient();
  const [state, setState] = useState<SignalRState>("connecting");
  const previousRuns = useRef(new Map<string, string>());

  useEffect(() => {
    let stopped = false;
    const connection = new HubConnectionBuilder()
      .withUrl("/hubs/dashboard")
      .withAutomaticReconnect()
      .configureLogging(LogLevel.Warning)
      .build();

    connection.onreconnecting(() => setState("reconnecting"));
    connection.onreconnected(() => {
      setState("connected");
      void connection.invoke("SubscribeDashboard");
      if (runId) {
        void connection.invoke("SubscribeRun", runId);
      }
      void queryClient.invalidateQueries({ queryKey: ["runs"] });
      if (runId) {
        void queryClient.invalidateQueries({
          queryKey: ["runs", runId, "queue-items"],
        });
      }
    });
    connection.onclose(() => setState("disconnected"));
    connection.on("RunUpdated", (run: SftpSyncRun) => {
      const previousStatus = previousRuns.current.get(run.id);
      previousRuns.current.set(run.id, run.status);
      upsertRunInPagedCaches(queryClient, run);
      if (previousStatus && previousStatus !== run.status) {
        if (run.status === "Completed")
          pushToast({
            id: Date.now(),
            tone: "success",
            message: `${run.jobName} completed`,
          });
        if (run.status === "Failed")
          pushToast({
            id: Date.now(),
            tone: "error",
            message: `${run.jobName} failed`,
          });
      }
    });
    connection.on("QueueItemUpdated", (item: DownloadQueueItem) => {
      upsertQueueItemInPagedCaches(queryClient, item);
      if (item.syncRunId === runId && item.status === "Failed") {
        pushToast({
          id: Date.now(),
          tone: "error",
          message: `${displayName(item)} failed`,
        });
      }
    });
    connection.on(
      "QueueItemRemoved",
      (event: { runId: string; queueItemId: string }) => {
        removeQueueItemFromPagedCaches(
          queryClient,
          event.runId,
          event.queueItemId,
        );
      },
    );

    void connection
      .start()
      .then(async () => {
        if (stopped) return;
        setState("connected");
        await connection.invoke("SubscribeDashboard");
        if (runId) {
          await connection.invoke("SubscribeRun", runId);
        }
      })
      .catch(() => setState("disconnected"));

    return () => {
      stopped = true;
      void connection.stop();
    };
  }, [pushToast, queryClient, runId]);

  return state;
}

function ConnectionIndicator({ state }: { state: SignalRState }) {
  const connected = state === "connected";
  return (
    <div className="inline-flex h-9 items-center gap-2 rounded-md border border-border bg-panel px-3 text-sm">
      {connected ? (
        <Wifi size={16} className="text-emerald-500" />
      ) : (
        <WifiOff size={16} className="text-amber-500" />
      )}
      <span className="capitalize">{state}</span>
    </div>
  );
}

function RunSummary({ run }: { run?: SftpSyncRun }) {
  if (!run) {
    return (
      <section className="rounded-lg border border-border bg-panel p-4 text-sm text-muted-foreground">
        No run selected.
      </section>
    );
  }

  return (
    <section className="grid gap-3 rounded-lg border border-border bg-panel p-4 sm:grid-cols-2 xl:grid-cols-4">
      <Metric label="Status" value={<StatusBadge status={run.status} />} />
      <Metric
        label="Files"
        value={`${run.completedFileCount}/${run.totalFileCount}`}
      />
      <Metric
        label="Progress"
        value={`${formatBytes(run.downloadedBytes)} / ${formatBytes(run.totalBytes)}`}
      />
      <Metric label="Rate" value={formatRate(run.currentBytesPerSecond)} />
      <Metric label="Started" value={formatLocalDateTime(run.startedAt)} />
      <Metric label="Completed" value={formatLocalDateTime(run.completedAt)} />
      <Metric label="Skipped" value={String(run.skippedFileCount)} />
      <Metric label="Failed" value={String(run.failedFileCount)} />
    </section>
  );
}

function Metric({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="min-w-0">
      <div className="text-xs uppercase text-muted-foreground">{label}</div>
      <div className="mt-1 truncate text-sm font-semibold">{value}</div>
    </div>
  );
}

function QueueTable({
  items,
  isLoading,
  selectedItemId,
  compact,
  sortBy,
  sortDirection,
  onSelectItem,
  onSort,
}: {
  items: DownloadQueueItem[];
  isLoading: boolean;
  selectedItemId?: string;
  compact: boolean;
  sortBy: string;
  sortDirection: SortDirection;
  onSelectItem: (id: string) => void;
  onSort: (column: string) => void;
}) {
  if (isLoading) {
    return <SkeletonRows />;
  }

  if (items.length === 0) {
    return <EmptyState message="No queue items match the current filters." />;
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[940px] text-left text-sm">
        <thead className="bg-muted text-xs uppercase text-muted-foreground">
          <tr>
            <SortableHeader
              column="basename"
              active={sortBy}
              direction={sortDirection}
              onSort={onSort}
            >
              Item
            </SortableHeader>
            <SortableHeader
              column="status"
              active={sortBy}
              direction={sortDirection}
              onSort={onSort}
            >
              Status
            </SortableHeader>
            <SortableHeader
              column="currentBytesPerSecond"
              active={sortBy}
              direction={sortDirection}
              onSort={onSort}
            >
              Speed
            </SortableHeader>
            <th className="px-4 py-3">ETA</th>
            <SortableHeader
              column="progress"
              active={sortBy}
              direction={sortDirection}
              onSort={onSort}
            >
              Progress
            </SortableHeader>
            <SortableHeader
              column="queuedAt"
              active={sortBy}
              direction={sortDirection}
              onSort={onSort}
            >
              Queued
            </SortableHeader>
            {!compact && <th className="px-4 py-3">Destination</th>}
          </tr>
        </thead>
        <tbody className="divide-y divide-border">
          {items.map((item) => (
            <tr
              key={item.id}
              className={cn(
                "cursor-pointer hover:bg-muted/70",
                selectedItemId === item.id && "bg-muted",
              )}
              onClick={() => onSelectItem(item.id)}
            >
              <td className="max-w-[300px] px-4 py-3">
                <div
                  className={cn(
                    "flex items-center gap-2",
                    item.isGroup && "text-blue-600 dark:text-blue-400",
                  )}
                >
                  {item.isGroup ? (
                    <Folder size={16} className="shrink-0" />
                  ) : null}
                  <p className="truncate font-medium">{displayName(item)}</p>
                </div>
                {!compact && (
                  <p className="truncate text-xs text-muted-foreground">
                    {displayPath(item, "remote")}
                  </p>
                )}
                {item.isGroup && (
                  <span className="inline-block rounded bg-blue-100 px-1.5 py-0.5 text-[10px] font-medium text-blue-700 dark:bg-blue-950 dark:text-blue-300">
                    Folder group
                  </span>
                )}
              </td>
              <td className="px-4 py-3">
                <StatusBadge status={item.status} />
              </td>
              <td className="px-4 py-3">
                {formatRate(item.currentBytesPerSecond)}
              </td>
              <td className="px-4 py-3">{formatEta(item)}</td>
              <td className="px-4 py-3">
                <Progress
                  value={item.bytesDownloaded}
                  total={item.fileSizeBytes}
                  compact
                />
              </td>
              <td
                className="px-4 py-3"
                title={formatLocalDateTime(item.queuedAt)}
              >
                {formatRelativeTime(item.queuedAt)}
              </td>
              {!compact && (
                <td className="max-w-[280px] truncate px-4 py-3 text-muted-foreground">
                  {displayPath(item, "local")}
                </td>
              )}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

function SortableHeader({
  column,
  active,
  direction,
  onSort,
  children,
}: {
  column: string;
  active: string;
  direction: SortDirection;
  onSort: (column: string) => void;
  children: ReactNode;
}) {
  const isActive = active === column;
  return (
    <th className="px-4 py-3">
      <button
        type="button"
        className="inline-flex items-center gap-1 font-semibold"
        onClick={() => onSort(column)}
      >
        {children}
        {isActive &&
          (direction === "asc" ? (
            <ArrowUp size={13} />
          ) : (
            <ArrowDown size={13} />
          ))}
      </button>
    </th>
  );
}

function ItemDetails({
  item,
  isDeleting,
  onDelete,
  onClose,
}: {
  item?: DownloadQueueItem;
  isDeleting: boolean;
  onDelete: (item: DownloadQueueItem, target: "local" | "remote") => void;
  onClose: () => void;
}) {
  if (!item) {
    return null;
  }

  return (
    <aside className="fixed inset-y-0 right-0 z-40 flex w-full max-w-md flex-col border-l border-border bg-panel shadow-xl sm:top-14">
      <div className="flex items-center justify-between border-b border-border px-4 py-3">
        <div className="flex min-w-0 items-center gap-2">
          {item.isGroup ? (
            <Folder
              size={18}
              className="shrink-0 text-blue-600 dark:text-blue-400"
            />
          ) : null}
          <h3 className="truncate text-sm font-semibold">
            {displayName(item)}
          </h3>
          {item.isGroup && (
            <span className="rounded bg-blue-100 px-1.5 py-0.5 text-[10px] font-medium text-blue-700 dark:bg-blue-950 dark:text-blue-300">
              Folder group
            </span>
          )}
        </div>
        <Button onClick={onClose}>Close</Button>
      </div>
      <dl className="grid gap-4 overflow-auto p-4 text-sm">
        <div className="grid gap-2 sm:grid-cols-2">
          <Button
            className="justify-start border-red-500/40 text-red-600 hover:bg-red-500/10 dark:text-red-400"
            disabled={isDeleting}
            onClick={() => onDelete(item, "local")}
          >
            <Trash2 size={16} />
            Delete Local
          </Button>
          <Button
            className="justify-start border-red-500/40 text-red-600 hover:bg-red-500/10 dark:text-red-400"
            disabled={isDeleting}
            onClick={() => onDelete(item, "remote")}
          >
            <Trash2 size={16} />
            Delete Remote
          </Button>
        </div>
        <Detail label="Status" value={<StatusBadge status={item.status} />} />
        <Detail label="Remote Path" value={displayPath(item, "remote")} />
        <Detail label="Destination Path" value={displayPath(item, "local")} />
        <Detail
          label="Progress"
          value={`${formatBytes(item.bytesDownloaded)} / ${formatBytes(item.fileSizeBytes)}`}
        />
        <Detail label="Speed" value={formatRate(item.currentBytesPerSecond)} />
        <Detail label="Retry Count" value={String(item.retryCount)} />
        <Detail label="Queued" value={formatLocalDateTime(item.queuedAt)} />
        <Detail label="Started" value={formatLocalDateTime(item.startedAt)} />
        <Detail
          label="Completed"
          value={formatLocalDateTime(item.completedAt)}
        />
        {item.handledReason && (
          <Detail label="Handled Reason" value={item.handledReason} />
        )}
        {item.errorMessage && (
          <Detail label="Error" value={item.errorMessage} />
        )}
      </dl>
    </aside>
  );
}

function Detail({ label, value }: { label: string; value: ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs uppercase text-muted-foreground">{label}</dt>
      <dd className="mt-1 break-words font-medium">{value}</dd>
    </div>
  );
}

function ToastStack({ toasts }: { toasts: Toast[] }) {
  return (
    <div className="fixed bottom-4 right-4 z-50 grid w-[min(360px,calc(100vw-2rem))] gap-2">
      {toasts.map((toast) => (
        <div
          key={toast.id}
          className="flex items-center gap-3 rounded-lg border border-border bg-panel p-3 text-sm shadow-lg"
        >
          {toast.tone === "success" ? (
            <CheckCircle2 size={18} className="text-emerald-500" />
          ) : (
            <FileWarning size={18} className="text-red-500" />
          )}
          <span>{toast.message}</span>
        </div>
      ))}
    </div>
  );
}

function Progress({
  value,
  total,
  compact = false,
}: {
  value: number;
  total: number;
  compact?: boolean;
}) {
  const percent =
    total > 0 ? Math.min(100, Math.round((value / total) * 100)) : 0;
  return (
    <div className={compact ? "min-w-[150px]" : "mt-3"}>
      <div className="h-2 overflow-hidden rounded-full bg-muted">
        <div
          className="h-full rounded-full bg-accent"
          style={{ width: `${percent}%` }}
        />
      </div>
      <p className="mt-1 text-xs text-muted-foreground">
        {formatBytes(value)} / {formatBytes(total)}
      </p>
    </div>
  );
}

function SkeletonRows() {
  return (
    <div className="space-y-3 p-4">
      <Loader2 className="animate-spin text-muted-foreground" size={18} />
      <div className="h-4 w-3/5 animate-pulse rounded bg-muted" />
      <div className="h-4 w-4/5 animate-pulse rounded bg-muted" />
      <div className="h-4 w-2/5 animate-pulse rounded bg-muted" />
    </div>
  );
}

function EmptyState({ message }: { message: string }) {
  return <div className="p-6 text-sm text-muted-foreground">{message}</div>;
}

function formatEta(item: DownloadQueueItem) {
  const remaining = Math.max(0, item.fileSizeBytes - item.bytesDownloaded);
  if (!item.currentBytesPerSecond || remaining === 0) {
    return remaining === 0 ? "Done" : "Unknown";
  }

  const seconds = Math.ceil(remaining / item.currentBytesPerSecond);
  if (seconds < 60) return `${seconds}s`;
  if (seconds < 3600) return `${Math.ceil(seconds / 60)}m`;
  return `${Math.ceil(seconds / 3600)}h`;
}

function displayName(item: DownloadQueueItem) {
  return basename(displayPath(item, "remote"));
}

function displayPath(item: DownloadQueueItem, target: "local" | "remote") {
  const path = target === "local" ? item.destinationPath : item.remotePath;
  return path ?? item.groupRemotePath ?? "Unknown path";
}

function basename(path: string) {
  return path.split(/[\\/]/).pop() || path;
}

function capitalize(value: string) {
  return value.charAt(0).toUpperCase() + value.slice(1);
}

function toggleValue(values: string[], value: string) {
  return values.includes(value)
    ? values.filter((item) => item !== value)
    : [...values, value];
}

function useLocalState<T extends string>(key: string, initialValue: T) {
  const [value, setValue] = useState<T>(
    () => (localStorage.getItem(key) as T | null) ?? initialValue,
  );
  useEffect(() => localStorage.setItem(key, value), [key, value]);
  return [value, setValue] as const;
}

function useLocalBooleanState(key: string, initialValue: boolean) {
  const [value, setValue] = useState(() =>
    localStorage.getItem(key)
      ? localStorage.getItem(key) === "true"
      : initialValue,
  );
  useEffect(() => localStorage.setItem(key, String(value)), [key, value]);
  return [value, setValue] as const;
}

function useLocalNumberState(key: string, initialValue: number) {
  const [value, setValue] = useState(() =>
    Number(localStorage.getItem(key) ?? initialValue),
  );
  useEffect(() => localStorage.setItem(key, String(value)), [key, value]);
  return [value, setValue] as const;
}

function useLocalArrayState(key: string) {
  const [value, setValue] = useState<string[]>(() => {
    const stored = localStorage.getItem(key);
    return stored ? (JSON.parse(stored) as string[]) : [];
  });
  useEffect(
    () => localStorage.setItem(key, JSON.stringify(value)),
    [key, value],
  );
  return [value, setValue] as const;
}
