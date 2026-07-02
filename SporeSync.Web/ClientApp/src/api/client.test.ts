import { afterEach, describe, expect, it, vi } from "vitest";
import { fetchJson, fetchNoContent, problemDetailsMessage } from "./client";

describe("problemDetailsMessage", () => {
  it("prefers field validation errors", () => {
    const body = JSON.stringify({
      title: "One or more validation errors occurred.",
      status: 400,
      errors: {
        Name: ["The Name field is required."],
        Port: ["Port must be between 1 and 65535."],
      },
    });

    expect(problemDetailsMessage(body)).toBe(
      "The Name field is required. Port must be between 1 and 65535.",
    );
  });

  it("falls back to detail then title", () => {
    expect(
      problemDetailsMessage(
        JSON.stringify({
          title: "Run is not active.",
          detail:
            "Only queued, scanning, or downloading runs can be cancelled.",
        }),
      ),
    ).toBe("Only queued, scanning, or downloading runs can be cancelled.");

    expect(
      problemDetailsMessage(JSON.stringify({ title: "Profile in use." })),
    ).toBe("Profile in use.");
  });

  it("returns undefined for non-JSON bodies", () => {
    expect(problemDetailsMessage("<html>oops</html>")).toBeUndefined();
  });
});

describe("fetch helpers", () => {
  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("throws the ProblemDetails detail on error responses", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(
        async () =>
          new Response(
            JSON.stringify({
              title: "Run already active.",
              detail: "Job already has an active run.",
              status: 409,
            }),
            { status: 409 },
          ),
      ),
    );

    await expect(fetchJson("/api/sftp-sync-jobs/x/run")).rejects.toThrow(
      "Job already has an active run.",
    );
  });

  it("throws a generic message when the error body is empty", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(null, { status: 500 })),
    );

    await expect(fetchNoContent("/api/sftp-sync-jobs/x")).rejects.toThrow(
      "Request failed with 500",
    );
  });

  it("resolves without parsing for 204 responses", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () => new Response(null, { status: 204 })),
    );

    await expect(
      fetchNoContent("/api/sftp-sync-jobs/x"),
    ).resolves.toBeUndefined();
  });
});
