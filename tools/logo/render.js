// Rasterises the logo SVGs written by gen.py into PNGs, then python3 tools/logo/ico.py builds the .ico.
// Run from the repo root: node tools/logo/render.js
const { chromium } = require('playwright');
const fs = require('fs');
const path = require('path');

const root = path.resolve(__dirname, '../..');
const brand = path.join(root, 'docs/brand');
const appLogo = path.join(root, 'src/Klemme15.App/Assets/Logo');

async function render(page, svgFile, outFile, w, h) {
  const svg = fs.readFileSync(svgFile, 'utf8').replace(/width="\d+" height="\d+"/, `width="${w}" height="${h}"`);
  await page.setViewportSize({ width: w, height: h });
  await page.setContent(`<html><body style="margin:0;background:transparent">${svg}</body></html>`);
  await page.locator('svg').screenshot({ path: outFile, omitBackground: true });
}

(async () => {
  const browser = await chromium.launch();
  const page = await browser.newPage();
  const icon = path.join(brand, 'klemme15-icon.svg');
  for (const s of [16, 24, 32, 48, 64, 128, 256]) {
    await render(page, icon, path.join(appLogo, `klemme15-${s}.png`), s, s);
  }
  for (const name of ['klemme15-lockup-dark', 'klemme15-lockup-light']) {
    const src = path.join(brand, `${name}.svg`);
    const m = fs.readFileSync(src, 'utf8').match(/viewBox="0 0 (\d+) (\d+)"/);
    await render(page, src, path.join(brand, `${name}.png`), +m[1] * 4, +m[2] * 4);
  }
  await render(page, path.join(brand, 'klemme15-social.svg'), path.join(brand, 'klemme15-social.png'), 1280, 640);
  await render(page, icon, path.join(brand, 'klemme15-icon.png'), 1024, 1024);
  await browser.close();
  console.log('PNGs written.');
})();
