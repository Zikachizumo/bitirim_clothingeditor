import { useEffect, useLayoutEffect, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import './colorpicker.css';

/* ------------------------------------------------------------------ colour */

export function hexToRgb(hex: string): { r: number; g: number; b: number } {
  const clean = hex.replace('#', '');
  const full = clean.length === 3
    ? clean.split('').map((c) => c + c).join('')
    : clean.padEnd(6, '0').slice(0, 6);
  return {
    r: parseInt(full.slice(0, 2), 16),
    g: parseInt(full.slice(2, 4), 16),
    b: parseInt(full.slice(4, 6), 16),
  };
}

export function rgbToHex(r: number, g: number, b: number): string {
  const to = (v: number) => Math.max(0, Math.min(255, Math.round(v))).toString(16).padStart(2, '0');
  return `#${to(r)}${to(g)}${to(b)}`;
}

export function rgbToHsv(r: number, g: number, b: number) {
  const rn = r / 255, gn = g / 255, bn = b / 255;
  const max = Math.max(rn, gn, bn);
  const min = Math.min(rn, gn, bn);
  const d = max - min;

  let h = 0;
  if (d !== 0) {
    if (max === rn) h = ((gn - bn) / d) % 6;
    else if (max === gn) h = (bn - rn) / d + 2;
    else h = (rn - gn) / d + 4;
    h *= 60;
    if (h < 0) h += 360;
  }

  return { h, s: max === 0 ? 0 : d / max, v: max };
}

export function hsvToRgb(h: number, s: number, v: number) {
  const c = v * s;
  const x = c * (1 - Math.abs(((h / 60) % 2) - 1));
  const m = v - c;

  const [r, g, b] =
    h < 60 ? [c, x, 0]
      : h < 120 ? [x, c, 0]
        : h < 180 ? [0, c, x]
          : h < 240 ? [0, x, c]
            : h < 300 ? [x, 0, c]
              : [c, 0, x];

  return { r: (r + m) * 255, g: (g + m) * 255, b: (b + m) * 255 };
}

/** Palette that ships with the app. Neutrals plus the common garment colours. */
export const PRESET_PALETTE = [
  '#000000', '#1a1a1a', '#333333', '#4d4d4d', '#808080', '#b3b3b3', '#e6e6e6', '#ffffff',
  '#7f1d1d', '#dc2626', '#ea580c', '#d97706', '#ca8a04', '#65a30d', '#16a34a', '#059669',
  '#0891b2', '#0284c7', '#2563eb', '#4f46e5', '#7c3aed', '#c026d3', '#db2777', '#e11d48',
  '#451a03', '#78350f', '#a16207', '#3f2d1a', '#1e3a5f', '#14532d', '#4c1d95', '#831843',
];

const RECENT_KEY = 'bcc.recentColours';

function loadRecent(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_KEY);
    return raw ? JSON.parse(raw) : [];
  } catch {
    return [];
  }
}

export function rememberColour(hex: string): string[] {
  const next = [hex, ...loadRecent().filter((c) => c !== hex)].slice(0, 16);
  try { localStorage.setItem(RECENT_KEY, JSON.stringify(next)); } catch { /* private mode */ }
  return next;
}

/* ------------------------------------------------------------------ swatch */

/**
 * A colour swatch that opens the full picker.
 *
 * `onPickFromCanvas` turns on the eyedropper. It is only offered where there
 * is actually a canvas to sample from, rather than shown disabled everywhere.
 */
