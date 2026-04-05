# Client

The frontend client for TeeTimeTally (Vue 3 + TypeScript + Pinia + Vite).

Quick start (WSL recommended):

```bash
cd Client
npm ci
npm run dev     # start dev server
npm run build   # production build
npm run type-check
```

Notes:
- If you get an error related to `@vue/devtools` or `localStorage` during build, run the build inside WSL or ensure Node on Windows has access to required global APIs. The repo guards the devtools plugin in `vite.config.ts`.
- Frontend code uses `mapApiErrorToAppError` (in `src/services/apiError.ts`) to normalize API errors to `AppError` objects.
