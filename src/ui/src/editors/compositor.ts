import type { BrushStroke, StrokeClip, TextureLayer } from '../state/store';

/**
 * Deterministic layer compositor.
 *
 * The same stack always produces the same pixels, which is what lets the
 * viewport preview, the saved PNG and the exported YTD agree with each other.
 *
 * Everything a layer needs is in the layer: brush strokes are replayed from
 * recorded points rather than read out of a canvas that only exists while the
 * app is running. That is what makes a reopened project look identical, and
 * what lets undo step through a paint session.
 */
export interface CompositeInputs {
  size: number;
  layers: TextureLayer[];
  /** Resolved image elements for image layers, keyed by layer id. */
  images: Map<string, HTMLImageElement>;
  /** Drawn under everything; usually the source diffuse. */
  base?: HTMLImageElement | HTMLCanvasElement | null;
  /**
   * A stroke being drawn right now, not yet committed to the document. Painted
   * on top of its layer so the canvas keeps up with the pointer.
   */
  liveStroke?: { layerId: string; stroke: BrushStroke } | null;
  /**
   * Which of the two things a variation holds is being drawn.
   *
   * A neon garment carries a colour and a glow mask, painted with the same
   * tools on the same canvas but kept apart: glow layers never reach the
   * colour, and colour layers never reach the mask. `preview` is the editing
   * view -- colour, with the mask tinted over it so you can see the shape you
   * are painting without mistaking it for paint.
   */
  mode?: 'preview' | 'color' | 'glow' | 'glow-edit';
  /** `glow-edit` only: ghost the garment under the mask. Defaults to on. */
  underlay?: boolean;
}

/** Tint glow layers wear in the editing view. Nothing exports this colour. */
const GLOW_TINT = '#35d6ff';

export function composite(target: HTMLCanvasElement, inputs: CompositeInputs): void {
  const { size, base } = inputs;
  if (target.width !== size) target.width = size;
  if (target.height !== size) target.height = size;

  const ctx = target.getContext('2d');
  if (!ctx) return;

  ctx.clearRect(0, 0, size, size);
  ctx.globalCompositeOperation = 'source-over';
  ctx.globalAlpha = 1;
  ctx.imageSmoothingQuality = 'high';

  // The mask is drawn on black: unpainted means "does not glow", and black is
  // the only value that says so. The garment's own texture must not show
  // through -- its brightness would become glow nobody asked for.
  if (inputs.mode === 'glow' || inputs.mode === 'glow-edit') {
    ctx.fillStyle = '#000000';
    ctx.fillRect(0, 0, size, size);

    // While editing, the garment is ghosted underneath so a shape can be
    // traced over the artwork it is meant to light. It is faint on purpose and
    // never reaches the exported mask, which is composed separately.
    if (inputs.mode === 'glow-edit' && inputs.underlay !== false) {
      // Composed whole, then faded as one image. Fading it in place does not
      // work: every layer sets its own globalAlpha, so an outer value is
      // overwritten by the first layer and the ghost comes out at full
      // strength -- which reads as the garment, not as a tracing aid.
      const ghost = document.createElement('canvas');
      ghost.width = ghost.height = size;
      const inner = ghost.getContext('2d');
      if (inner) {
        if (base) inner.drawImage(base, 0, 0, size, size);
        paintLevel(inner, null, { ...inputs, mode: 'color' });
        ctx.save();
        ctx.globalAlpha = 0.28;
        ctx.drawImage(ghost, 0, 0);
        ctx.restore();
      }
    }

    paintLevel(ctx, null, { ...inputs, mode: 'glow' });
    return;
  }

  if (base) {
    ctx.drawImage(base, 0, 0, size, size);
  } else {
    // No source texture: a neutral mid-grey, not white, so a user can tell
    // "nothing loaded" apart from "a white garment".
    ctx.fillStyle = '#8a8a8a';
    ctx.fillRect(0, 0, size, size);
  }

  paintLevel(ctx, null, inputs);
}

/**
 * Paints every layer whose parent is `parentId`, bottom of the list first.
 *
 * Groups render into their own canvas so the group's opacity and blend mode
 * apply to the composed result rather than to each child in turn -- which is
 * the behaviour anyone who has used a layered editor expects, and the only one
 * that makes a group worth having.
 */
