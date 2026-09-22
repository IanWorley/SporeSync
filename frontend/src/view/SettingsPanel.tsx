import { useSettings } from '../controller/useSettings';

const MIN_PORT = 1;
const MAX_PORT = 65535;
const MIN_TIMEOUT_MILLIS = 1;
const MAX_TIMEOUT_MILLIS = 300000;
const MIN_SCAN_SECONDS = 10;
const MAX_SCAN_SECONDS = 86400;
const INPUT_CLASS =
  'mt-2 w-full rounded-lg border border-slate-700 bg-slate-950 px-3 py-2.5 text-slate-100 focus:border-emerald-400 focus:outline-none';

type TextSetting = 'host' | 'username' | 'source' | 'destination';
const TEXT_FIELDS: { key: TextSetting; label: string; placeholder: string }[] = [
  { key: 'host', label: 'Seedbox hostname', placeholder: 'seedbox.example.com' },
  { key: 'username', label: 'SSH username', placeholder: 'Your seedbox user' },
  { key: 'source', label: 'Remote source directory', placeholder: '/downloads/completed' },
  { key: 'destination', label: 'Local download directory', placeholder: '/srv/sporesync/downloads' },
];

export function SettingsPanel() {
  const { settings, setSettings, message, error, saving, save } = useSettings();

  return (
    <section className="rounded-2xl border border-slate-800 bg-slate-900 p-6 sm:p-8" aria-labelledby="settings-title">
      <h2 id="settings-title" className="text-xl font-semibold">
        Connection & downloads
      </h2>
      <p className="mt-2 text-sm leading-6 text-slate-400">
        Choose where files come from and where they land. Private keys and trusted host keys are configured on the
        backend.
      </p>
      {error && (
        <p role="alert" className="mt-4 text-rose-300">
          {error}
        </p>
      )}
      {!settings ? (
        <p className="mt-6 text-slate-400">{error ? 'Reload to try loading settings again.' : 'Loading settings…'}</p>
      ) : (
        <form onSubmit={(event) => void save(event)} className="mt-6 space-y-6">
          <fieldset disabled={saving} className="grid gap-5 sm:grid-cols-2 disabled:opacity-60">
            <legend className="sr-only">Connection and download settings</legend>
            {TEXT_FIELDS.map(({ key, label, placeholder }) => (
              <label key={key} className="text-sm text-slate-300">
                {label}
                <input
                  className={INPUT_CLASS}
                  required
                  value={settings[key]}
                  placeholder={placeholder}
                  autoComplete="off"
                  onChange={(event) => setSettings({ ...settings, [key]: event.target.value })}
                />
              </label>
            ))}
            <label className="text-sm text-slate-300">
              SSH port
              <input
                className={INPUT_CLASS}
                type="number"
                required
                min={MIN_PORT}
                max={MAX_PORT}
                value={settings.port}
                onChange={(event) => setSettings({ ...settings, port: event.target.valueAsNumber })}
              />
            </label>
            <label className="text-sm text-slate-300">
              Scan interval (seconds)
              <input
                className={INPUT_CLASS}
                type="number"
                required
                min={MIN_SCAN_SECONDS}
                max={MAX_SCAN_SECONDS}
                value={settings.scanSeconds}
                onChange={(event) => setSettings({ ...settings, scanSeconds: event.target.valueAsNumber })}
              />
            </label>
            <label className="text-sm text-slate-300">
              SSH timeout (milliseconds)
              <input className={INPUT_CLASS} type="number" required min={MIN_TIMEOUT_MILLIS} max={MAX_TIMEOUT_MILLIS}
                value={settings.timeoutMillis}
                onChange={(event) => setSettings({ ...settings, timeoutMillis: event.target.valueAsNumber })} />
            </label>
            <label className="flex items-start gap-3 text-sm text-slate-300 sm:col-span-2">
              <input
                className="mt-1 accent-emerald-400"
                type="checkbox"
                checked={settings.automatic}
                onChange={(event) => setSettings({ ...settings, automatic: event.target.checked })}
              />
              <span>
                Download automatically
                <span className="mt-1 block text-slate-500">
                  Queue files after two unchanged scans. Existing queued jobs continue when disabled.
                </span>
              </span>
            </label>
            <label className="flex items-start gap-3 text-sm text-slate-300 sm:col-span-2">
              <input
                className="mt-1 accent-emerald-400"
                type="checkbox"
                checked={settings.temporaryFiles}
                onChange={(event) => setSettings({ ...settings, temporaryFiles: event.target.checked })}
              />
              <span>
                Use temporary files
                <span className="mt-1 block text-slate-500">
                  New files appear at their final path when complete. Existing final files resume in place.
                </span>
              </span>
            </label>
          </fieldset>
          <div className="flex flex-wrap items-center gap-4">
            <button
              disabled={saving}
              className="rounded-lg bg-emerald-400 px-5 py-2.5 text-sm font-semibold text-slate-950 hover:bg-emerald-300 disabled:opacity-50"
            >
              {saving ? 'Saving…' : 'Save settings'}
            </button>
            <p role="status" className="text-sm text-emerald-300">
              {message}
            </p>
          </div>
        </form>
      )}
    </section>
  );
}
