import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertCircle,
  Check,
  CircleSlash,
  ExternalLink,
  Fingerprint,
  KeyRound,
  Pencil,
  Plus,
  Save,
  Server,
  Settings2,
  ShieldCheck,
  X,
} from "lucide-react";
import type { ReactNode } from "react";
import { useMemo, useState } from "react";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import type {
  SftpConnectionProfile,
  SporeSyncJob,
  UpsertSftpConnectionProfile,
  UpsertSporeSyncJob,
} from "../api/types";
import { Button } from "../components/Button";
import { SectionHeader } from "../components/SectionHeader";
import { formatLocalDateTime } from "../lib/format";
import { extractErrorMessage } from "../lib/problem";

type PanelMode = "list" | "create" | "edit";

export function JobsPage() {
  const queryClient = useQueryClient();
  const jobsQuery = useQuery({ queryKey: queryKeys.jobs, queryFn: api.jobs });
  const profilesQuery = useQuery({
    queryKey: queryKeys.profiles,
    queryFn: api.profiles,
  });
  const [mode, setMode] = useState<PanelMode>("list");
  const [editingJob, setEditingJob] = useState<SporeSyncJob | undefined>();

  const mutation = useMutation({
    mutationFn: ({
      id,
      request,
    }: {
      id?: string;
      request: UpsertSporeSyncJob;
    }) => (id ? api.updateJob(id, request) : api.createJob(request)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobs });
      setMode("list");
      setEditingJob(undefined);
    },
  });

  const openEdit = (job: SporeSyncJob) => {
    setEditingJob(job);
    setMode("edit");
  };

  return (
    <div className="space-y-4">
      <SectionHeader
        title="Jobs"
        description="Create and update sync jobs. Paths are typed manually until browsing APIs exist."
        actions={
          <Button
            onClick={() => {
              setEditingJob(undefined);
              setMode("create");
            }}
          >
            <Plus size={16} />
            New Job
          </Button>
        }
      />

      {mode !== "list" && (
        <JobForm
          job={editingJob}
          profiles={profilesQuery.data ?? []}
          isSaving={mutation.isPending}
          error={mutation.error}
          onCancel={() => {
            setMode("list");
            setEditingJob(undefined);
          }}
          onSubmit={(request) =>
            mutation.mutate({ id: editingJob?.id, request })
          }
        />
      )}

      <section className="overflow-hidden rounded-lg border border-border bg-panel">
        <div className="grid grid-cols-[minmax(0,1fr)_120px_90px] border-b border-border bg-muted px-4 py-3 text-xs font-semibold uppercase text-muted-foreground">
          <span>Job</span>
          <span>Interval</span>
          <span className="text-right">Actions</span>
        </div>
        <div className="divide-y divide-border">
          {jobsQuery.isLoading && <SkeletonRows />}
          {jobsQuery.data?.map((job) => (
            <div
              key={job.id}
              className="grid grid-cols-[minmax(0,1fr)_120px_90px] items-center gap-3 px-4 py-3"
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium">{job.name}</p>
                  <StatusPill enabled={job.isEnabled} />
                </div>
                <p className="mt-1 truncate text-xs text-muted-foreground">
                  {job.sourcePath} -&gt; {job.destinationPath}
                </p>
              </div>
              <span className="text-sm text-muted-foreground">
                {job.pollingIntervalSeconds}s
              </span>
              <div className="flex justify-end">
                <Button
                  title={`Edit ${job.name}`}
                  onClick={() => openEdit(job)}
                >
                  <Pencil size={15} />
                </Button>
              </div>
            </div>
          ))}
          {jobsQuery.data?.length === 0 && (
            <EmptyState
              title="No jobs configured"
              detail="Create a job once a connection profile exists."
            />
          )}
        </div>
      </section>
    </div>
  );
}

