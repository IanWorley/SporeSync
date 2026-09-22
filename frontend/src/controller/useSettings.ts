import { useEffect, useState, type FormEvent } from 'react';
import type { Settings } from '../model/Settings';
import { errorMessage, jsonBody, request } from './api';

export function useSettings() {
  const [settings, setSettings] = useState<Settings | null>(null);
  const [message, setMessage] = useState('');
  const [error, setError] = useState('');
  const [saving, setSaving] = useState(false);

  useEffect(() => {
    const controller = new AbortController();
    request<Settings>('/settings', { signal: controller.signal })
      .then(setSettings)
      .catch((reason: unknown) => {
        if (!controller.signal.aborted) setError(errorMessage(reason));
      });
    return () => controller.abort();
  }, []);

  async function save(event: FormEvent) {
    event.preventDefault();
    if (!settings) return;
    setSaving(true);
    setError('');
    setMessage('');
    try {
      setSettings(await request<Settings>('/settings', { method: 'PUT', ...jsonBody(settings) }));
      setMessage('Settings saved. New jobs use these settings.');
    } catch (reason) {
      setError(errorMessage(reason));
    } finally {
      setSaving(false);
    }
  }

  function editSettings(value: Settings) {
    setSettings(value);
    setMessage('');
    setError('');
  }
  return { settings, setSettings: editSettings, message, error, saving, save };
}
