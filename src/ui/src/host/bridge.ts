/**
 * Client for the host bridge (protocol `bitirim-host-v1`).
 *
 * Every filesystem, asset and project operation goes through here. The UI holds
 * no business logic and never touches disk on its own.
 */

export interface HostError {
  code: string;
  message: string;
  hint?: string | null;
}

export class HostCallError extends Error {
  readonly code: string;
  readonly hint?: string | null;

  constructor(error: HostError) {
    super(error.message);
    this.name = 'HostCallError';
    this.code = error.code;
    this.hint = error.hint;
  }
}

type Pending = {
  resolve: (value: unknown) => void;
  reject: (reason: HostCallError) => void;
};

type EventHandler = (data: unknown) => void;

declare global {
  interface Window {
    chrome?: { webview?: {
      postMessage(message: string): void;
      addEventListener(type: 'message', listener: (event: { data: string }) => void): void;
    } };
  }
}

const pending = new Map<number, Pending>();
const listeners = new Map<string, Set<EventHandler>>();
let nextId = 1;

const webview = window.chrome?.webview;

/** True when running inside the desktop host rather than a plain browser. */
export const isHosted = Boolean(webview);

if (webview) {
  webview.addEventListener('message', (event) => {
    let payload: any;
    try {
      payload = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
    } catch {
      return;
    }

    if (payload && typeof payload.event === 'string') {
      listeners.get(payload.event)?.forEach((handler) => {
        try {
          handler(payload.data);
        } catch (err) {
          console.error(`Event handler for ${payload.event} failed`, err);
        }
      });
      return;
    }

    const entry = pending.get(payload?.id);
    if (!entry) return;
    pending.delete(payload.id);

    if (payload.ok) entry.resolve(payload.result);
    else entry.reject(new HostCallError(payload.error ?? {
      code: 'internal', message: 'The host returned an error.',
    }));
  });
}

/**
 * Calls a host operation.
 *
 * Rejects with {@link HostCallError}, which always carries a message written
 * for a person. Callers surface `.message` directly; `.hint` adds the "what to
 * do about it" line when there is one.
 */
export function call<T = unknown>(op: string, args: Record<string, unknown> = {}): Promise<T> {
  if (!webview) {
    return Promise.reject(new HostCallError({
      code: 'not_hosted',
      message: 'This build must run inside the Bitirim Clothing Creator desktop application.',
    }));
  }

  const id = nextId++;
  return new Promise<T>((resolve, reject) => {
    pending.set(id, { resolve: resolve as (v: unknown) => void, reject });
    webview.postMessage(JSON.stringify({ id, op, args }));
  });
}

/** Subscribes to a host-pushed event. Returns an unsubscribe function. */
export function on(event: string, handler: EventHandler): () => void {
  let set = listeners.get(event);
  if (!set) {
    set = new Set();
    listeners.set(event, set);
  }
  set.add(handler);
  return () => set!.delete(handler);
}

export const host = {
  info: () => call<AppInfo>('app.info'),
  capabilities: () => call<CapabilityReport>('app.capabilities'),
  components: () => call<ComponentInfo[]>('app.components'),
  ready: () => call<{ startupFile: string | null }>('ui.ready'),
  log: (level: 'info' | 'warn' | 'error', message: string) =>
    call('log.write', { level, message }),
  recentLogs: (max = 300) => call<LogLine[]>('log.recent', { max }),

  window: {
    minimize: () => call('window.minimize'),
    toggleMaximize: () => call('window.toggleMaximize'),
    close: () => call('window.close'),
    setTitle: (title: string | null) => call('window.setTitle', { title }),
    devtools: () => call('devtools.open'),
  },
};

export interface AppInfo {
  name: string;
  version: string;
  protocol: string;
  portable: boolean;
  userDataRoot: string;
  os: string;
  developerMode: boolean;
}

export interface CapabilityReport {
  available: boolean;
  reason: string | null;
  backend?: string;
  backendVersion?: string;
  python?: string;
  contract?: string;
  capabilities: Record<string, boolean>;
}

export interface ComponentInfo {
  index: number;
  prefix: string;
  label: string;
}

export interface LogLine {
  at: string;
  level: string;
  channel: string;
  message: string;
}
