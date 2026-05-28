export const queryKeys = {
  status: ["status"] as const,
  runs: (query: unknown) => ["runs", query] as const,
  run: (id: string) => ["runs", id] as const,
  queueItems: (runId: string, query: unknown) => ["runs", runId, "queue-items", query] as const,
  jobs: ["jobs"] as const,
  profiles: ["profiles"] as const,
  systemProperty: (propertyName: string) => ["system-properties", propertyName] as const
};