export function ProfilesPage() {
  const queryClient = useQueryClient();
  const profilesQuery = useQuery({
    queryKey: queryKeys.profiles,
    queryFn: api.profiles,
  });
  const [mode, setMode] = useState<PanelMode>("list");
  const [editingProfile, setEditingProfile] = useState<
    SftpConnectionProfile | undefined
  >();

  const mutation = useMutation({
    mutationFn: ({
      id,
      request,
    }: {
      id?: string;
      request: UpsertSftpConnectionProfile;
    }) => (id ? api.updateProfile(id, request) : api.createProfile(request)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.profiles });
      setMode("list");
      setEditingProfile(undefined);
    },
  });

  return (
    <div className="space-y-4">
      <SectionHeader
        title="Profiles"
        description="Manage SFTP hosts and write-only secret replacement fields."
        actions={
          <Button
            onClick={() => {
              setEditingProfile(undefined);
              setMode("create");
            }}
          >
            <Plus size={16} />
            New Profile
          </Button>
        }
      />

      {mode !== "list" && (
        <ProfileForm
          profile={editingProfile}
          isSaving={mutation.isPending}
          error={mutation.error}
          onCancel={() => {
            setMode("list");
            setEditingProfile(undefined);
          }}
          onSubmit={(request) =>
            mutation.mutate({ id: editingProfile?.id, request })
          }
        />
      )}

      <section className="overflow-hidden rounded-lg border border-border bg-panel">
        <div className="grid grid-cols-[minmax(0,1fr)_170px_90px] border-b border-border bg-muted px-4 py-3 text-xs font-semibold uppercase text-muted-foreground">
          <span>Profile</span>
          <span>Secrets</span>
          <span className="text-right">Actions</span>
        </div>
        <div className="divide-y divide-border">
          {profilesQuery.isLoading && <SkeletonRows />}
          {profilesQuery.data?.map((profile) => (
            <div
              key={profile.id}
              className="grid grid-cols-[minmax(0,1fr)_170px_90px] items-center gap-3 px-4 py-3"
            >
              <div className="min-w-0">
                <div className="flex items-center gap-2">
                  <p className="truncate text-sm font-medium">{profile.name}</p>
                  {profile.isDefault && (
                    <span className="rounded-full bg-muted px-2 py-0.5 text-xs font-medium">
                      Default
                    </span>
                  )}
                </div>
                <p className="mt-1 truncate text-xs text-muted-foreground">
                  {profile.username}@{profile.host}:{profile.port}
                </p>
              </div>
              <SecretIndicators profile={profile} />
              <div className="flex justify-end">
                <Button
                  title={`Edit ${profile.name}`}
                  onClick={() => {
                    setEditingProfile(profile);
                    setMode("edit");
                  }}
                >
                  <Pencil size={15} />
                </Button>
              </div>
            </div>
          ))}
          {profilesQuery.data?.length === 0 && (
            <EmptyState
              title="No profiles configured"
              detail="Add a profile before creating sync jobs."
            />
          )}
        </div>
      </section>
    </div>
  );
}

export function SettingsPage() {
  return (
    <div className="space-y-4">
      <SectionHeader title="Settings" description="Local UI preferences." />
      <section className="grid gap-4 lg:grid-cols-2">
        <div className="rounded-lg border border-border bg-panel p-4">
          <h3 className="flex items-center gap-2 text-sm font-semibold">
            <Settings2 size={16} />
            Local Preferences
          </h3>
          <dl className="mt-4 grid gap-3 text-sm">
            <InfoRow
              label="Theme"
              value={localStorage.getItem("sporesync:theme") ?? "system"}
            />
            <InfoRow
              label="Sidebar"
              value={localStorage.getItem("sporesync:sidebar") ?? "expanded"}
            />
            <InfoRow
              label="Queue Mode"
              value={
                localStorage.getItem("sporesync:queue-compact") === "false"
                  ? "detailed"
                  : "compact"
              }
            />
            <InfoRow
              label="Queue Page Size"
              value={localStorage.getItem("sporesync:queue-page-size") ?? "25"}
            />
          </dl>
        </div>
      </section>
    </div>
  );
}

