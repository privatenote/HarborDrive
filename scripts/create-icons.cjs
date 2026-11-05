// Run with Node.js + sharp. Generated PNG/ICO files are committed; building the app needs no Node.js.
const fs = require('node:fs/promises');
const path = require('node:path');
const sharp = require('sharp');
const assets = path.resolve(__dirname, '../src/HarborDrive.App/Assets');
const sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256];
async function makeIco(name, svg) {
  const frames = await Promise.all(sizes.map(size => sharp(Buffer.from(svg)).resize(size,size).png().toBuffer()));
  const header = Buffer.alloc(6 + 16 * frames.length);
  header.writeUInt16LE(1, 2); header.writeUInt16LE(frames.length, 4);
  let offset = header.length;
  for (let i = 0; i < frames.length; i++) {
    const base = 6 + i * 16;
    header[base] = sizes[i] === 256 ? 0 : sizes[i]; header[base + 1] = header[base];
    header.writeUInt16LE(1, base + 4); header.writeUInt16LE(32, base + 6);
    header.writeUInt32LE(frames[i].length, base + 8); header.writeUInt32LE(offset, base + 12);
    offset += frames[i].length;
  }
  await fs.writeFile(path.join(assets, name + '.ico'), Buffer.concat([header, ...frames]));
}
(async () => {
  const svg = await fs.readFile(path.join(assets, 'HarborDrive.svg'), 'utf8');
  await makeIco('HarborDrive', svg);
  await sharp(Buffer.from(svg)).resize(256,256).png().toFile(path.join(assets, 'HarborDrive.png'));
  const tray = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32"><rect x="1" y="1" width="30" height="30" rx="8" fill="COLOR"/><path d="M9 19c-4 0-6-2-6-5 0-3 2-5 5-5 1-4 4-6 8-6 4 0 7 3 8 6 4 0 6 3 6 6 0 2-2 4-5 4H9Z" fill="white"/><rect x="7" y="23" width="18" height="4" rx="2" fill="white"/>BADGE</svg>`;
  for (const [state, color, badge] of [
    ['Connected','#087E83',''],
    ['Disconnected','#6D818B',''],
    ['Transferring','#087E83','<circle cx="25" cy="25" r="7" fill="#FFCB58"/><path d="M25 21v8m-3-3 3 3 3-3" stroke="#153D50" stroke-width="2" fill="none" stroke-linecap="round" stroke-linejoin="round"/>'],
    ['Error','#087E83','<circle cx="25" cy="25" r="7" fill="#D94752"/><path d="M25 21v4m0 3v.2" stroke="white" stroke-width="2.5" stroke-linecap="round"/>']
  ]) {
    const variant = tray.replace('COLOR',color).replace('BADGE',badge);
    await fs.writeFile(path.join(assets, 'Tray'+state+'.svg'), variant + '\n');
    await makeIco('Tray'+state, variant);
  }
  console.log('Generated application icon and four tray states at '+sizes.join(', ')+' px.');
})().catch(error => { console.error(error); process.exitCode=1; });
