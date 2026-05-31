import { describe, expect, it } from "vitest";
import {
  formatBytes,
  formatLocalDateTime,
  formatRate,
  formatRelativeTime,
} from "./format";

describe("format helpers", () => {
  it("formats bytes with readable units", () => {
    expect(formatBytes(0)).toBe("0 B");
    expect(formatBytes(1024)).toBe("1.0 KB");
    expect(formatBytes(11 * 1024 * 1024)).toBe("11 MB");
  });

  it("formats transfer rate", () => {
    expect(formatRate(2048)).toBe("2.0 KB/s");
  });

  it("formats missing dates consistently", () => {
    expect(formatLocalDateTime(null)).toBe("Not available");
    expect(formatRelativeTime(undefined)).toBe("Not available");
  });

  it("formats relative dates", () => {
    expect(
      formatRelativeTime(
        "2026-05-27T12:00:00Z",
        new Date("2026-05-27T12:05:00Z"),
      ),
    ).toBe("5 minutes ago");
  });
});
