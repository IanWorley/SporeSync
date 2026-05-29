import { useQuery } from "@tanstack/react-query";
import { Link, Outlet, useRouterState } from "@tanstack/react-router";
import {
  type Activity,
  BriefcaseBusiness,
  ChevronLeft,
  ChevronRight,
  FileClock,
  FileText,
  Info,
  LayoutDashboard,
  Menu,
  MonitorCog,
  Moon,
  PlugZap,
  Server,
  Settings,
  Sun,
} from "lucide-react";
import { useEffect, useMemo, useState } from "react";
import { api } from "../api/client";
import { queryKeys } from "../api/queryKeys";
import { cn } from "../lib/cn";
import { Button } from "./Button";

const navItems = [
  { to: "/dashboard", label: "Dashboard", icon: LayoutDashboard },
  { to: "/runs", label: "Runs", icon: FileClock },
  { to: "/jobs", label: "Jobs", icon: BriefcaseBusiness },
  { to: "/profiles", label: "Profiles", icon: PlugZap },
  { to: "/settings", label: "Settings", icon: Settings },
  { to: "/logs", label: "Logs", icon: FileText },
  { to: "/about", label: "About", icon: Info },
] as const;

type Theme = "system" | "light" | "dark";

function useTheme() {
  const [theme, setTheme] = useState<Theme>(
    () => (localStorage.getItem("sftpsync:theme") as Theme | null) ?? "system",
  );

  useEffect(() => {
    localStorage.setItem("sftpsync:theme", theme);
    const prefersDark = window.matchMedia(
      "(prefers-color-scheme: dark)",
    ).matches;
    document.documentElement.classList.toggle(
      "dark",
      theme === "dark" || (theme === "system" && prefersDark),
    );
  }, [theme]);

  return { theme, setTheme };
}

export function AppShell() {
  const { location } = useRouterState();
  const [collapsed, setCollapsed] = useState(
    () => localStorage.getItem("sftpsync:sidebar") === "collapsed",
  );
  const [drawerOpen, setDrawerOpen] = useState(false);
  const { theme, setTheme } = useTheme();
  const statusQuery = useQuery({
    queryKey: queryKeys.status,
    queryFn: api.status,
    refetchInterval: 30_000,
  });

  useEffect(() => {
    localStorage.setItem(
      "sftpsync:sidebar",
      collapsed ? "collapsed" : "expanded",
    );
  }, [collapsed]);

  const title = useMemo(() => {
    const match = navItems.find((item) =>
      location.pathname.startsWith(item.to),
    );
    return match?.label ?? "Dashboard";
  }, [location.pathname]);

  const themeIcon =
    theme === "dark" ? (
      <Moon size={16} />
    ) : theme === "light" ? (
      <Sun size={16} />
    ) : (
      <MonitorCog size={16} />
    );

  return (
    <div className="min-h-screen bg-background text-foreground">
      <aside
        className={cn(
          "fixed inset-y-0 left-0 z-40 hidden border-r border-border bg-panel md:flex md:flex-col",
          collapsed ? "w-16" : "w-64",
        )}
      >
        <div className="flex h-14 items-center gap-2 border-b border-border px-3">
          <Server className="shrink-0 text-accent" size={22} />
          {!collapsed && (
            <span className="text-sm font-semibold tracking-wide">
              SftpSync
            </span>
          )}
        </div>
        <nav className="flex flex-1 flex-col gap-1 p-2">
          {navItems.map((item) => (
            <NavLink key={item.to} collapsed={collapsed} {...item} />
          ))}
        </nav>
        <div className="border-t border-border p-2">
          <Button
            className="w-full px-2"
            title={collapsed ? "Expand sidebar" : "Collapse sidebar"}
            onClick={() => setCollapsed((value) => !value)}
          >
            {collapsed ? <ChevronRight size={16} /> : <ChevronLeft size={16} />}
            {!collapsed && <span>Collapse</span>}
          </Button>
        </div>
      </aside>

      <div className={cn("min-h-screen", collapsed ? "md:pl-16" : "md:pl-64")}>
        <header className="sticky top-0 z-30 flex h-14 items-center justify-between border-b border-border bg-panel/95 px-4 backdrop-blur">
          <div className="flex min-w-0 items-center gap-3">
            <Button
              className="md:hidden"
              title="Open navigation"
              onClick={() => setDrawerOpen(true)}
            >
              <Menu size={18} />
            </Button>
            <div>
              <h1 className="truncate text-base font-semibold">{title}</h1>
              <div className="mt-0.5 flex items-center gap-2 text-xs text-muted-foreground">
                <span
                  className={cn(
                    "h-2 w-2 rounded-full",
                    statusQuery.data?.databaseAvailable
                      ? "bg-emerald-500"
                      : "bg-amber-500",
                  )}
                />
                <span>{statusQuery.data?.environment ?? "Loading"}</span>
              </div>
            </div>
          </div>
          <Button
            title="Toggle theme"
            onClick={() =>
              setTheme(
                theme === "system"
                  ? "light"
                  : theme === "light"
                    ? "dark"
                    : "system",
              )
            }
          >
            {themeIcon}
            <span className="hidden sm:inline">{theme}</span>
          </Button>
        </header>

        <main className="mx-auto w-full max-w-7xl px-4 py-5">
          <Outlet />
        </main>
      </div>

      {drawerOpen && (
        <div className="fixed inset-0 z-50 md:hidden">
          <button
            type="button"
            className="absolute inset-0 bg-black/45"
            aria-label="Close navigation"
            onClick={() => setDrawerOpen(false)}
          />
          <div className="absolute inset-y-0 left-0 flex w-72 flex-col border-r border-border bg-panel">
            <div className="flex h-14 items-center gap-2 border-b border-border px-4">
              <Server className="text-accent" size={22} />
              <span className="text-sm font-semibold">SftpSync</span>
            </div>
            <nav className="flex flex-col gap-1 p-2">
              {navItems.map((item) => (
                <NavLink
                  key={item.to}
                  collapsed={false}
                  onNavigate={() => setDrawerOpen(false)}
                  {...item}
                />
              ))}
            </nav>
          </div>
        </div>
      )}
    </div>
  );
}

function NavLink({
  to,
  label,
  icon: Icon,
  collapsed,
  onNavigate,
}: {
  to: string;
  label: string;
  icon: typeof Activity;
  collapsed: boolean;
  onNavigate?: () => void;
}) {
  return (
    <Link
      to={to}
      title={collapsed ? label : undefined}
      onClick={onNavigate}
      className="flex h-10 items-center gap-3 rounded-md px-3 text-sm text-muted-foreground transition hover:bg-muted hover:text-foreground [&.active]:bg-muted [&.active]:font-semibold [&.active]:text-foreground"
      activeProps={{ className: "active" }}
    >
      <Icon size={18} className="shrink-0" />
      {!collapsed && <span className="truncate">{label}</span>}
    </Link>
  );
}
