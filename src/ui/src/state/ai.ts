import { call } from '../host/bridge';
import { decodeIndices, decodeUvs } from '../panels/SidePanels';
import type { MeshInfo } from './store';

/**
 * The renderer's half of AI texture generation.
 *
 * The API key never comes near this file. Everything here builds the *inputs*
 * -- the UV layout, the UV mask, the aspect ratio -- and hands them to the
 * host, which is the only place that knows how to reach a model.
 *
 * The generated image comes back as a path inside the project and joins the
 * layer stack like any imported file. It is not a separate texture pipeline;
 * it is one more way to get an image into the one that already exists.
 */

export type AiMode = 'auto' | 'generate' | 'edit';

export type AiImageRole = 'currentTexture' | 'uvLayout' | 'uvMask' | 'garmentReference';

export interface AiStatus {
  providerId: string;
  displayName: string;
  model: string;
  configured: boolean;
  /** "environment", ".env" or "settings". Where the key is, never what it is. */
  keySource: string | null;
  reason: string | null;
  resolutions: string[];
  defaultResolution: string;
  busy: boolean;
}

export interface AiGenerateResult {
  relativePath: string;
  dataUrl: string;
  width: number;
  height: number;
  mimeType: string;
  provider: string;
  model: string;
  mode: 'generate' | 'edit';
  elapsedMs: number;
  bytes: number;
}

export interface AiInputImage {
  role: AiImageRole;
  dataUrl: string;
}

export const aiStatus = () => call<AiStatus>('ai.status');

export const aiTestConnection = () =>
  call<{ ok: boolean; message: string; detail: string | null }>('ai.testConnection');

export const aiCancel = () => call<{ cancelled: boolean }>('ai.cancel');

export function aiGenerate(args: {
  prompt: string;
  mode: AiMode;
  resolution: string;
  aspectRatio: string;
  images: AiInputImage[];
}) {
  return call<AiGenerateResult>('ai.generateTexture', args);
}

/* -------------------------------------------------------------- UV renders */

/**
 * How large the UV renders are sent at.
 *
 * Big enough that a seam is a line rather than a smudge, small enough that
 * three of them plus the texture is not a multi-megabyte upload.
 */
const UV_RENDER_SIZE = 1024;

function uvCanvas(): { canvas: HTMLCanvasElement; ctx: CanvasRenderingContext2D } | null {
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = UV_RENDER_SIZE;
  const ctx = canvas.getContext('2d');
  if (!ctx) return null;

  ctx.fillStyle = '#000000';
  ctx.fillRect(0, 0, UV_RENDER_SIZE, UV_RENDER_SIZE);
  return { canvas, ctx };
}

/**
 * The garment's UV islands as a wireframe, white on black.
 *
 * This is the placement map: it tells the model where the seams and panel
 * boundaries are, so a pattern follows the garment's construction instead of
 * running across the square and breaking at every seam.
 */
export function renderUvLayout(mesh: MeshInfo | null): string | null {
  const uvs = decodeUvs(mesh);
  const indices = decodeIndices(mesh);
  if (!uvs || !indices || uvs.every((v) => v === 0)) return null;

  const made = uvCanvas();
  if (!made) return null;
  const { canvas, ctx } = made;

  ctx.strokeStyle = '#ffffff';
  ctx.lineWidth = 1;
  ctx.beginPath();
  for (let i = 0; i + 2 < indices.length; i += 3) {
    const a = indices[i] * 2;
    const b = indices[i + 1] * 2;
    const c = indices[i + 2] * 2;
    ctx.moveTo(uvs[a] * UV_RENDER_SIZE, uvs[a + 1] * UV_RENDER_SIZE);
    ctx.lineTo(uvs[b] * UV_RENDER_SIZE, uvs[b + 1] * UV_RENDER_SIZE);
    ctx.lineTo(uvs[c] * UV_RENDER_SIZE, uvs[c + 1] * UV_RENDER_SIZE);
    ctx.closePath();
  }
  ctx.stroke();

  return canvas.toDataURL('image/png');
}

/**
 * Which parts of the square the garment actually uses: white where it does,
 * black where nothing is mapped.
 *
 * Padding outside the islands is never seen in game, so detail spent there is
 * detail not spent on the garment.
 */
export function renderUvMask(mesh: MeshInfo | null): string | null {
  const uvs = decodeUvs(mesh);
  const indices = decodeIndices(mesh);
  if (!uvs || !indices || uvs.every((v) => v === 0)) return null;

  const made = uvCanvas();
  if (!made) return null;
  const { canvas, ctx } = made;

  ctx.fillStyle = '#ffffff';
  // Also stroked, so the one-pixel gap along shared triangle edges that
  // anti-aliasing leaves does not read as a seam the model should draw.
  ctx.strokeStyle = '#ffffff';
  ctx.lineWidth = 2;

  ctx.beginPath();
  for (let i = 0; i + 2 < indices.length; i += 3) {
    const a = indices[i] * 2;
    const b = indices[i + 1] * 2;
    const c = indices[i + 2] * 2;
    ctx.moveTo(uvs[a] * UV_RENDER_SIZE, uvs[a + 1] * UV_RENDER_SIZE);
    ctx.lineTo(uvs[b] * UV_RENDER_SIZE, uvs[b + 1] * UV_RENDER_SIZE);
    ctx.lineTo(uvs[c] * UV_RENDER_SIZE, uvs[c + 1] * UV_RENDER_SIZE);
    ctx.closePath();
  }
  ctx.fill();
  ctx.stroke();

  return canvas.toDataURL('image/png');
}

/* ----------------------------------------------------------- aspect ratios */

/** The ratios the image model accepts, as width/height. */
const RATIOS: [string, number][] = [
  ['1:1', 1], ['2:3', 2 / 3], ['3:2', 3 / 2], ['3:4', 3 / 4], ['4:3', 4 / 3],
  ['4:5', 4 / 5], ['5:4', 5 / 4], ['9:16', 9 / 16], ['16:9', 16 / 9], ['21:9', 21 / 9],
];

/**
 * The nearest supported ratio to the texture being edited.
 *
 * Derived, never assumed. A garment diffuse is square in almost every case, and
 * a texture generated at the wrong ratio arrives stretched -- which looks like
 * a UV bug and is not one.
 */
export function aspectRatioFor(width: number, height: number): string {
  if (!(width > 0) || !(height > 0)) return '1:1';

  const target = width / height;
  let best = RATIOS[0];
  for (const candidate of RATIOS) {
    if (Math.abs(candidate[1] - target) < Math.abs(best[1] - target)) best = candidate;
  }
  return best[0];
}

/** Reads a data URL's pixel size, so the ratio comes from the real image. */
export function measureDataUrl(dataUrl: string): Promise<{ width: number; height: number }> {
  return new Promise((resolve) => {
    const img = new Image();
    img.onload = () => resolve({ width: img.naturalWidth, height: img.naturalHeight });
    img.onerror = () => resolve({ width: 0, height: 0 });
    img.src = dataUrl;
  });
}