function paintLevel(
  ctx: CanvasRenderingContext2D,
  parentId: string | null,
  inputs: CompositeInputs,
): void {
  const { size, layers } = inputs;
  const level = layers.filter((l) => (l.parentId ?? null) === parentId);

  const mode = inputs.mode ?? 'preview';

  for (let i = level.length - 1; i >= 0; i--) {
    const layer = level[i];
    if (!layer.visible || layer.opacity <= 0) continue;

    // A layer belongs to exactly one of the two images. Groups are never
    // filtered: a group's children decide for themselves, so a group can hold
    // both a printed design and the mask that lights part of it.
    const glow = layer.glow === true && layer.kind !== 'group';
    if (mode === 'color' && glow) continue;
    if (mode === 'glow' && !glow && layer.kind !== 'group') continue;

    ctx.save();

    // In the editing view the mask is shown, not applied: a flat tint so the
    // shape is legible over any design and cannot be mistaken for paint.
    if (mode === 'preview' && glow) {
      const scratch = document.createElement('canvas');
      scratch.width = scratch.height = size;
      const inner = scratch.getContext('2d');
      if (inner) {
        paintOne(inner, layer, inputs);
        inner.globalCompositeOperation = 'source-in';
        inner.fillStyle = GLOW_TINT;
        inner.fillRect(0, 0, size, size);
        ctx.globalAlpha = 0.55 * Math.max(0, Math.min(1, layer.opacity));
        ctx.drawImage(scratch, 0, 0);
      }
      ctx.restore();
      continue;
    }
    ctx.globalAlpha = Math.max(0, Math.min(1, layer.opacity));
    ctx.globalCompositeOperation =
      (layer.blendMode as GlobalCompositeOperation) || 'source-over';

    paintOne(ctx, layer, inputs);
    ctx.restore();
  }
}

/** Draws one layer's own content, with alpha and blending already set. */
function paintOne(
  ctx: CanvasRenderingContext2D,
  layer: TextureLayer,
  inputs: CompositeInputs,
): void {
  const { size, images } = inputs;

  {
    switch (layer.kind) {
      case 'group': {
        const scratch = document.createElement('canvas');
        scratch.width = scratch.height = size;
        const inner = scratch.getContext('2d');
        if (inner) {
          paintLevel(inner, layer.id, inputs);
          ctx.drawImage(scratch, 0, 0);
        }
        break;
      }

      case 'fill': {
        ctx.fillStyle = layer.color ?? '#ffffff';
        ctx.fillRect(0, 0, size, size);
        break;
      }

      case 'gradient': {
        paintGradient(ctx, layer, size);
        break;
      }

      case 'brush': {
        const live = inputs.liveStroke?.layerId === layer.id ? inputs.liveStroke.stroke : null;
        paintStrokes(ctx, layer.strokes ?? [], size, live);
        break;
      }

      case 'shape': {
        paintShape(ctx, layer);
        break;
      }

      case 'image':
      case 'generated': {
        const img = images.get(layer.id);
        if (img?.complete && img.naturalWidth > 0) {
          drawTransformed(ctx, layer, (w, h) => ctx.drawImage(img, -w / 2, -h / 2, w, h));
        }
        break;
      }

      case 'text': {
        drawText(ctx, layer);
        break;
      }

      default:
        break;
    }
  }
}

/**
 * Replays recorded strokes.
 *
 * Erasing uses `destination-out` against a scratch surface rather than against
 * the whole composite: an eraser stroke must remove what this layer painted,
 * not punch a hole through the garment underneath it.
 */
