import { useEffect, useRef, useState } from 'react';
import { call, host } from '../host/bridge';
import { activeAsset, activeVariation, useApp } from '../state/store';
import { Badge } from '../ui/primitives';
import './chrome.css';

interface MenuItem {
  label: string;
  action?: () => void;
  shortcut?: string;
  disabled?: boolean;
  separator?: boolean;
}

export function MenuBar() {
  const [open, setOpen] = useState<string | null>(null);
  const barRef = useRef<HTMLDivElement>(null);

  const store = useApp();
  const projectOpen = store.projectState.open;
  const devMode = store.info?.developerMode ?? false;
  const history = store.projectState.history;

  useEffect(() => {
    const away = (e: MouseEvent) => {
      if (barRef.current && !barRef.current.contains(e.target as Node)) setOpen(null);
    };
    window.addEventListener('mousedown', away);
    return () => window.removeEventListener('mousedown', away);
  }, []);

  const menus: Record<string, MenuItem[]> = {
    File: [
      { label: 'New Project', shortcut: 'Ctrl+N', action: () => store.setScreen('wizard') },
      { label: 'Open Project…', shortcut: 'Ctrl+O', action: () => store.openProject() },
      { separator: true, label: '' },
      { label: 'Save', shortcut: 'Ctrl+S', disabled: !projectOpen, action: () => store.saveProject() },
      {
        label: 'Save As Package…', shortcut: 'Ctrl+Shift+S', disabled: !projectOpen,
        action: async () => {
          const next = await call<any>('project.saveAs');
          if (next) { store.refreshProject(next); store.toast('success', 'Project package saved.'); }
        },
      },
      { separator: true, label: '' },
      {
        label: 'Import Asset…', disabled: !projectOpen,
        action: async () => {
          const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind: 'ydd' });
          if (picked?.paths?.length) await store.loadMesh(picked.paths[0]);
        },
      },
      { label: 'Export…', shortcut: 'Ctrl+E', disabled: !projectOpen, action: () => store.setModal('export') },
      { separator: true, label: '' },
      { label: 'Close Project', disabled: !projectOpen, action: () => store.closeProject() },
      { label: 'Exit', action: () => host.window.close() },
    ],
    Edit: [
      {
        label: history?.undoLabel ? `Undo ${history.undoLabel}` : 'Undo',
        shortcut: 'Ctrl+Z',
        disabled: !history?.canUndo,
        action: () => store.undo(),
      },
      {
        label: history?.redoLabel ? `Redo ${history.redoLabel}` : 'Redo',
        shortcut: 'Ctrl+Y',
        disabled: !history?.canRedo,
        action: () => store.redo(),
      },
      { separator: true, label: '' },
      { label: 'Find…', shortcut: 'Ctrl+P', action: () => store.setModal('search') },
      { label: 'Keyboard Shortcuts…', shortcut: 'F1', action: () => store.setModal('shortcuts') },
      { label: 'Preferences…', action: () => store.setModal('settings') },
    ],
    View: [
      { label: '3D Model', shortcut: 'Ctrl+1', action: () => store.setTab('model') },
      { label: 'UV', shortcut: 'Ctrl+2', action: () => store.setTab('uv') },
      { label: 'Texture', shortcut: 'Ctrl+3', disabled: !projectOpen,
        action: () => store.setTab('texture') },
      { label: 'Material', shortcut: 'Ctrl+4', action: () => store.setTab('material') },
      { label: 'Character', shortcut: 'Ctrl+5', action: () => store.setTab('character') },
      { label: 'Neon', shortcut: 'Ctrl+6', disabled: !projectOpen,
        action: () => store.setTab('neon') },
      { label: 'Validation', shortcut: 'Ctrl+7', action: () => store.setTab('validation') },
      ...(devMode ? [{ label: 'Developer', action: () => store.setTab('developer') }] : []),
      { separator: true, label: '' },
      {
        label: store.layout.leftOpen ? 'Hide Left Panel' : 'Show Left Panel',
        action: () => store.patchLayout({ leftOpen: !store.layout.leftOpen }),
      },
      {
        label: store.layout.rightOpen ? 'Hide Inspector' : 'Show Inspector',
        action: () => store.patchLayout({ rightOpen: !store.layout.rightOpen }),
      },
      {
        label: store.layout.viewportFullscreen ? 'Leave Fullscreen Viewport' : 'Fullscreen Viewport',
        shortcut: 'F11',
        action: () => store.patchLayout({ viewportFullscreen: !store.layout.viewportFullscreen }),
      },
      { separator: true, label: '' },
      { label: store.consoleOpen ? 'Hide Console' : 'Show Console', shortcut: 'Ctrl+`',
        action: store.toggleConsole },
      { label: 'Home', action: () => store.setScreen('home') },
    ],
    Tools: [
      { label: 'Asset Library', action: () => { void store.scanLibrary(); store.setScreen('editor'); } },
      { label: 'AI Texture Generator', action: () => store.setModal('ai') },
      { label: 'Run Validation', shortcut: 'F5', disabled: !projectOpen,
        action: () => { store.setTab('validation'); void store.runValidation(); } },
      ...(devMode
        ? [
          { label: 'Raw Asset Inspector', action: () => store.setTab('developer') },
          { label: 'Web Developer Tools', shortcut: 'F12', action: () => host.window.devtools() },
        ]
        : []),
    ],
    Export: [
      { label: 'Export FiveM Resource…', disabled: !projectOpen, action: () => store.setModal('export') },
      { label: 'Open Export Folder', disabled: !store.projectState.directory,
        action: () => call('shell.openFolder', { path: `${store.projectState.directory}\\exports` }) },
    ],
    Help: [
      { label: 'Open Log Folder', action: async () => {
        const s = await call<{ paths: { logs: string } }>('settings.get');
        await call('shell.openFolder', { path: s.paths.logs });
      } },
      { label: 'About', action: () => store.setModal('about') },
    ],
  };

  return (
    <div className="menubar" ref={barRef}>
      <div className="menubar-brand">
        <span className="mb-mark">B</span>
        <span className="mb-name">Bitirim Clothing Creator</span>
      </div>

      {Object.entries(menus).map(([name, items]) => (
        <div key={name} className="menu-root">
          <button
            className={`menu-title${open === name ? ' open' : ''}`}
            onMouseDown={(e) => { e.preventDefault(); setOpen(open === name ? null : name); }}
            onMouseEnter={() => open && setOpen(name)}
          >
            {name}
          </button>

          {open === name && (
            <div className="menu-pop">
              {items.map((item, i) =>
                item.separator ? (
                  <div key={i} className="menu-sep" />
                ) : (
                  <button
                    key={i}
                    className="menu-item"
                    disabled={item.disabled}
                    onClick={() => { setOpen(null); item.action?.(); }}
                  >
                    <span>{item.label}</span>
                    {item.shortcut && <span className="menu-shortcut">{item.shortcut}</span>}
                  </button>
                ))}
            </div>
          )}
        </div>
      ))}

      <span className="grow" />

      {store.projectState.open && (
        <div className="menubar-project">
          <span className="nowrap">{store.projectState.project?.name}</span>
          {store.projectState.dirty && <span className="dirty-dot" title="Unsaved changes" />}
          {store.projectState.isMock && <Badge kind="mock">Mock</Badge>}
        </div>
      )}
    </div>
  );
}

