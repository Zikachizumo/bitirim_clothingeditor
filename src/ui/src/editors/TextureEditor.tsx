import { useCallback, useEffect, useMemo, useRef, useState } from 'react';
import { call } from '../host/bridge';
import { activeAsset, activeVariation, newId, useApp } from '../state/store';
import type { BrushStroke, TextureLayer } from '../state/store';
import { Badge, Button, EmptyState } from '../ui/primitives';
import { ColorSwatch, rgbToHex } from '../ui/ColorPicker';
import { composite, layerBounds, paintCheckerboard } from './compositor';
import './texture.css';

export type Tool =
  | 'select' | 'brush' | 'eraser' | 'fill' | 'picker'
  | 'line' | 'rectangle' | 'ellipse' | 'gradient'
  | 'text' | 'image' | 'marquee' | 'lasso';

interface ToolSpec {
  id: Tool;
  label: string;
  glyph: string;
  hint: string;
  key?: string;
}

const TOOLS: ToolSpec[] = [
  { id: 'select', label: 'Select', glyph: '⬉', key: 'V', hint: 'Move, scale and rotate the selected layer' },
  { id: 'brush', label: 'Brush', glyph: '🖌', key: 'B', hint: 'Paint onto a brush layer' },
  { id: 'eraser', label: 'Eraser', glyph: '⌫', key: 'E', hint: 'Erase from the selected brush layer' },
  { id: 'fill', label: 'Fill', glyph: '🪣', key: 'G', hint: 'Add a colour layer filling the canvas or the selection' },
  { id: 'picker', label: 'Picker', glyph: '⌖', key: 'I', hint: 'Sample a colour from the canvas' },
  { id: 'line', label: 'Line', glyph: '╱', key: 'L', hint: 'Drag to draw a line layer' },
  { id: 'rectangle', label: 'Rectangle', glyph: '▭', key: 'R', hint: 'Drag to draw a rectangle layer' },
  { id: 'ellipse', label: 'Ellipse', glyph: '◯', key: 'O', hint: 'Drag to draw an ellipse layer' },
  { id: 'gradient', label: 'Gradient', glyph: '◨', key: 'D', hint: 'Add a gradient layer' },
  { id: 'text', label: 'Text', glyph: 'T', key: 'T', hint: 'Click to place a text layer' },
  { id: 'image', label: 'Image', glyph: '🖼', hint: 'Import an image as a layer' },
  { id: 'marquee', label: 'Marquee', glyph: '⬚', key: 'M', hint: 'Rectangular selection; constrains painting' },
  { id: 'lasso', label: 'Lasso', glyph: '✎', key: 'Q', hint: 'Freehand selection; constrains painting' },
];

type Selection =
  | { kind: 'rect' | 'ellipse'; x: number; y: number; width: number; height: number }
  | { kind: 'lasso'; points: number[] }
  | null;

type Handle = 'nw' | 'ne' | 'se' | 'sw' | 'rotate' | 'move';

