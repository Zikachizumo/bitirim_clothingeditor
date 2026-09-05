import { useEffect, useState } from 'react';
import { call } from '../host/bridge';
import { useApp } from '../state/store';
import { Badge, Button, Spinner } from '../ui/primitives';
import './screens.css';

interface RecentEntry {
  path: string;
  name: string;
  exists: boolean;
  modified: string | null;
}

export function Home() {
  const info = useApp((s) => s.info);
  const caps = useApp((s) => s.caps);
  const setScreen = useApp((s) => s.setScreen);
  const setModal = useApp((s) => s.setModal);
  const openProject = useApp((s) => s.openProject);
  const scanLibrary = useApp((s) => s.scanLibrary);

  const [recent, setRecent] = useState<RecentEntry[] | null>(null);

  useEffect(() => {
    call<RecentEntry[]>('project.recent')
      .then(setRecent)
      .catch(() => setRecent([]));
  }, []);

  return (
    <div className="home">
      <div className="home-hero">
        <div className="home-brand">
          <div className="home-title">BITIRIM</div>
          <div className="home-subtitle">CLOTHING CREATOR</div>
          <div className="home-tagline">GTA V / FiveM clothing development studio</div>
        </div>

        <div className="home-badges">
          <Badge kind="neutral">v{info?.version ?? '—'}</Badge>
          {info?.portable && <Badge kind="neutral">Portable</Badge>}
          {caps?.available
            ? <Badge kind="ok" title={`${caps.backend} ${caps.backendVersion}`}>Asset engine ready</Badge>
            : <Badge kind="error" title={caps?.reason ?? undefined}>Asset engine unavailable</Badge>}
        </div>
      </div>

      <div className="home-columns">
        <div className="home-actions">
          <button className="home-action primary" onClick={() => setScreen('wizard')}>
            <span className="ha-glyph">＋</span>
            <span className="ha-body">
              <span className="ha-title">New Project</span>
              <span className="ha-sub">Start from a drawable or a mock asset</span>
            </span>
          </button>

          <button className="home-action" onClick={() => openProject()}>
            <span className="ha-glyph">📂</span>
            <span className="ha-body">
              <span className="ha-title">Open Project</span>
              <span className="ha-sub">A .bitirimclothing package or project folder</span>
            </span>
          </button>

          <button className="home-action" onClick={() => { void scanLibrary(); setScreen('editor'); }}>
            <span className="ha-glyph">▦</span>
            <span className="ha-body">
              <span className="ha-title">Asset Library</span>
              <span className="ha-sub">Browse drawables on disk</span>
            </span>
          </button>

          <button className="home-action" onClick={() => setModal('settings')}>
            <span className="ha-glyph">⚙</span>
            <span className="ha-body">
              <span className="ha-title">Settings</span>
              <span className="ha-sub">Paths, editor, export, AI</span>
            </span>
          </button>
        </div>

        <div className="home-recent">
          <div className="home-recent-head">Recent projects</div>

          {recent === null && (
            <div className="row" style={{ padding: 12 }}><Spinner /> <span className="dim">Loading…</span></div>
          )}

          {recent?.length === 0 && (
            <div className="home-recent-empty dim">
              Nothing yet. Projects you create or open will appear here.
            </div>
          )}

          {recent?.map((r) => (
            <button
              key={r.path}
              className={`recent-row${r.exists ? '' : ' missing'}`}
              onClick={() => r.exists && openProject(r.path)}
              disabled={!r.exists}
              title={r.exists ? r.path : `Missing: ${r.path}`}
            >
              <span className="recent-name nowrap">{r.name}</span>
              <span className="recent-path nowrap dim">{r.path}</span>
              <span className="recent-date faint">
                {r.exists
                  ? (r.modified ? new Date(r.modified).toLocaleDateString() : '')
                  : 'missing'}
              </span>
            </button>
          ))}
        </div>
      </div>

      <div className="home-footer">
        <span className="faint">
          FiveM export is experimental and has not yet been validated in a running game.
        </span>
        <span className="grow" />
        <Button size="sm" variant="ghost" onClick={() => setModal('about')}>About</Button>
      </div>
    </div>
  );
}
