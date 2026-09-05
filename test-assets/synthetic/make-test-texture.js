#!/usr/bin/env node
/*
 * Generates a synthetic diffuse texture for pipeline testing.
 *
 * Deliberately garish: the point is that if this lands on a garment in game,
 * there is no ambiguity about whether the export worked. A subtle test pattern
 * can be confused with the original texture at a glance.
 *
 * No Rockstar art is used or redistributed -- see docs/legal-and-oss.md.
 *
 *   node make-test-texture.js [size] [output.png]
 */

const fs = require('fs');
const zlib = require('zlib');

const size = parseInt(process.argv[2] || '512', 10);
const out = process.argv[3] || `test-texture-${size}.png`;

if ((size & (size - 1)) !== 0) {
  console.error(`Size must be a power of two; got ${size}.`);
  process.exit(1);
}

// --- paint ---------------------------------------------------------------
const px = Buffer.alloc(size * size * 4);
const cell = size / 8;

function set(x, y, r, g, b, a = 255) {
  const i = (y * size + x) * 4;
  px[i] = r; px[i + 1] = g; px[i + 2] = b; px[i + 3] = a;
}

for (let y = 0; y < size; y++) {
  for (let x = 0; x < size; x++) {
    const cx = Math.floor(x / cell);
    const cy = Math.floor(y / cell);
    const checker = (cx + cy) % 2 === 0;
    // Magenta/cyan checker with a vertical value ramp, so mip generation and
    // gamma handling are both visible if something goes wrong.
    const ramp = 0.45 + 0.55 * (y / size);
    if (checker) set(x, y, Math.round(255 * ramp), 0, Math.round(200 * ramp));
    else set(x, y, 0, Math.round(220 * ramp), Math.round(255 * ramp));
  }
}

// Bright border so UV edges and wrapping are obvious.
for (let i = 0; i < size; i++) {
  for (let t = 0; t < Math.max(2, size / 128); t++) {
    set(i, t, 255, 255, 0);
    set(i, size - 1 - t, 255, 255, 0);
    set(t, i, 255, 255, 0);
    set(size - 1 - t, i, 255, 255, 0);
  }
}

// Solid diagonal, so mirrored or rotated UVs are immediately obvious.
for (let i = 0; i < size; i++) {
  for (let t = -2; t <= 2; t++) {
    const y = Math.min(size - 1, Math.max(0, i + t));
    set(i, y, 255, 255, 255);
  }
}

// --- encode --------------------------------------------------------------
function chunk(type, data) {
  const len = Buffer.alloc(4);
  len.writeUInt32BE(data.length, 0);
  const body = Buffer.concat([Buffer.from(type, 'ascii'), data]);
  const crc = Buffer.alloc(4);
  crc.writeUInt32BE(crc32(body) >>> 0, 0);
  return Buffer.concat([len, body, crc]);
}

let crcTable = null;
function crc32(buf) {
  if (!crcTable) {
    crcTable = new Int32Array(256);
    for (let n = 0; n < 256; n++) {
      let c = n;
      for (let k = 0; k < 8; k++) c = c & 1 ? 0xedb88320 ^ (c >>> 1) : c >>> 1;
      crcTable[n] = c;
    }
  }
  let c = -1;
  for (let i = 0; i < buf.length; i++) c = crcTable[(c ^ buf[i]) & 0xff] ^ (c >>> 8);
  return c ^ -1;
}

const ihdr = Buffer.alloc(13);
ihdr.writeUInt32BE(size, 0);
ihdr.writeUInt32BE(size, 4);
ihdr[8] = 8;   // bit depth
ihdr[9] = 6;   // colour type: RGBA
ihdr[10] = 0;  // deflate
ihdr[11] = 0;  // adaptive filtering
ihdr[12] = 0;  // no interlace

// One filter byte (0 = None) per scanline.
const raw = Buffer.alloc(size * (size * 4 + 1));
for (let y = 0; y < size; y++) {
  raw[y * (size * 4 + 1)] = 0;
  px.copy(raw, y * (size * 4 + 1) + 1, y * size * 4, (y + 1) * size * 4);
}

const png = Buffer.concat([
  Buffer.from([0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]),
  chunk('IHDR', ihdr),
  chunk('IDAT', zlib.deflateSync(raw, { level: 9 })),
  chunk('IEND', Buffer.alloc(0)),
]);

fs.writeFileSync(out, png);
console.log(`${out}  ${size}x${size}  ${png.length.toLocaleString()} bytes`);