export function LogsPage() {
  const queryClient = useQueryClient();
  const [selectedLevel, setSelectedLevel] = useState<
    "debug" | "info" | "warning" | "error"
  >("info");
  const [autoRefresh, setAutoRefresh] = useState(true);

  const levelQuery = useQuery({
    queryKey: ["db-log-level"],
    queryFn: async () => {
      try {
        const res = await api.getDbLogLevel();
        const lvl = (res.propertyValue || "info").toLowerCase() as
          | "debug"
          | "info"
          | "warning"
          | "error";
        setSelectedLevel(
          ["debug", "info", "warning", "error"].includes(lvl) ? lvl : "info",
        );
        return res;
      } catch {
        return null;
      }
    },
  });

  const setLevelMutation = useMutation({
    mutationFn: (value: string) => api.setDbLogLevel(value),
    onSuccess: async (res) => {
      setSelectedLevel(
        res.propertyValue.toLowerCase() as
          | "debug"
          | "info"
          | "warning"
          | "error",
      );
      await queryClient.invalidateQueries({ queryKey: ["db-log-level"] });
    },
  });

  const logsQuery = useQuery({
    queryKey: ["db-logs", selectedLevel],
    queryFn: () => api.getDbLogs(selectedLevel, 150),
    refetchInterval: autoRefresh ? 5000 : false,
  });

  const handleLevelChange = (newLevel: string) => {
    setSelectedLevel(newLevel as "debug" | "info" | "warning" | "error");
    setLevelMutation.mutate(newLevel);
  };

  return (
    <div className="space-y-4">
      <SectionHeader
        title="DB Call Logs"
        description="Recent database repository calls. Level is controlled by the db_log_level system property (default: info)."
        actions={
          <div className="flex items-center gap-3">
            <label className="flex items-center gap-2 text-sm">
              <input
                type="checkbox"
                checked={autoRefresh}
                onChange={(e) => setAutoRefresh(e.target.checked)}
              />
              Auto-refresh (5s)
            </label>
            <select
              value={selectedLevel}
              onChange={(e) => handleLevelChange(e.target.value)}
              className="rounded border border-border bg-panel px-3 py-1.5 text-sm"
              disabled={setLevelMutation.isPending}
            >
              <option value="debug">Debug</option>
              <option value="info">Info</option>
              <option value="warning">Warning</option>
              <option value="error">Error</option>
            </select>
          </div>
        }
      />

      <div className="overflow-hidden rounded-lg border border-border bg-panel">
        <div className="grid grid-cols-[180px_80px_120px_1fr_1fr] border-b border-border bg-muted px-4 py-2 text-xs font-semibold uppercase text-muted-foreground">
          <span>Time</span>
          <span>Level</span>
          <span>Duration</span>
          <span>Operation</span>
          <span>Params / Error</span>
        </div>
        <div className="max-h-[520px] overflow-auto text-sm">
          {logsQuery.data?.items?.length ? (
            logsQuery.data.items.map((entry) => (
              <div
                key={entry.timestamp}
                className="grid grid-cols-[180px_80px_120px_1fr_1fr] border-b border-border px-4 py-2 even:bg-muted/40"
              >
                <span className="font-mono text-xs text-muted-foreground">
                  {new Date(entry.timestamp).toLocaleTimeString()}
                </span>
                <span>
                  <span
                    className={
                      entry.level === "error"
                        ? "rounded bg-red-500/10 px-1.5 py-0.5 text-xs text-red-600"
                        : entry.level === "warning"
                          ? "rounded bg-amber-500/10 px-1.5 py-0.5 text-xs text-amber-600"
                          : "rounded bg-sky-500/10 px-1.5 py-0.5 text-xs text-sky-600"
                    }
                  >
                    {entry.level}
                  </span>
                </span>
                <span className="font-mono text-xs">{entry.durationMs} ms</span>
                <span className="font-medium">{entry.operation}</span>
                <span className="truncate text-xs text-muted-foreground">
                  {entry.exceptionMessage ? (
                    <span className="text-red-600">
                      {entry.exceptionMessage}
                    </span>
                  ) : (
                    entry.paramNames || "—"
                  )}
                </span>
              </div>
            ))
          ) : (
            <div className="px-4 py-8 text-center text-muted-foreground">
              No DB calls logged yet at this level.
            </div>
          )}
        </div>
      </div>

      <p className="text-xs text-muted-foreground">
        SQL text is logged at Debug level. Current effective level:{" "}
        <code>
          {logsQuery.data?.currentLevel ??
            levelQuery.data?.propertyValue ??
            "info"}
        </code>
        . Changes take effect immediately for new calls.
      </p>
    </div>
  );
}