export function ColorSwatch({
  value, onChange, title, onPickFromCanvas, eyedropperActive,
}: {
  value: string;
  onChange: (hex: string) => void;
  title?: string;
  onPickFromCanvas?: () => void;
  eyedropperActive?: boolean;
}) {
  const [open, setOpen] = useState(false);
  const [at, setAt] = useState<{ left: number; top: number } | null>(null);
  const anchor = useRef<HTMLDivElement>(null);
  const pop = useRef<HTMLDivElement>(null);

  /*
    The panel is rendered into <body> rather than next to the swatch.

    Every strip that holds a swatch is a fixed-height row that clips what
    overflows it -- the texture editor's context bar is 28px tall with
    `overflow: hidden`, and the inspector scrolls. An absolutely positioned
    panel inside those is simply invisible: it opens, it is clipped, and the
    click looks like it did nothing.
  */
  const place = () => {
    const rect = anchor.current?.getBoundingClientRect();
    if (!rect) return;

    const width = pop.current?.offsetWidth ?? 250;
    const height = pop.current?.offsetHeight ?? 300;
    const margin = 8;

    // Below the swatch when there is room, above it when there is not, and
    // never past either edge of the window.
    const below = rect.bottom + 6;
    const top = below + height + margin > window.innerHeight
      ? Math.max(margin, rect.top - height - 6)
      : below;

    setAt({
      left: Math.max(margin, Math.min(rect.left, window.innerWidth - width - margin)),
      top,
    });
  };

  // Before paint, so the panel never shows up in the wrong place first.
  useLayoutEffect(() => {
    if (open) place();
    else setAt(null);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  useEffect(() => {
    if (!open) return;

    const away = (e: MouseEvent) => {
      const target = e.target as Node;
      // The panel is outside the anchor now, so both count as "inside".
      if (anchor.current?.contains(target)) return;
      if (pop.current?.contains(target)) return;
      setOpen(false);
    };
    const escape = (e: KeyboardEvent) => { if (e.key === 'Escape') setOpen(false); };
    // A panel pinned to the window has to follow the swatch when anything
    // moves, and `true` catches scrolling in any ancestor, not just the page.
    const follow = () => place();

    window.addEventListener('mousedown', away);
    window.addEventListener('keydown', escape);
    window.addEventListener('resize', follow);
    window.addEventListener('scroll', follow, true);
    return () => {
      window.removeEventListener('mousedown', away);
      window.removeEventListener('keydown', escape);
      window.removeEventListener('resize', follow);
      window.removeEventListener('scroll', follow, true);
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open]);

  return (
    <div className="colour-anchor" ref={anchor}>
      <button
        className="colour-swatch"
        style={{ background: value }}
        onClick={() => setOpen((v) => !v)}
        title={title ?? `Colour ${value}`}
      >
        <span className="sr-only">{value}</span>
      </button>

      {open && createPortal(
        <div
          className="colour-pop"
          ref={pop}
          style={{ left: at?.left ?? -9999, top: at?.top ?? -9999 }}
        >
          <ColorPicker
            value={value}
            onChange={onChange}
            onCommit={(hex) => { rememberColour(hex); }}
            onPickFromCanvas={onPickFromCanvas
              ? () => { onPickFromCanvas(); setOpen(false); }
              : undefined}
            eyedropperActive={eyedropperActive}
          />
        </div>,
        document.body,
      )}
    </div>
  );
}

/* ------------------------------------------------------------------ picker */

export function ColorPicker({
  value, onChange, onCommit, onPickFromCanvas, eyedropperActive,
}: {
  value: string;
  onChange: (hex: string) => void;
  onCommit?: (hex: string) => void;
  onPickFromCanvas?: () => void;
  eyedropperActive?: boolean;
}) {
  const rgb = hexToRgb(value);
  const hsv = rgbToHsv(rgb.r, rgb.g, rgb.b);
  const [recent, setRecent] = useState<string[]>(loadRecent);
  const [hexDraft, setHexDraft] = useState(value);
  const field = useRef<HTMLDivElement>(null);

  useEffect(() => setHexDraft(value), [value]);

  const emit = (hex: string) => {
    onChange(hex);
  };

  const commit = (hex: string) => {
    onChange(hex);
    setRecent(rememberColour(hex));
    onCommit?.(hex);
  };

  const fromField = (e: React.PointerEvent) => {
    const rect = field.current?.getBoundingClientRect();
    if (!rect) return;
    const s = Math.max(0, Math.min(1, (e.clientX - rect.left) / rect.width));
    const v = 1 - Math.max(0, Math.min(1, (e.clientY - rect.top) / rect.height));
    const next = hsvToRgb(hsv.h, s, v);
    emit(rgbToHex(next.r, next.g, next.b));
  };

  return (
    <div className="colour-picker">
      <div
        ref={field}
        className="cp-field"
        style={{ background: `linear-gradient(to top, #000, transparent), `
          + `linear-gradient(to right, #fff, hsl(${hsv.h}, 100%, 50%))` }}
        onPointerDown={(e) => {
          (e.currentTarget as HTMLElement).setPointerCapture(e.pointerId);
          fromField(e);
        }}
        onPointerMove={(e) => { if (e.buttons === 1) fromField(e); }}
        onPointerUp={() => commit(value)}
      >
        <span
          className="cp-cursor"
          style={{ left: `${hsv.s * 100}%`, top: `${(1 - hsv.v) * 100}%` }}
        />
      </div>

      <div className="cp-row">
        <input
          className="cp-hue"
          type="range" min={0} max={359} value={Math.round(hsv.h)}
          onChange={(e) => {
            const next = hsvToRgb(Number(e.target.value), hsv.s || 1, hsv.v || 1);
            emit(rgbToHex(next.r, next.g, next.b));
          }}
          onPointerUp={() => commit(value)}
        />
        {onPickFromCanvas && (
          <button
            className={`cp-eyedropper${eyedropperActive ? ' active' : ''}`}
            onClick={onPickFromCanvas}
            title="Pick a colour from the canvas"
          >⌖</button>
        )}
      </div>

      <div className="cp-inputs">
        <label className="cp-field-label">
          <span>HEX</span>
          <input
            value={hexDraft}
            onChange={(e) => {
              setHexDraft(e.target.value);
              if (/^#?[0-9a-fA-F]{6}$/.test(e.target.value.trim())) {
                const normalised = e.target.value.trim().startsWith('#')
                  ? e.target.value.trim()
                  : `#${e.target.value.trim()}`;
                emit(normalised.toLowerCase());
              }
            }}
            onBlur={() => { setHexDraft(value); commit(value); }}
          />
        </label>

        {(['r', 'g', 'b'] as const).map((channel) => (
          <label key={channel} className="cp-field-label narrow">
            <span>{channel.toUpperCase()}</span>
            <input
              type="number" min={0} max={255}
              value={Math.round(rgb[channel])}
              onChange={(e) => {
                const next = { ...rgb, [channel]: Number(e.target.value) };
                emit(rgbToHex(next.r, next.g, next.b));
              }}
              onBlur={() => commit(value)}
            />
          </label>
        ))}
      </div>

      <div className="cp-hsv dim">
        H {Math.round(hsv.h)}&deg; &middot; S {Math.round(hsv.s * 100)}% &middot; V{' '}
        {Math.round(hsv.v * 100)}%
      </div>

      <div className="cp-section">Palette</div>
      <div className="cp-swatches">
        {PRESET_PALETTE.map((c) => (
          <button
            key={c}
            className={`cp-chip${c === value ? ' active' : ''}`}
            style={{ background: c }}
            title={c}
            onClick={() => commit(c)}
          />
        ))}
      </div>

      {recent.length > 0 && (
        <>
          <div className="cp-section">Recent</div>
          <div className="cp-swatches">
            {recent.map((c) => (
              <button
                key={c}
                className={`cp-chip${c === value ? ' active' : ''}`}
                style={{ background: c }}
                title={c}
                onClick={() => commit(c)}
              />
            ))}
          </div>
        </>
      )}
    </div>
  );
}
