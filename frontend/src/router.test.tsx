import { createMemoryHistory, RouterProvider } from "@tanstack/react-router";
import { fireEvent, render, screen } from "@testing-library/react";
import { expect, test } from "vite-plus/test";
import { createAppRouter } from "./router";

const PAGES = [
  { path: "/", link: "Files", title: "SporeSync" },
  { path: "/settings", link: "Settings", title: "Settings" },
  { path: "/autoqueue", link: "Autoqueue", title: "Autoqueue" },
  { path: "/about", link: "About", title: "About" },
];

function renderAt(path: string) {
  const history = createMemoryHistory({ initialEntries: [path] });
  render(<RouterProvider router={createAppRouter(history)} />);
}

test.each(PAGES)("renders the $title page at $path", async ({ path, title }) => {
  renderAt(path);

  const heading = await screen.findByRole("heading", { level: 1 });
  expect(heading.textContent).toBe(title);
});

test("each nav link opens its page", async () => {
  renderAt("/about");
  await screen.findByRole("heading", { level: 1, name: "About" });

  for (const { link, title } of PAGES) {
    fireEvent.click(screen.getByRole("link", { name: link }));
    expect(await screen.findByRole("heading", { level: 1, name: title })).toBeTruthy();
  }
});
