import { useEffect, useMemo, useRef, useState } from 'react';
import { call } from '../host/bridge';
import { useApp } from '../state/store';
import type { LibraryAsset, ProjectState } from '../state/store';
import { Badge, Button, EmptyState, Panel, Spinner } from '../ui/primitives';
import './panels.css';

type View = 'grid' | 'list';
type Sort = 'name' | 'date' | 'component' | 'drawable' | 'size';

/**
 * Thumbnail cache shared by every card in the session.
 *
 * The host caches the rendered PNG on disk; this keeps the decoded data URL in
 * memory so scrolling the grid does not round-trip per card. `unavailable`
 * entries are remembered too, so a drawable that cannot be rendered is asked
 * about once rather than on every re-render.
 */
const thumbnails = new Map<string, { state: string; dataUrl?: string; reason?: string }>();

export function AssetBrowser() {
  const library = useApp((s) => s.library);
  const libraryRoot = useApp((s) => s.libraryRoot);
  const libraryNote = useApp((s) => s.libraryNote);
  const loading = useApp((s) => s.libraryLoading);
  const scanLibrary = useApp((s) => s.scanLibrary);
  const loadMesh = useApp((s) => s.loadMesh);
  const meshPath = useApp((s) => s.meshPath);
  const components = useApp((s) => s.components);
  const favourites = useApp((s) => s.favourites);
  const toggleFavourite = useApp((s) => s.toggleFavourite);
  const projectOpen = useApp((s) => s.projectState.open);
  const refreshProject = useApp((s) => s.refreshProject);
  const reportError = useApp((s) => s.reportError);
  const toast = useApp((s) => s.toast);

  const [view, setView] = useState<View>('grid');
  const [search, setSearch] = useState('');
  const [gender, setGender] = useState<'all' | 'male' | 'female'>('all');
  const [component, setComponent] = useState<number | 'all'>('all');
  const [sort, setSort] = useState<Sort>('name');
  const [onlyFavourites, setOnlyFavourites] = useState(false);
  const [selected, setSelected] = useState<string | null>(null);

  useEffect(() => { void scanLibrary(); }, [scanLibrary]);

  const filtered = useMemo(() => {
    const needle = search.trim().toLowerCase();
    const list = library.filter((a) => {
      if (gender === 'male' && !a.male) return false;
      if (gender === 'female' && a.male) return false;
      if (component !== 'all' && a.component !== component) return false;
      if (onlyFavourites && !favourites.includes(a.path)) return false;
      if (needle && !a.name.toLowerCase().includes(needle)
        && !(a.componentPrefix ?? '').includes(needle)) return false;
      return true;
    });

    const compare: Record<Sort, (a: LibraryAsset, b: LibraryAsset) => number> = {
      name: (a, b) => a.name.localeCompare(b.name),
      date: (a, b) => b.modified.localeCompare(a.modified),
      component: (a, b) => (a.component ?? 99) - (b.component ?? 99)
        || a.name.localeCompare(b.name),
      drawable: (a, b) => (a.drawableIndex ?? 9999) - (b.drawableIndex ?? 9999),
      size: (a, b) => b.sizeBytes - a.sizeBytes,
    };

    return [...list].sort(compare[sort]);
  }, [library, search, gender, component, sort, onlyFavourites, favourites]);

  const open = async (asset: LibraryAsset) => {
    setSelected(asset.id);
    await loadMesh(asset.path);
  };

  const useAsBase = async (asset: LibraryAsset) => {
    if (!projectOpen) {
      toast('info', 'Open a project first.', 'A base asset belongs to a garment in a project.');
      return;
    }
    try {
      const next = await call<ProjectState>('assets.setBase', {
        ydd: asset.path,
        ytd: asset.texturePaths[0] ?? null,
      });
      refreshProject(next);
      await loadMesh(next.resolved?.ydd ?? null);
      toast('success', `${asset.name} is now the base asset.`);
    } catch (err) {
      reportError(err, 'That asset could not be used as the base.');
    }
  };

  const pickRoot = async () => {
    try {
      const result = await call<{ root: string } | null>('library.pickRoot');
      if (result) await scanLibrary(result.root);
    } catch (err) {
      reportError(err, 'The asset library folder could not be set.');
    }
  };

  const importOne = async () => {
    try {
      const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind: 'ydd' });
      if (!picked?.paths?.length) return;
      await loadMesh(picked.paths[0]);
      toast('success', `Loaded ${picked.paths[0].split(/[\\/]/).pop()}.`);
    } catch (err) {
      reportError(err, 'That asset could not be loaded.');
    }
  };

  return (
    <Panel
      title="Assets"
      actions={
        <>
          <button className={`mini${view === 'grid' ? ' active' : ''}`}
                  onClick={() => setView('grid')} title="Grid view">▦</button>
          <button className={`mini${view === 'list' ? ' active' : ''}`}
                  onClick={() => setView('list')} title="List view">☰</button>
          <button className="mini" onClick={() => scanLibrary()} title="Rescan library">⟳</button>
        </>
      }
      scroll={false}
      flush
    >
      <div className="ab-filters">
        <input
          type="search"
          placeholder="Search assets…"
          value={search}
          onChange={(e) => setSearch(e.target.value)}
        />
        <div className="row" style={{ gap: 4 }}>
          {(['all', 'male', 'female'] as const).map((g) => (
            <button key={g} className={`chip${gender === g ? ' active' : ''}`}
                    onClick={() => setGender(g)}>{g}</button>
          ))}
          <span className="grow" />
          <button
            className={`chip${onlyFavourites ? ' active' : ''}`}
            onClick={() => setOnlyFavourites((v) => !v)}
            title="Show only favourites"
          >★</button>
        </div>
        <div className="row" style={{ gap: 4 }}>
          <select
            value={component}
            onChange={(e) => setComponent(e.target.value === 'all' ? 'all' : Number(e.target.value))}
            style={{ flex: 1 }}
          >
            <option value="all">All components</option>
            {components.map((c) => (
              <option key={c.index} value={c.index}>{c.prefix} — {c.label}</option>
            ))}
          </select>
          <select value={sort} onChange={(e) => setSort(e.target.value as Sort)} title="Sort by">
            <option value="name">Name</option>
            <option value="date">Date</option>
            <option value="component">Component</option>
            <option value="drawable">Drawable</option>
            <option value="size">Size</option>
          </select>
        </div>
      </div>

      <div className="ab-body scroll">
        {loading && (
          <div className="ab-loading"><Spinner /> <span className="dim">Scanning…</span></div>
        )}

        {!loading && library.length === 0 && (
          <EmptyState
            title="No asset library"
            detail={libraryNote
              ?? 'Point the library at a folder of .ydd drawables, or import a single file.'}
            action={
              <div className="row">
                <Button size="sm" variant="primary" onClick={pickRoot}>Choose folder</Button>
                <Button size="sm" onClick={importOne}>Import .ydd</Button>
              </div>
            }
          />
        )}

        {!loading && library.length > 0 && filtered.length === 0 && (
          <EmptyState title="Nothing matches" detail="Try a different search or filter." />
        )}

        {!loading && filtered.length > 0 && view === 'grid' && (
          <div className="ab-grid">
            {filtered.map((a) => (
              <AssetCard
                key={a.id}
                asset={a}
                selected={selected === a.id || meshPath === a.path}
                favourite={favourites.includes(a.path)}
                onSelect={() => setSelected(a.id)}
                onOpen={() => open(a)}
                onUse={() => useAsBase(a)}
                onFavourite={() => toggleFavourite(a.path)}
              />
            ))}
          </div>
        )}

        {!loading && filtered.length > 0 && view === 'list' && (
          <table className="ab-table">
            <thead>
              <tr>
                <th></th><th>Name</th><th>Comp</th><th>Draw</th><th>Tex</th>
                <th className="right">Size</th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((a) => (
                <tr
                  key={a.id}
                  className={selected === a.id ? 'selected' : ''}
                  onClick={() => setSelected(a.id)}
                  onDoubleClick={() => open(a)}
                  title={a.path}
                >
                  <td>
                    <button
                      className={`mini${favourites.includes(a.path) ? ' active' : ''}`}
                      onClick={(e) => { e.stopPropagation(); toggleFavourite(a.path); }}
                      title="Favourite"
                    >★</button>
                  </td>
                  <td className="nowrap">{a.name}</td>
                  <td className={a.component === null ? 'na' : ''}>{a.component ?? '—'}</td>
                  <td className={a.drawableIndex === null ? 'na' : ''}>{a.drawableIndex ?? '—'}</td>
                  <td className={a.textureCount === 0 ? 'na' : ''}>{a.textureCount || '—'}</td>
                  <td className="right mono dim">{(a.sizeBytes / 1024).toFixed(0)} KB</td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <div className="ab-footer">
        <span className="nowrap dim" title={libraryRoot ?? undefined}>
          {libraryRoot ? libraryRoot.split(/[\\/]/).slice(-2).join('/') : 'No library folder'}
        </span>
        <span className="grow" />
        <Badge kind="neutral">{filtered.length}</Badge>
        <button className="mini" onClick={pickRoot} title="Change library folder">…</button>
      </div>
    </Panel>
  );
}

/* ------------------------------------------------------------------- card */

function AssetCard({
  asset, selected, favourite, onSelect, onOpen, onUse, onFavourite,
}: {
  asset: LibraryAsset;
  selected: boolean;
  favourite: boolean;
  onSelect: () => void;
  onOpen: () => void;
  onUse: () => void;
  onFavourite: () => void;
}) {
  const [thumb, setThumb] = useState(() => thumbnails.get(asset.path));
  const cardRef = useRef<HTMLDivElement>(null);

  // Thumbnails render on demand as cards scroll into view: a library of a few
  // thousand drawables must not fire a few thousand renders on open.
  useEffect(() => {
    if (thumb) return;
    const element = cardRef.current;
    if (!element) return;

    let cancelled = false;
    const observer = new IntersectionObserver(async (entries) => {
      if (!entries[0]?.isIntersecting) return;
      observer.disconnect();

      try {
        const result = await call<{ state: string; dataUrl?: string; reason?: string }>(
          'library.thumbnail', { path: asset.path });
        if (cancelled) return;
        thumbnails.set(asset.path, result);
        setThumb(result);
      } catch {
        const failure = { state: 'unavailable', reason: 'No preview could be rendered.' };
        thumbnails.set(asset.path, failure);
        if (!cancelled) setThumb(failure);
      }
    }, { rootMargin: '160px' });

    observer.observe(element);
    return () => { cancelled = true; observer.disconnect(); };
  }, [asset.path, thumb]);

  return (
    <div
      ref={cardRef}
      className={`asset-card${selected ? ' selected' : ''}`}
      onClick={onSelect}
      onDoubleClick={onOpen}
      title={asset.path}
    >
      <div className="asset-thumb">
        {thumb?.state === 'ready' && thumb.dataUrl ? (
          <img src={thumb.dataUrl} alt="" draggable={false} />
        ) : thumb?.state === 'unavailable' ? (
          // Never a blank square passed off as a render.
          <span className="asset-thumb-text na" title={thumb.reason}>
            {asset.componentPrefix ?? '?'}
          </span>
        ) : (
          <span className="asset-thumb-loading"><Spinner size={12} /></span>
        )}

        <button
          className={`asset-fav${favourite ? ' active' : ''}`}
          onClick={(e) => { e.stopPropagation(); onFavourite(); }}
          title={favourite ? 'Remove from favourites' : 'Add to favourites'}
        >★</button>
      </div>

      <div className="asset-meta">
        <div className="asset-name nowrap">{asset.name}</div>
        <div className="asset-sub">
          <span className={asset.component === null ? 'na' : ''}>
            {asset.component === null ? '—' : `Comp ${asset.component}`}
          </span>
          <span className={asset.drawableIndex === null ? 'na' : ''}>
            {asset.drawableIndex === null ? '—' : `Draw ${asset.drawableIndex}`}
          </span>
          <span className={asset.textureCount === 0 ? 'na' : ''}>
            {asset.textureCount === 0 ? '—' : `${asset.textureCount} var`}
          </span>
        </div>
      </div>

      <div className="asset-actions">
        <button className="mini" onClick={(e) => { e.stopPropagation(); onOpen(); }}
                title="Preview in the viewport">👁</button>
        <button className="mini" onClick={(e) => { e.stopPropagation(); onUse(); }}
                title="Use as this garment's base asset">Use</button>
      </div>
    </div>
  );
}
