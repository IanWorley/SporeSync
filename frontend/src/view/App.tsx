import { useApplicationStatus } from '../controller/useApplicationStatus';

export function App() {
  const status = useApplicationStatus();

  return (
    <main className="min-h-screen bg-slate-950 px-6 py-16 text-slate-100">
      <section className="mx-auto max-w-xl rounded-2xl border border-slate-800 bg-slate-900 p-8">
        <h1 className="text-3xl font-semibold tracking-tight">SporeSync</h1>
        <p className="mt-4 text-slate-300" role="status">{status}</p>
      </section>
    </main>
  );
}
