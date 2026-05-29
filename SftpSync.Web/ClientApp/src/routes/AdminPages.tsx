import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import {
  AlertCircle,
  Check,
  CircleSlash,
  ExternalLink,
  KeyRound,
  Pencil,
  Plus,
  Save,
  Server,
  Settings2,
  X,
} from "lucide-react";
import type { ReactNode } from "react";
import { useMemo, useState } from "react";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import type {
  SftpConnectionProfile,
  SftpSyncJob,
  UpsertSftpConnectionProfile,
  UpsertSftpSyncJob,
} from "../api/types";
import { Button } from "../components/Button";
import { SectionHeader } from "../components/SectionHeader";
import { formatLocalDateTime } from "../lib/format";

type PanelMode = "list" | "create" | "edit";

export function JobsPage() {
  const queryClient = useQueryClient();
  const jobsQuery = useQuery({ queryKey: queryKeys.jobs, queryFn: api.jobs });
  const profilesQuery = useQuery({
    queryKey: queryKeys.profiles,
    queryFn: api.profiles,
  });
  const [mode, setMode] = useState<PanelMode>("list");
  const [editingJob, setEditingJob] = useState<SftpSyncJob | undefined>();

  const mutation = useMutation({
    mutationFn: ({
      id,
      request,
    }: {
      id?: string;
      request: UpsertSftpSyncJob;
    }) => (id ? api.updateJob(id, request) : api.createJob(request)),
    onSuccess: async () => {
      await queryClient.invalidateQueries({ queryKey: queryKeys.jobs });
      setMode("list");
      setEditingJob(undefined);
    },
  });

  const openEdit = (job: SftpSyncJob) => {
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
              value={localStorage.getItem("sftpsync:theme") ?? "system"}
            />
            <InfoRow
              label="Sidebar"
              value={localStorage.getItem("sftpsync:sidebar") ?? "expanded"}
            />
            <InfoRow
              label="Queue Mode"
              value={
                localStorage.getItem("sftpsync:queue-compact") === "false"
                  ? "detailed"
                  : "compact"
              }
            />
            <InfoRow
              label="Queue Page Size"
              value={localStorage.getItem("sftpsync:queue-page-size") ?? "25"}
            />
          </dl>
        </div>
      </section>
    </div>
  );
}

export function LogsPage() {
  return (
    <div className="space-y-4">
      <SectionHeader
        title="Logs"
        description="Placeholder route reserved for the LogAppended SignalR contract."
      />
      <section className="rounded-lg border border-border bg-panel p-4 font-mono text-sm text-muted-foreground">
        <p>No persisted log stream is configured yet.</p>
        <p className="mt-2">Live contract: LogAppended</p>
      </section>
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
  job?: SftpSyncJob;
  profiles: SftpConnectionProfile[];
  isSaving: boolean;
  error: Error | null;
  onCancel: () => void;
  onSubmit: (request: UpsertSftpSyncJob) => void;
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
  const [isDefault, setIsDefault] = useState(profile?.isDefault ?? true);
  const hasExistingSecret = Boolean(
    profile?.hasPassword || profile?.hasPrivateKey,
  );
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
  const message = error instanceof Error ? error.message : "Request failed.";
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