/* --------------------------------------------------------------- Status bar */

export function StatusBar() {
  const store = useApp();
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const mesh = store.meshInfo;
  const caps = store.caps;

  const component = store.components.find(
    (c) => c.prefix === asset?.component?.toLowerCase());

  return (
    <div className="statusbar">
      <span className={store.meshLoading ? 'accent' : ''}>
        {store.meshLoading ? 'Loading…' : store.status}
      </span>

      <span className="sb-sep" />
      <StatusItem label="Asset" value={mesh?.source} />
      <StatusItem label="Component" value={component ? String(component.index) : null} />
      <StatusItem label="Drawable" value={asset ? String(asset.drawableIndex) : null} />
      <StatusItem label="Texture" value={variation ? String(variation.index) : null} />
      <StatusItem label="Vertices" value={mesh ? mesh.vertexCount.toLocaleString() : null} />

      <span className="grow" />

      {store.projectState.history?.depth
        ? (
          <span className="sb-item" title="Undo history depth">
            <span className="sb-label">Undo:</span>
            <span className="sb-value mono">{store.projectState.history.depth}</span>
          </span>
        )
        : null}

      {mesh?.isMock && <Badge kind="mock">Mock asset</Badge>}
      {caps && !caps.available && (
        <Badge kind="error" title={caps.reason ?? undefined}>Engine offline</Badge>
      )}
      {store.validationStatus && (
        <Badge kind={store.validationStatus === 'RED' ? 'error'
          : store.validationStatus === 'YELLOW' ? 'warn' : 'ok'}>
          {store.validationStatus}
        </Badge>
      )}
      <span className="sb-version faint">v{store.info?.version ?? '—'}</span>
    </div>
  );
}

function StatusItem({ label, value }: { label: string; value: string | null | undefined }) {
  return (
    <span className="sb-item">
      <span className="sb-label">{label}:</span>
      <span className={value ? 'sb-value' : 'sb-value na'}>{value ?? '—'}</span>
    </span>
  );
}