function paintStrokes(
  ctx: CanvasRenderingContext2D,
  strokes: BrushStroke[],
  size: number,
  live: BrushStroke | null,
): void {
  const all = live ? [...strokes, live] : strokes;
  if (all.length === 0) return;

  const scratch = document.createElement('canvas');
  scratch.width = scratch.height = size;
  const paint = scratch.getContext('2d');
  if (!paint) return;

  paint.lineCap = 'round';
  paint.lineJoin = 'round';

  for (const stroke of all) {
    const points = stroke.points;
    if (points.length < 2) continue;

    paint.save();
    if (stroke.clip) applyClip(paint, stroke.clip);

    paint.globalCompositeOperation = stroke.erase ? 'destination-out' : 'source-over';
    paint.strokeStyle = stroke.color;
    paint.fillStyle = stroke.color;
    paint.lineWidth = stroke.size;

    // A soft brush is drawn as a shadowed stroke: it costs one pass instead of
    // per-dab radial gradients, and reads the same at these sizes.
    if (stroke.softness > 0) {
      paint.shadowBlur = stroke.size * stroke.softness * 0.9;
      paint.shadowColor = stroke.erase ? '#000000' : stroke.color;
    } else {
      paint.shadowBlur = 0;
    }

    if (points.length === 2) {
      paint.beginPath();
      paint.arc(points[0], points[1], stroke.size / 2, 0, Math.PI * 2);
      paint.fill();
      paint.restore();
      continue;
    }

    paint.beginPath();
    paint.moveTo(points[0], points[1]);
    for (let i = 2; i < points.length; i += 2) paint.lineTo(points[i], points[i + 1]);
    paint.stroke();
    paint.restore();
  }

  paint.shadowBlur = 0;
  ctx.drawImage(scratch, 0, 0);
}

/** Confines painting to the region the stroke was drawn inside. */
function applyClip(ctx: CanvasRenderingContext2D, clip: StrokeClip): void {
  ctx.beginPath();

  if (clip.kind === 'lasso') {
    const p = clip.points ?? [];
    if (p.length < 6) return;   // fewer than three points is not an area
    ctx.moveTo(p[0], p[1]);
    for (let i = 2; i < p.length; i += 2) ctx.lineTo(p[i], p[i + 1]);
    ctx.closePath();
  } else {
    const x = clip.x ?? 0;
    const y = clip.y ?? 0;
    const w = clip.width ?? 0;
    const h = clip.height ?? 0;
    if (w <= 0 || h <= 0) return;

    if (clip.kind === 'ellipse') ctx.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
    else ctx.rect(x, y, w, h);
  }

  ctx.clip();
}

function paintGradient(ctx: CanvasRenderingContext2D, layer: TextureLayer, size: number): void {
  const from = layer.color ?? '#000000';
  const to = layer.color2 ?? '#ffffff';

  let gradient: CanvasGradient;
  if (layer.gradientType === 'radial') {
    gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  } else {
    const radians = ((layer.angle ?? 0) * Math.PI) / 180;
    const half = size / 2;
    const dx = Math.cos(radians) * half;
    const dy = Math.sin(radians) * half;
    gradient = ctx.createLinearGradient(half - dx, half - dy, half + dx, half + dy);
  }

  gradient.addColorStop(0, from);
  gradient.addColorStop(1, to);
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, size, size);
}

function paintShape(ctx: CanvasRenderingContext2D, layer: TextureLayer): void {
  const w = layer.width || 1;
  const h = layer.height || 1;

  ctx.translate(layer.x + w / 2, layer.y + h / 2);
  if (layer.rotation) ctx.rotate((layer.rotation * Math.PI) / 180);

  ctx.beginPath();
  switch (layer.shape) {
    case 'ellipse':
      ctx.ellipse(0, 0, Math.abs(w) / 2, Math.abs(h) / 2, 0, 0, Math.PI * 2);
      break;
    case 'line':
      ctx.moveTo(-w / 2, -h / 2);
      ctx.lineTo(w / 2, h / 2);
      break;
    default:
      ctx.rect(-w / 2, -h / 2, w, h);
      break;
  }

  if (layer.filled !== false && layer.shape !== 'line') {
    ctx.fillStyle = layer.color ?? '#ffffff';
    ctx.fill();
  }

  const strokeWidth = layer.strokeWidth ?? (layer.shape === 'line' ? 4 : 0);
  if (strokeWidth > 0) {
    ctx.lineWidth = strokeWidth;
    ctx.lineCap = 'round';
    ctx.strokeStyle = layer.strokeColor ?? layer.color ?? '#000000';
    ctx.stroke();
  }
}

function drawTransformed(
  ctx: CanvasRenderingContext2D,
  layer: TextureLayer,
  paint: (w: number, h: number) => void,
) {
  const w = layer.width || 1;
  const h = layer.height || 1;
  ctx.translate(layer.x + w / 2, layer.y + h / 2);
  if (layer.rotation) ctx.rotate((layer.rotation * Math.PI) / 180);
  paint(w, h);
}