export function TextureEditor({ onCompositeChange, glowMode = false }: {
  onCompositeChange?: (dataUrl: string | null) => void;
  /**
   * Paint the glow mask instead of the garment.
   *
   * Same tools, same canvas, one difference that matters: what you see is the
   * mask that ships, not a tinted hint of it. A neon garment is judged on
   * where it lights up, and that is impossible to judge through a preview of
   * something else.
   */
  glowMode?: boolean;
}) {
  const projectState = useApp((s) => s.projectState);
  const asset = useApp(activeAsset);
  const variation = useApp(activeVariation);
  const updateProject = useApp((s) => s.updateProject);
  const selectedLayerId = useApp((s) => s.selectedLayerId);
  const selectLayer = useApp((s) => s.selectLayer);
  const toast = useApp((s) => s.toast);
  const reportError = useApp((s) => s.reportError);
  const refreshProject = useApp((s) => s.refreshProject);

  const size = projectState.project?.settings?.textureSize ?? 512;

  const [tool, setTool] = useState<Tool>('select');
  const [color, setColor] = useState('#c81e1e');
  const [brushSize, setBrushSize] = useState(28);
  const [softness, setSoftness] = useState(0);
  const [zoom, setZoom] = useState(1);
  const [pan, setPan] = useState({ x: 0, y: 0 });
  const [checkerboard, setCheckerboard] = useState(true);
  const [dirty, setDirty] = useState(false);
  const [selection, setSelection] = useState<Selection>(null);
  const [gradientType, setGradientType] = useState<'linear' | 'radial'>('linear');
  const [shapeFilled, setShapeFilled] = useState(true);
  const [strokeWidth, setStrokeWidth] = useState(6);
  /** Neon mode: ghost the garment under the mask so a shape can be traced. */
  const [showUnderlay, setShowUnderlay] = useState(true);

  const canvasRef = useRef<HTMLCanvasElement>(null);
  const stageRef = useRef<HTMLDivElement>(null);
  const compositeRef = useRef<HTMLCanvasElement>(document.createElement('canvas'));
  const measureRef = useRef<CanvasRenderingContext2D | null>(null);
  const images = useRef(new Map<string, HTMLImageElement>());
  const baseImage = useRef<HTMLImageElement | null>(null);
  const [baseState, setBaseState] = useState<'loading' | 'ready' | 'none'>('loading');

  /** A stroke in progress. Kept out of the document until the pointer lifts. */
  const [liveStroke, setLiveStroke] = useState<{ layerId: string; stroke: BrushStroke } | null>(null);
  const strokeRef = useRef<{ layerId: string; stroke: BrushStroke } | null>(null);

  /** A shape or selection being dragged out. */
  const dragRef = useRef<{ kind: 'shape' | 'select' | 'transform' | 'pan';
                           startX: number; startY: number;
                           handle?: Handle; origin?: TextureLayer } | null>(null);
  const [dragBox, setDragBox] = useState<
    { x: number; y: number; width: number; height: number } | null>(null);

  const layers = useMemo(() => variation?.layers ?? [], [variation]);
  const selected = layers.find((l) => l.id === selectedLayerId) ?? null;

  if (!measureRef.current) {
    measureRef.current = document.createElement('canvas').getContext('2d');
  }

  /* --------------------------------------------------------- image cache */

  const layerSignature = layers.map((l) => `${l.id}:${l.source ?? ''}`).join('|');

  useEffect(() => {
    let cancelled = false;

    (async () => {
      for (const layer of layers) {
        if (layer.kind !== 'image' && layer.kind !== 'generated') continue;
        if (!layer.source || images.current.has(layer.id)) continue;

        try {
          const { dataUrl } = await call<{ dataUrl: string }>('asset.readImage',
            { relativePath: layer.source });
          if (cancelled) return;

          await new Promise<void>((resolve) => {
            const img = new Image();
            img.onload = () => { images.current.set(layer.id, img); resolve(); };
            img.onerror = () => resolve();
            img.src = dataUrl;
          });
          if (!cancelled) redraw();
        } catch {
          // A missing layer image is reported by validation rather than as a
          // toast on every frame.
        }
      }
    })();

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layerSignature]);

  /* ------------------------------------------------- base diffuse texture */

  useEffect(() => {
    let cancelled = false;
    setBaseState('loading');
    baseImage.current = null;

    call<{ available: boolean; dataUrl?: string }>('texture.baseImage', { assetId: asset?.id })
      .then((result) => {
        if (cancelled) return;
        if (!result.available || !result.dataUrl) { setBaseState('none'); return; }

        const img = new Image();
        img.onload = () => {
          if (cancelled) return;
          baseImage.current = img;
          setBaseState('ready');
          redraw();
        };
        img.onerror = () => !cancelled && setBaseState('none');
        img.src = result.dataUrl;
      })
      .catch(() => { if (!cancelled) setBaseState('none'); });

    return () => { cancelled = true; };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [projectState.directory, asset?.id]);

  /* --------------------------------------------------------------- draw */

  const redraw = useCallback(() => {
    const canvas = canvasRef.current;
    if (!canvas) return;

    composite(compositeRef.current, {
      size,
      layers,
      images: images.current,
      base: baseImage.current,
      liveStroke: strokeRef.current,
      mode: glowMode ? 'glow-edit' : 'preview',
      underlay: showUnderlay,
    });

    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    canvas.width = size;
    canvas.height = size;
    if (checkerboard) paintCheckerboard(ctx, size, size);
    ctx.drawImage(compositeRef.current, 0, 0);

    // Transform handles for the selected layer.
    if (selected && tool === 'select' && measureRef.current) {
      const box = layerBounds(selected, measureRef.current);
      if (box) drawHandles(ctx, box, selected.rotation);
    }

    // The active selection outline, and any box being dragged out right now.
    if (selection) drawSelection(ctx, selection);
    if (dragBox) {
      ctx.save();
      ctx.strokeStyle = '#a8e10c';
      ctx.lineWidth = 1.5;
      ctx.setLineDash([5, 4]);
      ctx.strokeRect(dragBox.x, dragBox.y, dragBox.width, dragBox.height);
      ctx.restore();
    }

    // The 3D viewport always wants the garment, never the mask. In neon mode
    // that means a second composite: what is on this canvas is the thing being
    // painted, and it is not what the ped should be wearing.
    if (glowMode) {
      const colour = document.createElement('canvas');
      composite(colour, {
        size, layers, images: images.current, base: baseImage.current, mode: 'color',
      });
      onCompositeChange?.(colour.toDataURL('image/png'));
    } else {
      onCompositeChange?.(compositeRef.current.toDataURL('image/png'));
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [layers, selected, tool, selection, dragBox, size, checkerboard, onCompositeChange,
      glowMode, showUnderlay]);

  useEffect(() => { redraw(); }, [redraw]);
  useEffect(() => { redraw(); }, [liveStroke, redraw]);

  /* ------------------------------------------------------------ mutation */

  const mutateLayers = (
    mutate: (list: TextureLayer[]) => void,
    label: string,
    mergeKey?: string,
  ) => {
    if (!variation || !asset) return;
    setDirty(true);
    void updateProject((draft) => {
      const a = draft.assets.find((x) => x.id === asset.id);
      const v = a?.variations.find((x) => x.id === variation.id);
      if (v) mutate(v.layers);
    }, { label, mergeKey });
  };

  const addLayer = (layer: TextureLayer, label: string) => {
    // A new layer joins whatever group the selection is in, which is what
    // happens in every layered editor and saves a drag afterwards.
    const parentId = selected
      ? (selected.kind === 'group' ? selected.id : selected.parentId ?? null)
      : null;

    mutateLayers((list) => list.unshift({ ...layer, parentId }), label);
    selectLayer(layer.id);
  };

  const nextName = (prefix: string) =>
    `${prefix} ${layers.filter((l) => l.name.startsWith(prefix)).length + 1}`;

  const baseLayer = (kind: TextureLayer['kind'], name: string): TextureLayer => ({
    id: newId(), name, kind,
    visible: true, opacity: 1, blendMode: null, parentId: null, locked: false,
    x: 0, y: 0, width: 0, height: 0, rotation: 0,
    // In neon mode every new layer belongs to the mask. Anything else would
    // silently paint the garment while the canvas shows a mask.
    glow: glowMode,
  });

  /* ------------------------------------------------------- canvas mapping */

  const canvasPoint = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const rect = e.currentTarget.getBoundingClientRect();
    return {
      x: ((e.clientX - rect.left) / rect.width) * size,
      y: ((e.clientY - rect.top) / rect.height) * size,
    };
  };

  const sampleColour = (x: number, y: number): string | null => {
    const ctx = compositeRef.current.getContext('2d', { willReadFrequently: true });
    if (!ctx) return null;
    try {
      const [r, g, b] = ctx.getImageData(Math.floor(x), Math.floor(y), 1, 1).data;
      return rgbToHex(r, g, b);
    } catch {
      return null;
    }
  };

  const hitHandle = (point: { x: number; y: number }): Handle | null => {
    if (!selected || !measureRef.current) return null;
    const box = layerBounds(selected, measureRef.current);
    if (!box) return null;

    const grab = 12;
    const corners: [Handle, number, number][] = [
      ['nw', box.x, box.y],
      ['ne', box.x + box.width, box.y],
      ['se', box.x + box.width, box.y + box.height],
      ['sw', box.x, box.y + box.height],
      ['rotate', box.x + box.width / 2, box.y - 26],
    ];

    for (const [handle, hx, hy] of corners) {
      if (Math.abs(point.x - hx) <= grab && Math.abs(point.y - hy) <= grab) return handle;
    }

    const inside = point.x >= box.x && point.x <= box.x + box.width
      && point.y >= box.y && point.y <= box.y + box.height;
    return inside ? 'move' : null;
  };

  /* --------------------------------------------------------------- tools */

  const onPointerDown = (e: React.PointerEvent<HTMLCanvasElement>) => {
    if (!variation) return;
    const point = canvasPoint(e);
    e.currentTarget.setPointerCapture(e.pointerId);

    // Middle mouse or space-drag pans, whatever tool is active.
    if (e.button === 1 || e.altKey) {
      dragRef.current = { kind: 'pan', startX: e.clientX, startY: e.clientY };
      return;
    }

    switch (tool) {
      case 'picker': {
        const sampled = sampleColour(point.x, point.y);
        if (sampled) {
          setColor(sampled);
          setTool('brush');
          toast('info', `Picked ${sampled}.`);
        }
        return;
      }

      case 'fill': {
        const bounds = selectionBounds(selection, size);
        addLayer({
          ...baseLayer('fill', nextName('Fill')),
          x: 0, y: 0, width: size, height: size, color,
        }, 'Add fill layer');
        // A selection turns the fill into a shape confined to it, which is
        // what "fill the selection" has to mean when layers are the unit.
        if (bounds) {
          mutateLayers((list) => {
            const top = list[0];
            if (!top) return;
            top.kind = 'shape';
            top.shape = selection?.kind === 'ellipse' ? 'ellipse' : 'rectangle';
            top.x = bounds.x; top.y = bounds.y;
            top.width = bounds.width; top.height = bounds.height;
            top.filled = true;
            top.name = nextName('Fill');
          }, 'Fill selection');
        }
        return;
      }

      case 'gradient': {
        addLayer({
          ...baseLayer('gradient', nextName('Gradient')),
          x: 0, y: 0, width: size, height: size,
          color, color2: '#000000', gradientType, angle: 90,
        }, 'Add gradient layer');
        return;
      }

      case 'text': {
        addLayer({
          ...baseLayer('text', nextName('Text')),
          x: point.x, y: point.y,
          text: 'BITIRIM', color, fontFamily: 'Segoe UI', fontSize: 56,
          bold: true, italic: false, align: 'center',
          outlineColor: '#000000', outlineWidth: 0, letterSpacing: 0,
        }, 'Add text layer');
        setTool('select');
        return;
      }

      case 'line':
      case 'rectangle':
      case 'ellipse':
      case 'marquee':
      case 'lasso': {
        dragRef.current = {
          kind: tool === 'marquee' || tool === 'lasso' ? 'select' : 'shape',
          startX: point.x, startY: point.y,
        };
        if (tool === 'lasso') setSelection({ kind: 'lasso', points: [point.x, point.y] });
        else setDragBox({ x: point.x, y: point.y, width: 0, height: 0 });
        return;
      }

      case 'brush':
      case 'eraser': {
        let layerId = selected?.kind === 'brush' && !selected.locked ? selected.id : null;

        if (tool === 'eraser' && !layerId) {
          toast('info', 'Select a brush layer to erase from.',
            'The eraser removes paint from one layer rather than cutting a hole '
            + 'through the garment.');
          return;
        }

        if (!layerId) {
          const created = { ...baseLayer('brush', nextName('Brush')), strokes: [] as BrushStroke[] };
          layerId = created.id;
          addLayer(created, 'Add brush layer');
        }

        const stroke: BrushStroke = {
          color,
          size: brushSize,
          erase: tool === 'eraser',
          softness,
          points: [point.x, point.y],
          ...(selection ? { clip: toClip(selection) } : {}),
        } as BrushStroke;

        strokeRef.current = { layerId, stroke };
        setLiveStroke({ layerId, stroke });
        setDirty(true);
        return;
      }

      case 'select': {
        const handle = hitHandle(point);
        if (handle && selected && !selected.locked) {
          dragRef.current = {
            kind: 'transform', startX: point.x, startY: point.y,
            handle, origin: { ...selected },
          };
          return;
        }

        // Topmost transformable layer under the cursor.
        const hit = [...layers].find((l) => {
          if (l.locked || !l.visible || !measureRef.current) return false;
          const box = layerBounds(l, measureRef.current);
          return box
            && point.x >= box.x && point.x <= box.x + box.width
            && point.y >= box.y && point.y <= box.y + box.height;
        });
        selectLayer(hit?.id ?? null);
        return;
      }

      default:
        break;
    }
  };

  const onPointerMove = (e: React.PointerEvent<HTMLCanvasElement>) => {
    const drag = dragRef.current;

    if (drag?.kind === 'pan') {
      setPan((p) => ({ x: p.x + (e.clientX - drag.startX), y: p.y + (e.clientY - drag.startY) }));
      drag.startX = e.clientX;
      drag.startY = e.clientY;
      return;
    }

    const point = canvasPoint(e);

    if (strokeRef.current) {
      const stroke = strokeRef.current.stroke;
      const last = stroke.points.length;
      // Skip points the pointer barely moved through: fewer points means a
      // smaller project file and an identical line.
      if (last < 2
        || Math.hypot(point.x - stroke.points[last - 2], point.y - stroke.points[last - 1]) > 1.2) {
        stroke.points.push(point.x, point.y);
        setLiveStroke({ ...strokeRef.current, stroke: { ...stroke } });
      }
      return;
    }

    if (!drag) return;

    if (drag.kind === 'shape' || drag.kind === 'select') {
      if (tool === 'lasso') {
        setSelection((current) =>
          current?.kind === 'lasso'
            ? { kind: 'lasso', points: [...current.points, point.x, point.y] }
            : { kind: 'lasso', points: [point.x, point.y] });
        return;
      }

      setDragBox({
        x: Math.min(drag.startX, point.x),
        y: Math.min(drag.startY, point.y),
        width: Math.abs(point.x - drag.startX),
        height: Math.abs(point.y - drag.startY),
      });
      return;
    }

    if (drag.kind === 'transform' && drag.origin && selected) {
      const dx = point.x - drag.startX;
      const dy = point.y - drag.startY;
      const origin = drag.origin;

      const patch: Partial<TextureLayer> = {};
      switch (drag.handle) {
        case 'move':
          patch.x = origin.x + dx;
          patch.y = origin.y + dy;
          break;
        case 'se':
          patch.width = Math.max(4, origin.width + dx);
          patch.height = Math.max(4, origin.height + dy);
          break;
        case 'nw':
          patch.x = origin.x + dx;
          patch.y = origin.y + dy;
          patch.width = Math.max(4, origin.width - dx);
          patch.height = Math.max(4, origin.height - dy);
          break;
        case 'ne':
          patch.y = origin.y + dy;
          patch.width = Math.max(4, origin.width + dx);
          patch.height = Math.max(4, origin.height - dy);
          break;
        case 'sw':
          patch.x = origin.x + dx;
          patch.width = Math.max(4, origin.width - dx);
          patch.height = Math.max(4, origin.height + dy);
          break;
        case 'rotate': {
          const cx = origin.x + origin.width / 2;
          const cy = origin.y + origin.height / 2;
          const angle = (Math.atan2(point.y - cy, point.x - cx) * 180) / Math.PI + 90;
          patch.rotation = e.shiftKey ? Math.round(angle / 15) * 15 : Math.round(angle);
          break;
        }
        default:
          break;
      }

      // One undo entry per drag, not one per pointer event.
      mutateLayers((list) => {
        const target = list.find((l) => l.id === selected.id);
        if (target) Object.assign(target, patch);
      }, `Transform ${selected.name}`, `transform:${selected.id}`);
    }
  };

  const onPointerUp = () => {
    if (strokeRef.current) {
      const { layerId, stroke } = strokeRef.current;
      strokeRef.current = null;
      setLiveStroke(null);

      if (stroke.points.length >= 2) {
        mutateLayers((list) => {
          const layer = list.find((l) => l.id === layerId);
          if (!layer) return;
          layer.strokes = [...(layer.strokes ?? []), stroke];
        }, stroke.erase ? 'Erase' : 'Brush stroke');
      }
      return;
    }

    const drag = dragRef.current;
    dragRef.current = null;

    if (drag?.kind === 'shape' && dragBox && dragBox.width > 2 && dragBox.height > 2) {
      const kindName = tool === 'line' ? 'Line' : tool === 'ellipse' ? 'Ellipse' : 'Rectangle';
      addLayer({
        ...baseLayer('shape', nextName(kindName)),
        x: dragBox.x, y: dragBox.y, width: dragBox.width, height: dragBox.height,
        shape: tool === 'line' ? 'line' : tool === 'ellipse' ? 'ellipse' : 'rectangle',
        color,
        filled: tool === 'line' ? false : shapeFilled,
        strokeColor: color,
        strokeWidth: tool === 'line' || !shapeFilled ? strokeWidth : 0,
      }, `Add ${kindName.toLowerCase()} layer`);
      setTool('select');
    }

    if (drag?.kind === 'select' && dragBox && dragBox.width > 2 && dragBox.height > 2) {
      setSelection({ kind: 'rect', ...dragBox });
    }

    setDragBox(null);
  };

  /* --------------------------------------------------------------- image */

  const importImage = async () => {
    try {
      const picked = await call<{ paths: string[] } | null>('asset.pickFile', { kind: 'image' });
      if (!picked?.paths?.length) return;

      const { relativePath } = await call<{ relativePath: string }>('asset.importImage',
        { path: picked.paths[0] });
      const { dataUrl } = await call<{ dataUrl: string }>('asset.readImage', { relativePath });

      const img = new Image();
      await new Promise<void>((resolve, reject) => {
        img.onload = () => resolve();
        img.onerror = () => reject(new Error('decode failed'));
        img.src = dataUrl;
      });

      // Fit to at most half the canvas, keeping aspect.
      const scale = Math.min(1, (size * 0.5) / Math.max(img.naturalWidth, img.naturalHeight));
      const w = Math.round(img.naturalWidth * scale);
      const h = Math.round(img.naturalHeight * scale);
      const id = newId();
      images.current.set(id, img);

      addLayer({
        ...baseLayer('image', picked.paths[0].split(/[\\/]/).pop() ?? 'Image'),
        id,
        x: (size - w) / 2, y: (size - h) / 2,
        width: w, height: h,
        source: relativePath,
      }, 'Import image layer');
      setTool('select');
    } catch (err) {
      reportError(err, 'The image could not be imported.');
    }
  };

  /* ----------------------------------------------------------- transform */

  const flip = (axis: 'h' | 'v') => {
    if (!selected) return;
    mutateLayers((list) => {
      const target = list.find((l) => l.id === selected.id);
      if (!target) return;
      // Negative extents flip the drawn content about the layer's own centre.
      if (axis === 'h') { target.x += target.width; target.width = -target.width; }
      else { target.y += target.height; target.height = -target.height; }
    }, `Flip ${selected.name} ${axis === 'h' ? 'horizontally' : 'vertically'}`);
  };

  /* ---------------------------------------------------------------- view */

  const fit = useCallback(() => {
    const stage = stageRef.current;
    if (!stage) return;
    const usable = Math.min(stage.clientWidth, stage.clientHeight) - 32;
    setZoom(Math.max(0.1, usable / size));
    setPan({ x: 0, y: 0 });
  }, [size]);

  useEffect(() => { fit(); }, [fit]);

  const onWheel = (e: React.WheelEvent) => {
    if (!e.ctrlKey && !e.metaKey) return;
    e.preventDefault();
    setZoom((z) => Math.max(0.1, Math.min(8, z * (e.deltaY < 0 ? 1.12 : 0.89))));
  };

  /* -------------------------------------------------------- tool hotkeys */

  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (e.ctrlKey || e.metaKey || e.altKey) return;
      const target = e.target as HTMLElement;
      if (target.tagName === 'INPUT' || target.tagName === 'TEXTAREA') return;

      const match = TOOLS.find((t) => t.key && t.key.toLowerCase() === e.key.toLowerCase());
      if (match) {
        e.preventDefault();
        if (match.id === 'image') void importImage();
        else setTool(match.id);
        return;
      }

      if (e.key === 'Escape' && selection) { setSelection(null); e.preventDefault(); }
      if (e.key === '[') setBrushSize((s) => Math.max(1, s - 4));
      if (e.key === ']') setBrushSize((s) => Math.min(256, s + 4));
    };

    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [selection]);

  /* ---------------------------------------------------------------- save */

  const saveVariation = async () => {
    if (!variation) return;
    try {
      // The editing canvas shows the mask tinted over the design, which is
      // neither of the two images that ship. Both are composed fresh here.
      const colour = document.createElement('canvas');
      composite(colour, { size, layers, images: images.current, base: baseImage.current, mode: 'color' });
      const dataUrl = colour.toDataURL('image/png');

      const marked = layers.some((l) => l.glow && l.visible && l.kind !== 'group');
      let glowDataUrl: string | undefined;
      if (marked) {
        const mask = document.createElement('canvas');
        composite(mask, { size, layers, images: images.current, base: null, mode: 'glow' });
        glowDataUrl = mask.toDataURL('image/png');
      }

      await call('texture.saveComposite', { variationId: variation.id, dataUrl, glowDataUrl });
      // The host recorded a texture path against the variation; pull the state
      // back so the layers panel stops showing it as unsaved.
      refreshProject(await call('project.current'));
      setDirty(false);
      toast('success', `Saved texture for "${variation.name}".`);
    } catch (err) {
      reportError(err, 'The texture could not be saved.');
    }
  };

  const toolHint = useMemo(() => TOOLS.find((t) => t.id === tool)?.hint ?? '', [tool]);

  if (!projectState.open || !variation) {
    return (
      <EmptyState
        title="No variation selected"
        detail="Create or open a project, then pick a texture variation to start painting."
      />
    );
  }

  const shapeTool = tool === 'rectangle' || tool === 'ellipse' || tool === 'line';

  return (
    <div className="texture-editor">
      <div className="te-toolbar">
        <div className="te-tools">
          {TOOLS.map((t) => (
            <button
              key={t.id}
              className={`te-tool${tool === t.id ? ' active' : ''}`}
              onClick={() => (t.id === 'image' ? importImage() : setTool(t.id))}
              title={t.key ? `${t.label} (${t.key}) — ${t.hint}` : `${t.label} — ${t.hint}`}
            >
              <span className="te-glyph">{t.glyph}</span>
            </button>
          ))}
        </div>

        <span className="grow" />

        <Button size="sm" variant={dirty ? 'primary' : 'subtle'} onClick={saveVariation}>
          {dirty ? 'Save texture *' : 'Save texture'}
        </Button>
      </div>

      {/* Context strip: only the options the active tool actually uses. */}
      <div className="te-context">
        <ColorSwatch
          value={color}
          onChange={setColor}
          onPickFromCanvas={() => setTool('picker')}
          eyedropperActive={tool === 'picker'}
        />
        <span className="mono dim">{color}</span>

        {(tool === 'brush' || tool === 'eraser') && (
          <>
            <span className="vt-sep" />
            <label className="te-inline" title="Brush size in pixels">
              <span className="dim">Size</span>
              <input
                type="range" min={1} max={256} value={brushSize}
                onChange={(e) => setBrushSize(Number(e.target.value))}
                style={{ width: 100 }}
              />
              <span className="mono dim" style={{ width: 28 }}>{brushSize}</span>
            </label>
            <label className="te-inline" title="Edge softness">
              <span className="dim">Soft</span>
              <input
                type="range" min={0} max={1} step={0.05} value={softness}
                onChange={(e) => setSoftness(Number(e.target.value))}
                style={{ width: 70 }}
              />
            </label>
          </>
        )}

        {shapeTool && (
          <>
            <span className="vt-sep" />
            {tool !== 'line' && (
              <button
                className={`chip${shapeFilled ? ' active' : ''}`}
                onClick={() => setShapeFilled((v) => !v)}
              >
                {shapeFilled ? 'Filled' : 'Outline'}
              </button>
            )}
            <label className="te-inline" title="Outline width">
              <span className="dim">Width</span>
              <input
                type="range" min={1} max={40} value={strokeWidth}
                onChange={(e) => setStrokeWidth(Number(e.target.value))}
                style={{ width: 70 }}
              />
              <span className="mono dim" style={{ width: 20 }}>{strokeWidth}</span>
            </label>
          </>
        )}

        {tool === 'gradient' && (
          <>
            <span className="vt-sep" />
            {(['linear', 'radial'] as const).map((g) => (
              <button
                key={g}
                className={`chip${gradientType === g ? ' active' : ''}`}
                onClick={() => setGradientType(g)}
              >{g}</button>
            ))}
          </>
        )}

        {tool === 'select' && selected && (
          <>
            <span className="vt-sep" />
            <button className="chip" onClick={() => flip('h')} title="Flip horizontally">⇋</button>
            <button className="chip" onClick={() => flip('v')} title="Flip vertically">⇵</button>
          </>
        )}

        <span className="vt-sep" />
        <span className="te-hint dim nowrap">{toolHint}</span>

        <span className="grow" />

        {selection && (
          <Badge kind="accent" title="Painting is confined to the selection">
            selection
          </Badge>
        )}
        {selection && (
          <button className="chip" onClick={() => setSelection(null)} title="Clear selection (Esc)">
            clear
          </button>
        )}
        {baseState === 'none' && (
          <span className="te-inline dim" title="Painting starts from a neutral grey instead">
            No base texture
          </span>
        )}
      </div>

      <div className="te-stage" ref={stageRef} onWheel={onWheel}>
        <canvas
          ref={canvasRef}
          className={`te-canvas tool-${tool}`}
          style={{
            width: size * zoom,
            height: size * zoom,
            transform: `translate(${pan.x}px, ${pan.y}px)`,
          }}
          onPointerDown={onPointerDown}
          onPointerMove={onPointerMove}
          onPointerUp={onPointerUp}
          onPointerLeave={onPointerUp}
        />
      </div>

      <div className="te-viewbar">
        <button className="mini" onClick={() => setZoom((z) => Math.max(0.1, z * 0.8))}
                title="Zoom out">−</button>
        <span className="mono dim" style={{ width: 46, textAlign: 'center' }}>
          {Math.round(zoom * 100)}%
        </span>
        <button className="mini" onClick={() => setZoom((z) => Math.min(8, z * 1.25))}
                title="Zoom in">＋</button>
        <button className="mini" onClick={fit} title="Fit to window">Fit</button>
        <button className="mini" onClick={() => { setZoom(1); setPan({ x: 0, y: 0 }); }}
                title="Actual size">100%</button>

        <span className="vt-sep" />
        <button
          className={`mini${checkerboard ? ' active' : ''}`}
          onClick={() => setCheckerboard((v) => !v)}
          title="Show the transparency checkerboard"
        >▦</button>

        {glowMode && (
          <button
            className={`mini${showUnderlay ? ' active' : ''}`}
            onClick={() => setShowUnderlay((v) => !v)}
            title={showUnderlay
              ? 'Hide the garment behind the mask'
              : 'Ghost the garment behind the mask so a shape can be traced over it'}
          >👕</button>
        )}

        <span className="grow" />
        <span className="dim" style={{ fontSize: 'var(--fs-xs)' }}>
          {size}×{size} · {layers.length} layer{layers.length === 1 ? '' : 's'}
          {' · Alt-drag to pan · Ctrl+wheel to zoom'}
        </span>
      </div>
    </div>
  );
}

