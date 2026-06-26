# Pocket Station Web

Vue 3 + Vite + TypeScript frontend for the Pocket Station LAN web console.

## Commands

```powershell
npm install
npm run dev
npm run build
```

`npm run build` writes the static production output to `../wwwroot`, which is the directory copied into the Dalamud plugin build output.

## Structure

- `src/services/pocketApi.ts`: LAN HTTP/WebSocket URL and JSON helpers.
- `src/composables/usePocketStation.ts`: application state, WebSocket events, API commands, chat filtering.
- `src/components`: reusable UI components.
- `src/views`: tab-level screens.
