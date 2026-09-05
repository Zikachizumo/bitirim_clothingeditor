import { useEffect, useState } from 'react';
import { useApp } from '../state/store';
import { AssetBrowser } from '../panels/AssetBrowser';
import { GarmentPanel } from '../panels/GarmentPanel';
import { LayersPanel } from '../panels/LayersPanel';
import { Properties } from '../panels/Properties';
import { CharacterPanel } from '../panels/CharacterPanel';
import { NeonPanel } from '../panels/NeonPanel';
import {
  DeveloperPanel, LogConsole, MaterialPanel, UvPanel, ValidationPanel,
} from '../panels/SidePanels';
import { TextureEditor } from '../editors/TextureEditor';
import { Viewport } from '../viewport/Viewport';
import { Badge, Splitter, Tabs } from '../ui/primitives';
import type { EditorTab } from '../state/store';
import './screens.css';

const MIN_SIDE = 210;
const MAX_SIDE = 560;

/** Which left-hand panel is showing. */
type LeftTab = 'assets' | 'garment';

export function Editor() {
  const tab = useApp((s) => s.tab);
  const setTab = useApp((s) => s.setTab);
  const consoleOpen = useApp((s) => s.consoleOpen);
  const projectState = useApp((s) => s.projectState);
  const projectOpen = projectState.open;
  const validationStatus = useApp((s) => s.validationStatus);
  const meshInfo = useApp((s) => s.meshInfo);
  const layout = useApp((s) => s.layout);
  const patchLayout = useApp((s) => s.patchLayout);
  const developerMode = useApp((s) => s.info?.developerMode ?? false);

  const [leftTab, setLeftTab] = useState<LeftTab>('assets');

  // The composited texture drives the 3D preview, so painting updates the
  // garment live. It is kept in the store because the AI dialog sends the same
  // image as the texture being edited.
  const preview = useApp((s) => s.compositePreview);
  const setPreview = useApp((s) => s.setCompositePreview);

  useEffect(() => {
    if (!projectOpen) setPreview(null);
  }, [projectOpen]);

  const toggleFullscreen = () =>
    patchLayout({ viewportFullscreen: !layout.viewportFullscreen });

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'F11') { e.preventDefault(); toggleFullscreen(); }
      if (e.key === 'Escape' && layout.viewportFullscreen) {
        patchLayout({ viewportFullscreen: false });
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layout.viewportFullscreen]);

  const tabs: { id: EditorTab; label: string; disabled?: boolean; reason?: string }[] = [
    { id: 'model', label: 'Model' },
    { id: 'uv', label: 'UV' },
    { id: 'texture', label: 'Texture', disabled: !projectOpen, reason: 'Open a project first' },
    { id: 'material', label: 'Material' },
    { id: 'character', label: 'Character' },
    { id: 'neon', label: 'Neon', disabled: !projectOpen, reason: 'Open a project first' },
    { id: 'validation', label: 'Validation' },
    ...(developerMode ? [{ id: 'developer' as EditorTab, label: 'Developer' }] : []),
  ];

  if (layout.viewportFullscreen) {
    return (
      <div className="editor editor-fullscreen">
        <Viewport textureUrl={preview} fullscreen onToggleFullscreen={toggleFullscreen} />
      </div>
    );
  }

  return (
    <div className="editor">
      <div className="editor-main">
        {layout.leftOpen ? (
          <>
            <div className="editor-side" style={{ width: layout.leftWidth }}>
              <div className="side-switch">
                <button className={`side-tab${leftTab === 'assets' ? ' active' : ''}`}
                        onClick={() => setLeftTab('assets')}>Library</button>
                <button className={`side-tab${leftTab === 'garment' ? ' active' : ''}`}
                        onClick={() => setLeftTab('garment')}>Garment</button>
                <span className="grow" />
                <button className="mini" onClick={() => patchLayout({ leftOpen: false })}
                        title="Collapse this panel">⟨</button>
              </div>
              <div className="side-content">
                {leftTab === 'assets' ? <AssetBrowser /> : <GarmentPanel />}
              </div>
            </div>
            <Splitter onDrag={(d) => patchLayout({
              leftWidth: Math.max(MIN_SIDE, Math.min(MAX_SIDE, layout.leftWidth + d)),
            })} />
          </>
        ) : (
          <button className="rail rail-left" onClick={() => patchLayout({ leftOpen: true })}
                  title="Show the library and garment panels">⟩</button>
        )}

        <div className="editor-centre">
          <div className="editor-stage">
            {tab === 'model' && (
              <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
            )}

            {tab === 'uv' && (
              <div className="split-h">
                <div className="grow">
                  <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
                </div>
                <div className="pane-right" style={{ width: 520 }}>
                  <UvPanel textureUrl={preview} />
                </div>
              </div>
            )}

            {tab === 'texture' && (
              <div className="split-h">
                <div className="pane-left" style={{ width: '42%', minWidth: 300 }}>
                  <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
                </div>
                <div className="grow"><TextureEditor onCompositeChange={setPreview} /></div>
              </div>
            )}

            {tab === 'material' && (
              <div className="split-h">
                <div className="grow">
                  <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
                </div>
                <div className="pane-right" style={{ width: 360 }}><MaterialPanel /></div>
              </div>
            )}

            {tab === 'character' && (
              <div className="split-h">
                <div className="grow">
                  <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
                </div>
                <div className="pane-right" style={{ width: 400 }}><CharacterPanel /></div>
              </div>
            )}

            {tab === 'neon' && (
              <div className="split-h">
                <div className="pane-left" style={{ width: '42%', minWidth: 300 }}>
                  <Viewport textureUrl={preview} onToggleFullscreen={toggleFullscreen} />
                </div>
                <div className="grow neon-stage">
                  <NeonPanel />
                  {/* The same editor, painting the mask instead of the
                      garment. Sharing it is the point: a glow shape is drawn
                      with the same brush, at the same scale, over the same
                      artwork. */}
                  <TextureEditor glowMode onCompositeChange={setPreview} />
                </div>
              </div>
            )}

            {tab === 'validation' && <ValidationPanel />}
            {tab === 'developer' && <DeveloperPanel />}
          </div>

          <Tabs
            value={tab}
            onChange={setTab}
            items={tabs.map((t) => ({
              ...t,
              badge: t.id === 'validation' && validationStatus
                ? <span className={`tab-dot dot-${validationStatus.toLowerCase()}`} />
                : undefined,
            }))}
          />
        </div>

        {layout.rightOpen ? (
          <>
            <Splitter side="right" onDrag={(d) => patchLayout({
              rightWidth: Math.max(MIN_SIDE, Math.min(MAX_SIDE, layout.rightWidth + d)),
            })} />
            <div className="editor-side" style={{ width: layout.rightWidth }}>
              <div className="side-switch">
                <span className="side-title">Inspector</span>
                {projectState.isMock && <Badge kind="mock">Mock</Badge>}
                <span className="grow" />
                <button className="mini" onClick={() => patchLayout({ rightOpen: false })}
                        title="Collapse this panel">⟩</button>
              </div>
              <div className="editor-right">
                <div className="editor-right-top"><Properties /></div>
                <div className="editor-right-bottom"><LayersPanel /></div>
              </div>
            </div>
          </>
        ) : (
          <button className="rail rail-right" onClick={() => patchLayout({ rightOpen: true })}
                  title="Show the inspector">⟨</button>
        )}
      </div>

      {consoleOpen && (
        <div className="editor-console" style={{ height: layout.consoleHeight }}>
          <LogConsole />
        </div>
      )}

      {!meshInfo && !projectOpen && (
        <div className="editor-hint faint">
          Create or open a project, or double-click a drawable in the asset browser.
        </div>
      )}
    </div>
  );
}