/* ---------------------------------------------------------------- helpers */

function selectionBounds(selection: Selection, size: number) {
  if (!selection) return null;
  if (selection.kind === 'lasso') {
    const xs = selection.points.filter((_, i) => i % 2 === 0);
    const ys = selection.points.filter((_, i) => i % 2 === 1);
    if (xs.length === 0) return null;
    const x = Math.min(...xs), y = Math.min(...ys);
    return { x, y, width: Math.max(...xs) - x, height: Math.max(...ys) - y };
  }
  return {
    x: Math.max(0, selection.x),
    y: Math.max(0, selection.y),
    width: Math.min(size, selection.width),
    height: Math.min(size, selection.height),
  };
}

function toClip(selection: NonNullable<Selection>) {
  return selection.kind === 'lasso'
    ? { kind: 'lasso' as const, points: selection.points }
    : {
      kind: selection.kind,
      x: selection.x, y: selection.y,
      width: selection.width, height: selection.height,
    };
}

function drawSelection(ctx: CanvasRenderingContext2D, selection: NonNullable<Selection>) {
  ctx.save();
  ctx.strokeStyle = '#ffffff';
  ctx.lineWidth = 1;
  ctx.setLineDash([4, 3]);

  ctx.beginPath();
  if (selection.kind === 'lasso') {
    const p = selection.points;
    if (p.length >= 2) {
      ctx.moveTo(p[0], p[1]);
      for (let i = 2; i < p.length; i += 2) ctx.lineTo(p[i], p[i + 1]);
      ctx.closePath();
    }
  } else if (selection.kind === 'ellipse') {
    ctx.ellipse(
      selection.x + selection.width / 2, selection.y + selection.height / 2,
      selection.width / 2, selection.height / 2, 0, 0, Math.PI * 2);
  } else {
    ctx.rect(selection.x, selection.y, selection.width, selection.height);
  }
  ctx.stroke();
  ctx.restore();
}

