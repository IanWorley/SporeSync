import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError, api, shouldRedirectToLogin } from "./client";
import type { AuthSession } from "./types";

const session: AuthSession = {
  authRequired: true,
  authenticated: true,
  username: "admin",
};

function jsonResponse(body: unknown, status = 200) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json" },
  });
}

afterEach(() => {
  vi.unstubAllGlobals();
});

describe("auth api client", () => {
  it("posts credentials to the login endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(session));
    vi.stubGlobal("fetch", fetchMock);

    const result = await api.login("admin", "s3cret");

    expect(result).toEqual(session);
    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/auth/login");
    expect(init.method).toBe("POST");
    expect(JSON.parse(init.body)).toEqual({
      username: "admin",
      password: "s3cret",
    });
  });

  it("throws an ApiError with status for failed logins", async () => {
    vi.stubGlobal(
      "fetch",
      vi
        .fn()
        .mockResolvedValue(
          jsonResponse({ message: "Invalid username or password." }, 401),
        ),
    );

    const error = await api.login("admin", "wrong").catch((e) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error.status).toBe(401);
  });

  it("fetches the current session", async () => {
    const fetchMock = vi.fn().mockResolvedValue(jsonResponse(session));
    vi.stubGlobal("fetch", fetchMock);

    const result = await api.session();

    expect(result).toEqual(session);
    expect(fetchMock.mock.calls[0][0]).toBe("/api/auth/session");
  });

  it("posts to the logout endpoint", async () => {
    const fetchMock = vi.fn().mockResolvedValue(
      jsonResponse({
        authRequired: true,
        authenticated: false,
        username: null,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    await api.logout();

    const [path, init] = fetchMock.mock.calls[0];
    expect(path).toBe("/api/auth/logout");
    expect(init.method).toBe("POST");
  });
});

describe("shouldRedirectToLogin", () => {
  it("redirects for 401 responses from protected endpoints", () => {
    expect(shouldRedirectToLogin(401, "/api/status")).toBe(true);
    expect(shouldRedirectToLogin(401, "/api/sftp-sync-jobs")).toBe(true);
  });

  it("does not redirect for auth endpoints or other statuses", () => {
    expect(shouldRedirectToLogin(401, "/api/auth/login")).toBe(false);
    expect(shouldRedirectToLogin(401, "/api/auth/session")).toBe(false);
    expect(shouldRedirectToLogin(403, "/api/status")).toBe(false);
    expect(shouldRedirectToLogin(500, "/api/status")).toBe(false);
  });
});
