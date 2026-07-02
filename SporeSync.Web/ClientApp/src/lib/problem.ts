/**
 * Extracts a human-readable message from an API error. Error bodies may be plain
 * text or RFC 7807 problem-details JSON ({ title, detail, errors }).
 */
export function extractErrorMessage(error: unknown): string {
  if (!(error instanceof Error) || !error.message) {
    return "Request failed.";
  }

  try {
    const parsed = JSON.parse(error.message) as {
      title?: string;
      detail?: string;
      errors?: Record<string, string[]>;
    };
    const validationMessages = Object.values(parsed.errors ?? {}).flat();
    return (
      parsed.detail ||
      validationMessages.join(" ") ||
      parsed.title ||
      error.message
    );
  } catch {
    return error.message;
  }
}