function drawHandles(
  ctx: CanvasRenderingContext2D,
  box: { x: number; y: number; width: number; height: number },
  rotation: number,
) {
  ctx.save();
  ctx.strokeStyle = '#a8e10c';
  ctx.lineWidth = 1.5;
  ctx.setLineDash([5, 4]);
  ctx.strokeRect(box.x, box.y, box.width, box.height);

  ctx.setLineDash([]);
  ctx.fillStyle = '#a8e10c';
  const corners = [
    [box.x, box.y],
    [box.x + box.width, box.y],
    [box.x + box.width, box.y + box.height],
    [box.x, box.y + box.height],
  ];
  for (const [x, y] of corners) ctx.fillRect(x - 4, y - 4, 8, 8);

  // Rotation grip above the top edge.
  const gx = box.x + box.width / 2;
  const gy = box.y - 26;
  ctx.beginPath();
  ctx.moveTo(gx, box.y);
  ctx.lineTo(gx, gy);
  ctx.stroke();
  ctx.beginPath();
  ctx.arc(gx, gy, 5, 0, Math.PI * 2);
  ctx.fill();

  if (rotation) {
    ctx.fillStyle = '#a8e10c';
    ctx.font = '11px monospace';
    ctx.fillText(`${Math.round(rotation)}°`, gx + 10, gy + 4);
  }

  ctx.restore();
}
