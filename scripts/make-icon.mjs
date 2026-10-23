// Builds installer/AFModLoader.Installer/app.ico from the open-ledger "AF" mark of the author's calculator project.
// Developer tool: not part of check.lua or the build, and the .ico is committed.
//
// Usage: node scripts/make-icon.mjs <calculator folder> [output file]
//
// The output file defaults to the installer's app.ico. Give another path to compare hashes without overwriting it.
//
// The mark comes from <calculator>/scripts/lib/brand-mark.mjs and <calculator>/lib/brand-mark.json (commit
// 5bb4695bbef65f41cd8ee06f35cf8e87d6cad26f), in the light colours on a transparent background. sharp is found the same
// way the calculator's scripts/generate-icons.mjs finds it: through next, which is installed there.
//
// Sizes and cuts: 16 -> c16, 24 and 32 -> c32, 48 and 256 -> master. 16 to 48 are 32-bit BMP frames, 256 is a PNG.
import { writeFileSync } from "node:fs";
import { createRequire } from "node:module";
import { dirname, join, resolve } from "node:path";
import { fileURLToPath, pathToFileURL } from "node:url";

const calculator = process.argv[2];
if (!calculator) {
  console.error("Usage: node scripts/make-icon.mjs <calculator folder> [output file]");
  process.exit(1);
}
const root = resolve(calculator);

const { light, markSvg } = await import(pathToFileURL(join(root, "scripts", "lib", "brand-mark.mjs")).href);
const nextRequire = createRequire(createRequire(join(root, "package.json")).resolve("next/package.json"));
const sharp = nextRequire("sharp");
console.log("sharp " + sharp.versions.sharp + " (libvips " + sharp.versions.vips + ")");

const frames = [
  { px: 16, cut: "c16", png: false },
  { px: 24, cut: "c32", png: false },
  { px: 32, cut: "c32", png: false },
  { px: 48, cut: "master", png: false },
  { px: 256, cut: "master", png: true },
];

const svgOf = ({ cut, px }) => Buffer.from(markSvg(cut, light, px));

/** A 32-bit BMP frame: BITMAPINFOHEADER (height doubled), BGRA pixels bottom-up, then the all-zero AND mask. */
async function bmpFrame({ px, cut }) {
  const { data } = await sharp(svgOf({ px, cut })).ensureAlpha().raw().toBuffer({ resolveWithObject: true });
  const header = Buffer.alloc(40);
  header.writeUInt32LE(40, 0);
  header.writeInt32LE(px, 4);
  header.writeInt32LE(px * 2, 8);
  header.writeUInt16LE(1, 12);
  header.writeUInt16LE(32, 14);
  const pixels = Buffer.alloc(px * px * 4);
  for (let y = 0; y < px; y++) {
    for (let x = 0; x < px; x++) {
      const from = (y * px + x) * 4;
      const to = ((px - 1 - y) * px + x) * 4;
      pixels[to] = data[from + 2];
      pixels[to + 1] = data[from + 1];
      pixels[to + 2] = data[from];
      pixels[to + 3] = data[from + 3];
    }
  }
  const maskRow = Math.ceil(px / 32) * 4;
  return Buffer.concat([header, pixels, Buffer.alloc(maskRow * px)]);
}

const images = [];
for (const frame of frames) {
  images.push(frame.png ? await sharp(svgOf(frame)).png().toBuffer() : await bmpFrame(frame));
}

const head = Buffer.alloc(6 + 16 * frames.length);
head.writeUInt16LE(1, 2);
head.writeUInt16LE(frames.length, 4);
let offset = head.length;
frames.forEach(({ px }, i) => {
  const entry = 6 + 16 * i;
  head[entry] = px === 256 ? 0 : px;
  head[entry + 1] = px === 256 ? 0 : px;
  head.writeUInt16LE(1, entry + 4);
  head.writeUInt16LE(32, entry + 6);
  head.writeUInt32LE(images[i].length, entry + 8);
  head.writeUInt32LE(offset, entry + 12);
  offset += images[i].length;
});

const target = process.argv[3]
  ? resolve(process.argv[3])
  : join(dirname(fileURLToPath(import.meta.url)), "..", "installer", "AFModLoader.Installer", "app.ico");
writeFileSync(target, Buffer.concat([head, ...images]));
console.log("Wrote " + resolve(target));
