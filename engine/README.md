# Dogma engine source

The application and contract sources required by FitLab.Engine are included here, extracted from the author’s EdenOS rewrite project. FitLab only starts the minimal host under `desktop/engine`, not the old web host, market services or account services. No legacy runtime configuration or user state is included.

The default desktop build uses these sources. CCP static data is a separate build input supplied via `-SdeRoot`.