function drawText(ctx: CanvasRenderingContext2D, layer: TextureLayer) {
  const text = layer.text ?? '';
  if (!text) return;

  const fontSize = layer.fontSize ?? 64;
  const weight = layer.bold ? '700' : '400';
  const style = layer.italic ? 'italic' : 'normal';
  const family = layer.fontFamily ?? 'Segoe UI';

  ctx.font = `${style} ${weight} ${fontSize}px "${family}", sans-serif`;
  ctx.textAlign = (layer.align as CanvasTextAlign) ?? 'center';
  ctx.textBaseline = 'middle';

  ctx.translate(layer.x, layer.y);
  if (layer.rotation) ctx.rotate((layer.rotation * Math.PI) / 180);

  const spacing = layer.letterSpacing ?? 0;
  const paint = (fn: (ch: string, x: number) => void) => {
    if (!spacing) {
      fn(text, 0);
      return;
    }
    // Manual letter spacing: canvas letterSpacing is not universally available
    // in the embedded runtime, and this keeps output identical everywhere.
    const widths = [...text].map((ch) => ctx.measureText(ch).width);
    const total = widths.reduce((a, b) => a + b, 0) + spacing * (text.length - 1);
    let x = ctx.textAlign === 'center' ? -total / 2 : ctx.textAlign === 'right' ? -total : 0;
    const previousAlign = ctx.textAlign;
    ctx.textAlign = 'left';
    [...text].forEach((ch, i) => {
      fn(ch, x);
      x += widths[i] + spacing;
    });
    ctx.textAlign = previousAlign;
  };

  if (layer.shadowBlur && layer.shadowBlur > 0) {
    ctx.shadowBlur = layer.shadowBlur;
    ctx.shadowColor = layer.shadowColor ?? 'rgba(0,0,0,0.7)';
  }

  if (layer.outlineWidth && layer.outlineWidth > 0) {
    ctx.lineWidth = layer.outlineWidth;
    ctx.strokeStyle = layer.outlineColor ?? '#000000';
    ctx.lineJoin = 'round';
    paint((ch, x) => ctx.strokeText(ch, x, 0));
  }

  ctx.fillStyle = layer.color ?? '#ffffff';
  paint((ch, x) => ctx.fillText(ch, x, 0));
  ctx.shadowBlur = 0;
}

/** Draws the checkerboard that shows through transparent pixels. */
export function paintCheckerboard(ctx: CanvasRenderingContext2D, w: number, h: number, cell = 12) {
  for (let y = 0; y < h; y += cell) {
    for (let x = 0; x < w; x += cell) {
      const even = ((x / cell) | 0) % 2 === ((y / cell) | 0) % 2;
      ctx.fillStyle = even ? '#2a2d26' : '#22251f';
      ctx.fillRect(x, y, cell, cell);
    }
  }
}

/**
 * The bounding box a layer occupies, for hit-testing and transform handles.
 *
 * Returns null for layers that cover the whole canvas or have no meaningful
 * box of their own -- there is nothing useful to drag on a full-canvas fill.
 */
export function layerBounds(
  layer: TextureLayer,
  ctx: CanvasRenderingContext2D,
): { x: number; y: number; width: number; height: number } | null {
  switch (layer.kind) {
    case 'image':
    case 'generated':
    case 'shape':
      return { x: layer.x, y: layer.y, width: layer.width, height: layer.height };

    case 'text': {
      const fontSize = layer.fontSize ?? 64;
      ctx.font = `${layer.italic ? 'italic' : 'normal'} ${layer.bold ? 700 : 400} `
        + `${fontSize}px "${layer.fontFamily ?? 'Segoe UI'}", sans-serif`;
      const width = ctx.measureText(layer.text ?? '').width
        + (layer.letterSpacing ?? 0) * Math.max(0, (layer.text ?? '').length - 1);
      const height = fontSize * 1.2;
      const align = layer.align ?? 'center';
      const x = align === 'center' ? layer.x - width / 2
        : align === 'right' ? layer.x - width
          : layer.x;
      return { x, y: layer.y - height / 2, width, height };
    }

    default:
      return null;
  }
}
