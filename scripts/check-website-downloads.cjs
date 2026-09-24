const fs = require('node:fs');
const assert = require('node:assert/strict');
const html = fs.readFileSync('website/download.html', 'utf8');
const links = [...html.matchAll(/<a\b[^>]*href="([^"]+)"[^>]*>/g)]
  .filter(([tag]) => /id="beta-download"|class="directory-download"/.test(tag));
assert(links.length > 0, 'No installer links found');
for (const [, url] of links) {
  const match = /^https:\/\/imfishman\.com\/downloads\/v([^/]+)\/EVE-FitLab-([^/]+)-Windows-x64-Setup\.exe$/.exec(url);
  assert(match && match[1] === match[2], `Invalid Cloudflare installer URL: ${url}`);
}
assert(!/href="https:\/\/github\.com\/[^" ]+\/releases\/download\//.test(html), 'Website must not send downloads directly to GitHub');
console.log(`Validated ${links.length} Cloudflare installer links.`);