export function AboutPage() {
  const statusQuery = useQuery({
    queryKey: queryKeys.status,
    queryFn: api.status,
    refetchInterval: 30_000,
  });
  const docsHref = "/scalar/v1";
  const openApiHref = "/openapi/v1.json";

  return (
    <div className="space-y-4">
      <SectionHeader
        title="About"
        description="Runtime status and development API references."
      />
      <section className="grid gap-4 lg:grid-cols-2">
        <div className="rounded-lg border border-border bg-panel p-4">
          <h3 className="flex items-center gap-2 text-sm font-semibold">
            <Server size={16} />
            Runtime
          </h3>
          <dl className="mt-4 grid gap-3 text-sm">
            <InfoRow
              label="API Status"
              value={statusQuery.data?.status ?? "Loading"}
            />
            <InfoRow
              label="Environment"
              value={statusQuery.data?.environment ?? "Loading"}
            />
            <InfoRow
              label="Database"
              value={
                statusQuery.data?.databaseAvailable
                  ? "Available"
                  : "Unavailable"
              }
            />
            <InfoRow
              label="Encryption Key"
              value={
                statusQuery.data?.encryptionKeyInitialized
                  ? "Initialized"
                  : "Unavailable"
              }
            />
            <InfoRow
              label="Encryption Version"
              value={statusQuery.data?.encryptionKeyVersion ?? "Loading"}
            />
            <InfoRow
              label="Backend Time"
              value={formatLocalDateTime(statusQuery.data?.currentTime)}
            />
          </dl>
        </div>
        <div className="rounded-lg border border-border bg-panel p-4">
          <h3 className="text-sm font-semibold">Development Links</h3>
          <div className="mt-4 flex flex-wrap gap-2">
            <a
              className={linkButtonClass}
              href={docsHref}
              target="_blank"
              rel="noreferrer"
            >
              API Docs <ExternalLink size={15} />
            </a>
            <a
              className={linkButtonClass}
              href={openApiHref}
              target="_blank"
              rel="noreferrer"
            >
              OpenAPI <ExternalLink size={15} />
            </a>
          </div>
          {statusQuery.data?.environment !== "Development" && (
            <p className="mt-3 text-xs text-muted-foreground">
              Development API docs are only mapped by the backend in
              Development.
            </p>
          )}
        </div>
      </section>
    </div>
  );
}

