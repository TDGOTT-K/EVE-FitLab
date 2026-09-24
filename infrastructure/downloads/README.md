# Cloudflare installer downloads

Production Worker: `fitlab-downloads`; route: `imfishman.com/downloads/*`.
Source: `worker.js` (service-worker syntax). Deploy through the Cloudflare Workers API or dashboard, preserving this route.

Public URL format:
`https://imfishman.com/downloads/vVERSION/EVE-FitLab-VERSION-Windows-x64-Setup.exe`

The Worker streams the public TDGOTT-K/EVE-FitLab GitHub release asset through Cloudflare; it never redirects visitors to GitHub. Origin fetch caching is enabled for successful assets for 24 hours and release redirects for 5 minutes. Complete GETs also attempt to fill a canonical-URL edge cache. Range and HEAD requests are supported. This is an edge cache, not an independent R2 archive: a cold cache still requires GitHub to be available. No R2 subscription was enabled.

Only versioned Windows installers from the fixed repository are accepted. The Worker forwards only Range/If-Range, never browser credentials. Errors are not cached. Cloudflare's cache object limit is 512 MB; current installers are below it.

For future releases:
1. Upload the immutable installer to the matching GitHub release.
2. Use the public Cloudflare URL above for every website download button, including history. GitHub release-note links may stay.
3. Verify a full GET against the release SHA-256, HEAD content length, and a 206 range response before publishing the website.
4. Run `node scripts/check-website-downloads.cjs` before Pages deployment.

Do not edit signed application update manifests just to change website buttons. Their URL is part of the signed payload and needs the normal release-signing workflow.

Rollback: deploy the previous Pages snapshot, then remove route `88764116af2c4e78b333a88c1939b741` if the Worker is no longer needed.

## Verification (2026-09-24)

All 10 website installers were fully downloaded through imfishman.com without redirects; sizes and SHA-256 matched GitHub release asset digests. Every installer returned a valid 206 range response. v0.1.5 was rechecked after the final Worker deployment: SHA-256 matched and `X-FitLab-Origin-Cache: HIT` confirmed Cloudflare fetch-cache use. Canonical cache probes returned MISS in this run; do not claim every download hits cache. Six Worker behavior tests and the website link check passed.

Pages deployment: https://21576c33.eve-fitlab.pages.dev
