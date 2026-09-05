import { useEffect, useMemo } from 'react';
import { call, host, on } from './host/bridge';
import { useApp } from './state/store';
import type { ProjectState } from './state/store';
import { inTextField, matchShortcut, resolvedBindings } from './state/shortcuts';
import { MenuBar, StatusBar } from './shell/Chrome';
import { Home } from './screens/Home';
import { NewProjectWizard } from './screens/NewProjectWizard';
import { Editor } from './screens/Editor';
import {
  AboutDialog, AiDialog, RecoveryDialog, SearchPalette, SettingsDialog, ShortcutsDialog,
} from './dialogs/Dialogs';
import { ExportWizard } from './dialogs/ExportWizard';
import { Toasts } from './ui/primitives';

export function App() {
  const booted = useApp((s) => s.booted);
  const screen = useApp((s) => s.screen);
  const modal = useApp((s) => s.modal);
  const store = useApp();

  const bindings = useMemo(() => resolvedBindings(), [modal]);

  /* ------------------------------------------------------------- bootstrap */

  useEffect(() => {
    let cancelled = false;

    (async () => {
      await store.boot();
      if (cancelled) return;

      // Tell the host we have painted, which retires the native splash.
      const ready = await host.ready().catch(() => null);

      const settings = await call<{ settings: { theme: string; uiScale: number } }>('settings.get')
        .catch(() => null);
      if (settings?.settings.theme) {
        document.documentElement.dataset.theme = settings.settings.theme;
      }
      if (settings?.settings.uiScale) {
        // A 4K display at 100% Windows scaling makes a dense tool unreadable;
        // this is the knob for it, applied to the root font size so every
        // rem-derived size follows.
        document.documentElement.style.fontSize = `${settings.settings.uiScale * 100}%`;
      }

      // A .bitirimclothing passed on the command line (or double-clicked).
      if (ready?.startupFile) {
        await store.openProject(ready.startupFile);
      }
    })();

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /* ----------------------------------------------------------- host events */

  useEffect(() => {
    const offAutosave = on('project.autosaved', () => {
      store.setStatus('Autosaved');
      setTimeout(() => store.setStatus('Ready'), 2500);
      void call('project.current').then((s) => store.refreshProject(s as ProjectState));
    });

    const offDrop = on('shell.filesDropped', async (data) => {
      const paths = (data as { paths: string[] })?.paths ?? [];
      if (paths.length === 0) return;

      const first = paths[0];
      const ext = first.split('.').pop()?.toLowerCase();

      if (ext === 'bitirimclothing') {
        await store.openProject(first);
        return;
      }
      if (ext === 'ydd') {
        await store.loadMesh(first);
        store.toast('success', `Loaded ${first.split(/[\\/]/).pop()}.`);
        return;
      }
      store.toast('warn', `Dropped file is not supported here: .${ext ?? '?'}`,
        'Drop a .bitirimclothing project or a .ydd drawable.');
    });

    return () => { offAutosave(); offDrop(); };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  /* -------------------------------------------------------------- autosave */

  useEffect(() => {
    const timer = setInterval(() => {
      if (!useApp.getState().projectState.dirty) return;
      void call('project.autosave').catch(() => { /* reported by the host log */ });
    }, 60_000);
    return () => clearInterval(timer);
  }, []);

  /* -------------------------------------------------------------- shortcuts */

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      const match = matchShortcut(e, bindings);
      if (!match) return;

      // Shortcuts that would eat a keystroke while typing are held back unless
      // they are marked global -- Ctrl+S has to work mid-rename.
      if (!match.global && inTextField(e)) return;

      const state = useApp.getState();
      const open = state.projectState.open;

      switch (match.id) {
        case 'project.new': state.setScreen('wizard'); break;
        case 'project.open': void state.openProject(); break;
        case 'project.save': if (open) void state.saveProject(); break;
        case 'project.saveAs':
          void call('project.saveAs').then((s) => s && state.refreshProject(s as ProjectState));
          break;
        case 'asset.import':
          void call<{ paths: string[] } | null>('asset.pickFile', { kind: 'ydd' })
            .then((picked) => { if (picked?.paths?.length) void state.loadMesh(picked.paths[0]); });
          break;
        case 'export.open': if (open) state.setModal('export'); break;

        case 'edit.undo': void state.undo(); break;
        case 'edit.redo':
        case 'edit.redoAlt': void state.redo(); break;

        case 'search.open': state.setModal('search'); break;
        case 'console.toggle': state.toggleConsole(); break;

        case 'tab.model': state.setTab('model'); break;
        case 'tab.uv': state.setTab('uv'); break;
        case 'tab.texture': if (open) state.setTab('texture'); break;
        case 'tab.material': state.setTab('material'); break;
        case 'tab.character': state.setTab('character'); break;
        case 'tab.neon': if (open) state.setTab('neon'); break;
        case 'tab.validation': state.setTab('validation'); break;

        case 'validate.run': if (open) { state.setTab('validation'); void state.runValidation(); } break;
        case 'help.shortcuts': state.setModal('shortcuts'); break;
        case 'devtools.open': void host.window.devtools(); break;
        default: return;
      }

      e.preventDefault();
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [bindings]);

  if (!booted) {
    // The native splash is still covering the window at this point.
    return <div style={{ height: '100%', background: 'var(--bg-1)' }} />;
  }

  return (
    <div className="app">
      <MenuBar />

      <div className="app-body">
        {screen === 'home' && <Home />}
        {screen === 'wizard' && <NewProjectWizard />}
        {screen === 'editor' && <Editor />}
      </div>

      <StatusBar />

      {modal === 'export' && <ExportWizard />}
      {modal === 'settings' && <SettingsDialog />}
      {modal === 'about' && <AboutDialog />}
      {modal === 'ai' && <AiDialog />}
      {modal === 'search' && <SearchPalette />}
      {modal === 'recovery' && <RecoveryDialog />}
      {modal === 'shortcuts' && <ShortcutsDialog />}

      <Toasts />
    </div>
  );
}
