// Deterministic derivatives of the approved FILPRO bitmap. Never invokes image generation.
// Requires an existing Sharp installation; downloads nothing.
// Set FILPRO_SHARP_MODULE to its module path when Sharp is not locally resolvable.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { createRequire } from 'node:module';
import { createHash } from 'node:crypto';

const require = createRequire(import.meta.url);
const sharp = require(process.env.FILPRO_SHARP_MODULE || 'sharp');
const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '../..');
const assets = path.join(root, 'assets/branding');
const check = process.argv.includes('--check');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
const sha = (bytes) => createHash('sha256').update(bytes).digest('hex');
const master = fs.readFileSync(path.join(assets, 'final-brand-master.png'));
const generation = JSON.parse(fs.readFileSync(path.join(assets, 'generation.json'), 'utf8'));
if (sha(master) !== generation.output.sha256 || master.length !== generation.output.bytes) {
  throw new Error('The approved generated master changed');
}
if (sha(fs.readFileSync(path.join(assets, generation.prompt))) !== generation.promptSha256) {
  throw new Error('The recorded image-generation prompt changed');
}
for (const previous of generation.history ?? []) {
  if (sha(fs.readFileSync(path.join(assets, previous.prompt))) !== previous.promptSha256
      || !generation.referencedImages.some((image) => image.sha256 === previous.output.sha256)) {
    throw new Error('The recorded background-edit chain changed');
  }
}
const outputs = new Map();
const payloads = [];

for (const size of [...sizes, 1024]) {
  const png = await sharp(master).resize(size, size, { kernel: 'lanczos3' }).png().toBuffer();
  const rgba = await sharp(png).ensureAlpha().raw().toBuffer();
  for (let index = 3; index < rgba.length; index += 4) {
    if (rgba[index] !== 255) throw new Error(`The ${size}px family icon is not entirely opaque`);
  }
  outputs.set(`assets/branding/png/filpro-${size}.png`, png);
  if (size === 1024) continue;
  if (size === 256) { payloads.push(png); continue; }
  // Windows DIB: bottom-up BGRA pixels, twice the height, followed by an opaque AND mask.
  const stride = Math.ceil(size / 32) * 4;
  const dib = Buffer.alloc(40 + size * size * 4 + stride * size);
  dib.writeUInt32LE(40, 0); dib.writeInt32LE(size, 4); dib.writeInt32LE(size * 2, 8);
  dib.writeUInt16LE(1, 12); dib.writeUInt16LE(32, 14);
  dib.writeUInt32LE(dib.length - 40, 20);
  for (let y = 0; y < size; y += 1) for (let x = 0; x < size; x += 1) {
    const src = (y * size + x) * 4;
    const dst = 40 + ((size - y - 1) * size + x) * 4;
    dib[dst] = rgba[src + 2]; dib[dst + 1] = rgba[src + 1];
    dib[dst + 2] = rgba[src]; dib[dst + 3] = 255;
  }
  payloads.push(dib);
}
const directory = Buffer.alloc(6 + 16 * sizes.length);
directory.writeUInt16LE(1, 2); directory.writeUInt16LE(sizes.length, 4);
let offset = directory.length;
for (const [index, size] of sizes.entries()) {
  const entry = 6 + index * 16;
  directory[entry] = size % 256; directory[entry + 1] = size % 256;
  directory.writeUInt16LE(1, entry + 4); directory.writeUInt16LE(32, entry + 6);
  directory.writeUInt32LE(payloads[index].length, entry + 8);
  directory.writeUInt32LE(offset, entry + 12); offset += payloads[index].length;
}
outputs.set('src/Filpro.App/Assets/filpro.ico', Buffer.concat([directory, ...payloads]));

for (const variant of ['', '-on-dark']) {
  const wordmark = fs.readFileSync(path.join(assets, `filpro-wordmark${variant}.svg`), 'utf8');
  const logo = `<svg xmlns="http://www.w3.org/2000/svg" width="984" height="256" viewBox="0 0 492 128"><image href="data:image/png;base64,${master.toString('base64')}" width="128" height="128"/><g transform="translate(149 30)">${wordmark}</g></svg>`;
  outputs.set(`assets/branding/png/filpro-logo${variant}.png`, await sharp(Buffer.from(logo)).png().toBuffer());
}
const sources = ['final-brand-master.png', 'generation.json', 'imagegen-prompt.txt', 'imagegen-pastel-prompt.txt', 'filpro-geometry-study.svg',
  'filpro-wordmark.svg', 'filpro-wordmark-on-dark.svg', 'filpro-monochrome.svg', 'filpro-icon-body.txt'];
const entries = sources.map((name) => {
  const data = fs.readFileSync(path.join(assets, name));
  return { path: `assets/branding/${name}`, bytes: data.length, sha256: sha(data) };
});
for (const [name, data] of outputs) entries.push({ path: name, bytes: data.length, sha256: sha(data) });
const provenance = {
  schemaVersion: 1, product: 'FILPRO', created: '2026-10-28',
  authorship: 'Original vector geometry study and wordmark; approved color bitmap refined with builtin image_gen, not represented as an exact vector source.',
  family: 'White profile cards, subtle relief and turquoise accent on an opaque pastel-blue square.',
  referencePalette: { pastelStart: '#B9DDF2', pastelEnd: '#93B6E6', royalSilhouette: '#066DDB', accent: '#19C3B2', symbol: '#FFFFFF' },
  paletteMeaning: 'Family design reference; generated pixels are not claimed to equal these flat colors.',
  generationRecord: 'generation.json', masterSha256: sha(master),
  iconSizes: sizes, allIconPixelsOpaque: true,
  renderer: { sharp: sharp.versions.sharp, vips: sharp.versions.vips, rsvg: sharp.versions.rsvg },
  files: entries,
};
outputs.set('assets/branding/provenance.json', Buffer.from(JSON.stringify(provenance, null, 2) + '\n'));
for (const [name, data] of outputs) {
  const target = path.join(root, name);
  if (check) {
    if (!fs.readFileSync(target).equals(data)) throw new Error(`Branding asset drift: ${name}`);
  } else {
    fs.mkdirSync(path.dirname(target), { recursive: true });
    fs.writeFileSync(target, data);
  }
}
const body = fs.readFileSync(path.join(assets, 'filpro-icon-body.txt'), 'utf8').trim();
if (!fs.readFileSync(path.join(root, 'web/js/icons.js'), 'utf8').includes(`filpro: '${body}'`)) {
  throw new Error('The inline FILPRO badge differs from its original vector source');
}
console.log(JSON.stringify({ mode: check ? 'checked' : 'rendered', sizes,
  icoSha256: sha(outputs.get('src/Filpro.App/Assets/filpro.ico')), renderer: provenance.renderer }));
