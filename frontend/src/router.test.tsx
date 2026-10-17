import { createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { render, screen } from "@testing-library/react";
import { expect, test } from "vite-plus/test";
import { createAppRouter } from "./router";

test("renders the home page at /", async () => {
  const history = createMemoryHistory({ initialEntries: ["/"] });

  render(<RouterProvider router={createAppRouter(history)} />);

  const heading = await screen.findByRole("heading", { level: 1 });
  expect(heading.textContent).toBe("SporeSync");
});
