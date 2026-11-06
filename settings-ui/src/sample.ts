import type { State } from "./bridge"

/** Пример состояния — для `npm run dev` без приложения */
export const sample: State = {
  platform: "mac",
  version: "0.6.1",
  page: null,
  general: { launchAtLogin: true, debugMode: false },
  attention: { modelMissing: true, macAutocorrect: true },
  layout: {
    autoSwitch: true,
    hotkey: "double:leftShift", hotkeyTitle: "Двойной левый Shift",
    apps: [{ id: "com.apple.Terminal", name: "Терминал" }, { id: "com.microsoft.VSCode", name: "Visual Studio Code" }],
    words: [],
  },
  correct: {
    available: true, hotkey: "double:rightShift", hotkeyTitle: "Двойной правый Shift", capitalize: false, typography: true, typos: true, yo: true,
    apps: [{ id: "com.apple.Terminal", name: "Терминал" }],
    words: ["вайб", "хабр"],
    emoji: { preset: "millennial", custom: [] },
  },
  hotkeyOptions: [
    { id: "double:leftShift", title: "Двойной левый Shift" },
    { id: "double:rightShift", title: "Двойной правый Shift" },
    { id: "double:leftOption", title: "Двойной левый Option" },
    { id: "double:rightOption", title: "Двойной правый Option" },
    { id: "double:leftControl", title: "Двойной Control" },
  ],
  canRecordHotkey: true,
  recording: null,
  hotkeyError: null,
  integrations: { figma: true, figmaPublished: false },
  about: {
    updates: true, autoUpdate: true, lastCheck: new Date().toISOString(),
    model: { state: "notDownloaded", progress: 0, message: "", size: "228 МБ", disk: "470 МБ" },
  },
}
