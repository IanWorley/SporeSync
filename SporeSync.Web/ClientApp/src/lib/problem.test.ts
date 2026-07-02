import { describe, expect, it } from "vitest";
import { extractErrorMessage } from "./problem";

describe("extractErrorMessage", () => {
  it("returns detail from problem-details JSON", () => {
    const error = new Error(
      JSON.stringify({
        title: "Unable to retrieve the host key from example.com:22.",
        detail: "Connection refused",
        status: 502,
      }),
    );

    expect(extractErrorMessage(error)).toBe("Connection refused");
  });

  it("falls back to title when detail is missing", () => {
    const error = new Error(
      JSON.stringify({ title: "Bad Request", status: 400 }),
    );

    expect(extractErrorMessage(error)).toBe("Bad Request");
  });

  it("joins validation errors when present", () => {
    const error = new Error(
      JSON.stringify({
        title: "One or more validation errors occurred.",
        errors: { Host: ["The Host field is required."] },
      }),
    );

    expect(extractErrorMessage(error)).toBe("The Host field is required.");
  });

  it("returns plain text messages unchanged", () => {
    expect(extractErrorMessage(new Error("Request failed with 500"))).toBe(
      "Request failed with 500",
    );
  });

  it("handles non-error values", () => {
    expect(extractErrorMessage(undefined)).toBe("Request failed.");
    expect(extractErrorMessage(new Error(""))).toBe("Request failed.");
  });
});
