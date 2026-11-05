// Мост страница ↔ приложение. Протокол — docs/superpowers/specs/2026-11-05-web-settings-design.md

export type PageId = "general" | "layout" | "correct" | "figma" | "help" | "about"
export type ListId = "layout" | "correct"

export interface AppEntry { id: string; name: string; icon?: string }
export interface HotkeyOption { id: string; title: string }
export type ModelState = "notDownloaded" | "downloading" | "ready" | "failed"

export interface State {
  platform: "mac" | "windows"
  version: string
  page: PageId | null
  general: { launchAtLogin: boolean; debugMode: boolean }
  attention: { modelMissing: boolean; macAutocorrect: boolean }
  layout: { autoSwitch: boolean; hotkey: string; hotkeyTitle: string; apps: AppEntry[]; words: string[] }
  correct: {
    available: boolean; hotkey: string; hotkeyTitle: string; capitalize: boolean
    typography: boolean; typos: boolean; yo: boolean; apps: AppEntry[]; words: string[]
  }
  hotkeyOptions: HotkeyOption[]
  recording: "convert" | "correct" | null
  hotkeyError: { action: "convert" | "correct"; text: string } | null
  integrations: { figma: boolean }
  about: {
    autoUpdate: boolean; lastCheck: string | null
    model: { state: ModelState; progress: number; message: string; size: string; disk: string }
  }
}

type Outgoing =
  | { type: "ready" }
  | { type: "set"; key: string; value: unknown }
  | { type: "action"; name: string; arg?: unknown }

interface Host {
  webkit?: { messageHandlers?: { vallo?: { postMessage(m: unknown): void } } }
  chrome?: { webview?: { postMessage(m: unknown): void } }
  vallo?: { receive(state: State): void }
}
const host = window as unknown as Host

/** Есть ли приложение рядом (иначе страница открыта в обычном браузере — показываем пример) */
export const hasHost = Boolean(host.webkit?.messageHandlers?.vallo || host.chrome?.webview)

export function send(message: Outgoing) {
  if (host.webkit?.messageHandlers?.vallo) host.webkit.messageHandlers.vallo.postMessage(message)
  else if (host.chrome?.webview) host.chrome.webview.postMessage(message)
  else console.log("→ приложение", message)
}

export const set = (key: string, value: unknown) => send({ type: "set", key, value })
export const action = (name: string, arg?: unknown) => send({ type: "action", name, arg })

/** Приложение зовёт window.vallo.receive(state) при каждом изменении */
export function onState(listener: (s: State) => void) {
  host.vallo = { receive: listener }
}
