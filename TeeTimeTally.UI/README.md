# TeeTimeTally.UI

Frontend application for TeeTimeTally.

Tech: Vue 3, TypeScript, Pinia, Vite.

Client is under `TeeTimeTally.UI/Client`.

Developer notes:
- Run client builds from WSL when possible to avoid dev-time plugin issues on Windows hosts.
- The devtools plugin is dynamically imported in `vite.config.ts` so production builds won't attempt to evaluate browser-only APIs.

Client quick commands (inside `Client/`):

```bash
# in WSL
npm ci
npm run dev   # dev server
npm run build # production build
```