function JobForm({
  job,
  profiles,
  isSaving,
  error,
  onCancel,
  onSubmit,
}: {
  job?: SporeSyncJob;
  profiles: SftpConnectionProfile[];
  isSaving: boolean;
  error: Error | null;
  onCancel: () => void;
  onSubmit: (request: UpsertSporeSyncJob) => void;
}) {
  const [name, setName] = useState(job?.name ?? "");
  const [connectionProfileId, setConnectionProfileId] = useState(
    job?.connectionProfileId ?? profiles[0]?.id ?? "",
  );
  const [sourcePath, setSourcePath] = useState(job?.sourcePath ?? "");
  const [destinationPath, setDestinationPath] = useState(
    job?.destinationPath ?? "",
  );
  const [pollingIntervalSeconds, setPollingIntervalSeconds] = useState(
    job?.pollingIntervalSeconds ?? 120,
  );
  const [isEnabled, setIsEnabled] = useState(job?.isEnabled ?? true);
  const validation = useMemo(() => {
    if (!name.trim()) return "Name is required.";
    if (!connectionProfileId) return "Connection profile is required.";
    if (!sourcePath.trim()) return "Source path is required.";
    if (!destinationPath.trim()) return "Destination path is required.";
    if (pollingIntervalSeconds < 30)
      return "Polling interval must be at least 30 seconds.";
    return undefined;
  }, [
    connectionProfileId,
    destinationPath,
    name,
    pollingIntervalSeconds,
    sourcePath,
  ]);

  return (
    <form
      className="rounded-lg border border-border bg-panel p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!validation) {
          onSubmit({
            connectionProfileId,
            name,
            sourcePath,
            destinationPath,
            pollingIntervalSeconds,
            isEnabled,
          });
        }
      }}
    >
      <FormTitle title={job ? "Edit Job" : "New Job"} onCancel={onCancel} />
      <div className="mt-4 grid gap-3 md:grid-cols-2">
        <Field label="Name">
          <input
            className={inputClass}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label="Connection profile">
          <select
            className={inputClass}
            value={connectionProfileId}
            onChange={(event) => setConnectionProfileId(event.target.value)}
          >
            <option value="">Select profile</option>
            {profiles.map((profile) => (
              <option key={profile.id} value={profile.id}>
                {profile.name}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Remote source path">
          <input
            className={inputClass}
            value={sourcePath}
            onChange={(event) => setSourcePath(event.target.value)}
          />
        </Field>
        <Field label="Local destination path">
          <input
            className={inputClass}
            value={destinationPath}
            onChange={(event) => setDestinationPath(event.target.value)}
          />
        </Field>
        <Field label="Polling interval seconds">
          <input
            className={inputClass}
            min={30}
            type="number"
            value={pollingIntervalSeconds}
            onChange={(event) =>
              setPollingIntervalSeconds(Number(event.target.value))
            }
          />
        </Field>
        <label className="flex items-center gap-2 pt-6 text-sm">
          <input
            checked={isEnabled}
            type="checkbox"
            onChange={(event) => setIsEnabled(event.target.checked)}
          />
          Enabled
        </label>
      </div>
      <FormFooter validation={validation} error={error} isSaving={isSaving} />
    </form>
  );
}

function ProfileForm({
  profile,
  isSaving,
  error,
  onCancel,
  onSubmit,
}: {
  profile?: SftpConnectionProfile;
  isSaving: boolean;
  error: Error | null;
  onCancel: () => void;
  onSubmit: (request: UpsertSftpConnectionProfile) => void;
}) {
  const [name, setName] = useState(profile?.name ?? "");
  const [host, setHost] = useState(profile?.host ?? "");
  const [port, setPort] = useState(profile?.port ?? 22);
  const [username, setUsername] = useState(profile?.username ?? "");
  const [password, setPassword] = useState("");
  const [privateKey, setPrivateKey] = useState("");
  const [privateKeyPassphrase, setPrivateKeyPassphrase] = useState("");
  const [hostKeyFingerprint, setHostKeyFingerprint] = useState(
    profile?.hostKeyFingerprintSha256 ?? "",
  );
  const [isDefault, setIsDefault] = useState(profile?.isDefault ?? true);
  const hasExistingSecret = Boolean(
    profile?.hasPassword || profile?.hasPrivateKey,
  );
  const scanMutation = useMutation({
    mutationFn: () => api.scanHostKey(host.trim(), port),
    onSuccess: (result) => {
      setHostKeyFingerprint(result.fingerprintSha256);
    },
  });
  const validation = useMemo(() => {
    if (!name.trim()) return "Name is required.";
    if (!host.trim()) return "Host is required.";
    if (port < 1 || port > 65535) return "Port must be between 1 and 65535.";
    if (!username.trim()) return "Username is required.";
    if (!hasExistingSecret && !password.trim() && !privateKey.trim())
      return "Password or private key is required.";
    return undefined;
  }, [hasExistingSecret, host, name, password, port, privateKey, username]);

  return (
    <form
      className="rounded-lg border border-border bg-panel p-4"
      onSubmit={(event) => {
        event.preventDefault();
        if (!validation) {
          onSubmit({
            name,
            host,
            port,
            username,
            password: password.trim() ? password : null,
            privateKey: privateKey.trim() ? privateKey : null,
            privateKeyPassphrase: privateKeyPassphrase.trim()
              ? privateKeyPassphrase
              : null,
            // The form always knows the current pin, so submit it verbatim;
            // an empty value clears the pin and re-enables trust-on-first-use.
            hostKeyFingerprintSha256: hostKeyFingerprint.trim(),
            isDefault,
          });
        }
      }}
    >
      <FormTitle
        title={profile ? "Edit Profile" : "New Profile"}
        onCancel={onCancel}
      />
      <div className="mt-4 grid gap-3 md:grid-cols-2">
        <Field label="Name">
          <input
            className={inputClass}
            value={name}
            onChange={(event) => setName(event.target.value)}
          />
        </Field>
        <Field label="Host">
          <input
            className={inputClass}
            value={host}
            onChange={(event) => setHost(event.target.value)}
          />
        </Field>
        <Field label="Port">
          <input
            className={inputClass}
            min={1}
            max={65535}
            type="number"
            value={port}
            onChange={(event) => setPort(Number(event.target.value))}
          />
        </Field>
        <Field label="Username">
          <input
            className={inputClass}
            value={username}
            onChange={(event) => setUsername(event.target.value)}
          />
        </Field>
        <Field label={profile?.hasPassword ? "Replace password" : "Password"}>
          <input
            className={inputClass}
            type="password"
            value={password}
            onChange={(event) => setPassword(event.target.value)}
          />
        </Field>
        <Field
          label={profile?.hasPrivateKey ? "Replace private key" : "Private key"}
        >
          <textarea
            className={`${inputClass} min-h-24 py-2 font-mono text-xs`}
            value={privateKey}
            onChange={(event) => setPrivateKey(event.target.value)}
          />
        </Field>
        <Field
          label={
            profile?.hasPrivateKeyPassphrase
              ? "Replace key passphrase"
              : "Key passphrase"
          }
        >
          <input
            className={inputClass}
            type="password"
            value={privateKeyPassphrase}
            onChange={(event) => setPrivateKeyPassphrase(event.target.value)}
          />
        </Field>
        <div className="md:col-span-2">
          <Field label="Pinned host key fingerprint (SHA-256)">
            <div className="flex gap-2">
              <input
                className={`${inputClass} font-mono text-xs`}
                placeholder="SHA256:…  (blank = trust and pin on first connection)"
                value={hostKeyFingerprint}
                onChange={(event) => setHostKeyFingerprint(event.target.value)}
              />
              <Button
                type="button"
                disabled={!host.trim() || scanMutation.isPending}
                title="Fetch the host key fingerprint from the server"
                onClick={() => scanMutation.mutate()}
              >
                <Fingerprint size={15} />
                {scanMutation.isPending ? "Scanning…" : "Fetch"}
              </Button>
            </div>
          </Field>
          {scanMutation.data && (
            <p className="mt-2 text-xs text-emerald-600 dark:text-emerald-300">
              Server presented a {scanMutation.data.hostKeyAlgorithm} key (
              {scanMutation.data.keyLength} bit). Verify this fingerprint
              against a trusted source before saving.
            </p>
          )}
          {scanMutation.error && (
            <p className="mt-2 text-xs text-red-600 dark:text-red-300">
              {extractErrorMessage(scanMutation.error)}
            </p>
          )}
          <p className="mt-2 text-xs text-muted-foreground">
            Connections are rejected when the server key does not match the
            pinned fingerprint. Leave blank to pin automatically on first
            connection; clear the field if the server host key legitimately
            changed.
          </p>
        </div>
        <label className="flex items-center gap-2 pt-6 text-sm">
          <input
            checked={isDefault}
            type="checkbox"
            onChange={(event) => setIsDefault(event.target.checked)}
          />
          Default profile
        </label>
      </div>
      {profile && (
        <p className="mt-3 text-xs text-muted-foreground">
          Blank secret fields keep the currently configured secret.
        </p>
      )}
      <FormFooter validation={validation} error={error} isSaving={isSaving} />
    </form>
  );
}

function FormTitle({
  title,
  onCancel,
}: {
  title: string;
  onCancel: () => void;
}) {
  return (
    <div className="flex items-center justify-between gap-3">
      <h3 className="text-sm font-semibold">{title}</h3>
      <Button type="button" title="Close form" onClick={onCancel}>
        <X size={16} />
      </Button>
    </div>
  );
}

function FormFooter({
  validation,
  error,
  isSaving,
}: {
  validation?: string;
  error: Error | null;
  isSaving: boolean;
}) {
  return (
    <div className="mt-4 flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
      <div className="min-h-5">
        {validation && (
          <p className="text-sm text-amber-600 dark:text-amber-300">
            {validation}
          </p>
        )}
        {error && <ErrorMessage error={error} />}
      </div>
      <Button type="submit" disabled={Boolean(validation) || isSaving}>
        <Save size={16} />
        Save
      </Button>
    </div>
  );
}

function Field({ label, children }: { label: string; children: ReactNode }) {
  return (
    // biome-ignore lint/a11y/noLabelWithoutControl: This reusable wrapper nests the form control passed as children.
    <label className="grid gap-1.5 text-sm">
      <span className="font-medium">{label}</span>
      {children}
    </label>
  );
}

function ErrorMessage({ error }: { error: unknown }) {
  const message = extractErrorMessage(error);
  return (
    <p className="flex items-center gap-2 text-sm text-red-600 dark:text-red-300">
      <AlertCircle size={15} />
      {message}
    </p>
  );
}

function SecretIndicators({ profile }: { profile: SftpConnectionProfile }) {
  const items = [
    { label: "Password", enabled: profile.hasPassword },
    { label: "Key", enabled: profile.hasPrivateKey },
    { label: "Passphrase", enabled: profile.hasPrivateKeyPassphrase },
  ];
  const hostKeyPinned = Boolean(profile.hostKeyFingerprintSha256);
  return (
    <div className="flex flex-wrap gap-1">
      {items.map((item) => (
        <span
          key={item.label}
          className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground"
        >
          {item.enabled ? <KeyRound size={12} /> : <CircleSlash size={12} />}
          {item.label}
        </span>
      ))}
      <span
        title={
          hostKeyPinned
            ? `Pinned host key: ${profile.hostKeyFingerprintSha256}`
            : "Host key not pinned yet; it will be pinned on first connection."
        }
        className={
          hostKeyPinned
            ? "inline-flex items-center gap-1 rounded-full bg-emerald-500/10 px-2 py-0.5 text-xs text-emerald-600 dark:text-emerald-300"
            : "inline-flex items-center gap-1 rounded-full bg-amber-500/10 px-2 py-0.5 text-xs text-amber-600 dark:text-amber-300"
        }
      >
        {hostKeyPinned ? <ShieldCheck size={12} /> : <CircleSlash size={12} />}
        Host key
      </span>
    </div>
  );
}

function StatusPill({ enabled }: { enabled: boolean }) {
  return (
    <span className="inline-flex items-center gap-1 rounded-full bg-muted px-2 py-0.5 text-xs text-muted-foreground">
      {enabled ? <Check size={12} /> : <CircleSlash size={12} />}
      {enabled ? "Enabled" : "Disabled"}
    </span>
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

function EmptyState({ title, detail }: { title: string; detail: string }) {
  return (
    <div className="px-4 py-8 text-center">
      <p className="text-sm font-medium">{title}</p>
      <p className="mt-1 text-sm text-muted-foreground">{detail}</p>
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

const inputClass =
  "h-9 w-full rounded-md border border-border bg-background px-3 text-sm outline-none focus:shadow-focus";
const linkButtonClass =
  "inline-flex h-9 items-center justify-center gap-2 rounded-md border border-border bg-panel px-3 text-sm font-medium text-foreground transition hover:bg-muted focus:outline-none focus:shadow-focus";
