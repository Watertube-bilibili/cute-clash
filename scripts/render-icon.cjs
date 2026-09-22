// Build-time only. The distributed Windows 7 application needs neither Node nor resvg.
// Rebuild all bitmap assets from the original SVG: powershell -File scripts/render-icon.ps1
'use strict';
const fs = require('node:fs');
const path = require('node:path');
const root = path.resolve(process.argv[2] || path.join(__dirname, '..'));
const rendererRoot = path.resolve(process.argv[3] || path.join(root, '.tools', 'icon-renderer'));
const { Resvg } = require(path.join(rendererRoot, 'node_modules', '@resvg', 'resvg-js'));
const svg = fs.readFileSync(path.join(root, 'assets', 'cute-clash.svg'), 'utf8');
const sizes = [16, 24, 32, 48, 64, 128, 256];

function render(size) {
  return new Resvg(svg, { fitTo: { mode: 'width', value: size }, font: { loadSystemFonts: false } }).render();
}

// BMP/DIB frames below 256px retain compatibility with older Windows icon consumers.
// The 256px PNG frame gives Explorer a compact, high-resolution preview.
function dib(image, size) {
  const pixels = image.pixels;
  const xorSize = size * size * 4;
  const maskStride = Math.ceil(size / 32) * 4;
  const buffer = Buffer.alloc(40 + xorSize + maskStride * size);
  buffer.writeUInt32LE(40, 0);
  buffer.writeInt32LE(size, 4);
  buffer.writeInt32LE(size * 2, 8);
  buffer.writeUInt16LE(1, 12);
  buffer.writeUInt16LE(32, 14);
  buffer.writeUInt32LE(xorSize, 20);
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const source = (y * size + x) * 4;
      const target = 40 + ((size - y - 1) * size + x) * 4;
      buffer[target] = pixels[source + 2];
      buffer[target + 1] = pixels[source + 1];
      buffer[target + 2] = pixels[source];
      buffer[target + 3] = pixels[source + 3];
      if (pixels[source + 3] === 0) {
        buffer[40 + xorSize + (size - y - 1) * maskStride + (x >> 3)] |= 0x80 >> (x & 7);
      }
    }
  }
  return buffer;
}

const frames = sizes.map(size => {
  const image = render(size);
  return { size, data: size === 256 ? image.asPng() : dib(image, size) };
});
const header = Buffer.alloc(6 + frames.length * 16);
header.writeUInt16LE(1, 2);
header.writeUInt16LE(frames.length, 4);
let offset = header.length;
frames.forEach(({ size, data }, index) => {
  const entry = 6 + index * 16;
  header[entry] = size === 256 ? 0 : size;
  header[entry + 1] = size === 256 ? 0 : size;
  header.writeUInt16LE(1, entry + 4);
  header.writeUInt16LE(32, entry + 6);
  header.writeUInt32LE(data.length, entry + 8);
  header.writeUInt32LE(offset, entry + 12);
  offset += data.length;
});
fs.writeFileSync(path.join(root, 'assets', 'cute-clash.ico'), Buffer.concat([header, ...frames.map(frame => frame.data)]));
fs.writeFileSync(path.join(root, 'assets', 'cute-clash.png'), render(512).asPng());
console.log('Rendered assets/cute-clash.png (512px) and cute-clash.ico (16, 24, 32, 48, 64, 128, 256px) from cute-clash.svg.');
