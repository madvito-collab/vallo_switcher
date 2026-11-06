// Смайлики из скобок: выбор набора и свой набор «скобки → смайлик»
import { useEffect, useRef, useState } from "react"
import { ArrowRight, Plus, Trash2 } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Select, SelectContent, SelectItem, SelectSeparator, SelectTrigger, SelectValue } from "@/components/ui/select"
import { action, set, type EmojiPair, type EmojiPreset, type State } from "./bridge"
import { Card, Row } from "./kit"

/** Готовые наборы — те же, что в приложении */
export const presets: Record<"millennial" | "zoomer", EmojiPair[]> = {
  millennial: [
    { trigger: ")", emoji: "🙂" }, { trigger: "))", emoji: "😄" }, { trigger: ")))", emoji: "😂" },
    { trigger: "(", emoji: "🙁" }, { trigger: "((", emoji: "😢" }, { trigger: "(((", emoji: "😭" },
  ],
  zoomer: [
    { trigger: ")", emoji: "😊" }, { trigger: "))", emoji: "😭" }, { trigger: ")))", emoji: "💀" },
    { trigger: "(", emoji: "🥲" }, { trigger: "((", emoji: "😔" }, { trigger: "(((", emoji: "🥺" },
  ],
}

const preview = (p: EmojiPair[]) => p.filter(x => x.trigger.startsWith(")")).map(x => x.emoji).join("")

/** Подпись примером, как у соседних строк: «ок)) → ок😄» смайликом из выбранного набора */
function example(preset: EmojiPreset, custom: EmojiPair[]) {
  if (preset === "off") return "Скобки остаются скобками"
  const pairs = preset === "custom" ? custom.filter(p => p.trigger && p.emoji) : presets[preset]
  const p = pairs.find(x => x.trigger === "))") ?? pairs[0]
  return p ? `ок${p.trigger} → ок${p.emoji}, а «(вот так)» не тронет` : "Добавьте свои смайлики ниже"
}

/** Строка в «Пока вы печатаете» */
export function EmojiRow({ s }: { s: State }) {
  const e = s.correct.emoji
  const choose = (v: string) => {
    // свой набор впервые — начинаем с миллениала, чтобы было что править
    if (v === "custom" && e.custom.length === 0) set("emoji.custom", presets.millennial)
    set("emoji.preset", v as EmojiPreset)
  }
  return (
    <Row title="Смайлики из скобок" desc={example(e.preset, e.custom)}>
      <Select value={e.preset} onValueChange={choose}>
        <SelectTrigger aria-label="Смайлики из скобок"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="millennial">Миллениал {preview(presets.millennial)}</SelectItem>
          <SelectItem value="zoomer">Зумер {preview(presets.zoomer)}</SelectItem>
          <SelectItem value="custom">Свой набор</SelectItem>
          <SelectSeparator />
          <SelectItem value="off">Выключено</SelectItem>
        </SelectContent>
      </Select>
    </Row>
  )
}

/** Свой набор: слева скобки, справа смайлик */
export function EmojiCustom({ pairs }: { pairs: EmojiPair[] }) {
  const save = (next: EmojiPair[]) => set("emoji.custom", next)
  const update = (i: number, patch: Partial<EmojiPair>) => save(pairs.map((p, j) => (j === i ? { ...p, ...patch } : p)))
  return (
    <Card>
      {pairs.map((p, i) => (
        <div key={i} className="flex items-center gap-3 px-4 py-2.5">
          <Input className="w-28 font-mono" placeholder="))" value={p.trigger} aria-label="Скобки"
                 // только скобки: так правило не заденет обычный текст
                 onChange={ev => update(i, { trigger: ev.target.value.replace(/[^()]/g, "").slice(0, 6) })} />
          <ArrowRight className="size-4 shrink-0 text-muted-foreground" />
          <EmojiPicker value={p.emoji} onChange={emoji => update(i, { emoji })} />
          <div className="flex-1" />
          <Button variant="destructive" size="icon" onClick={() => save(pairs.filter((_, j) => j !== i))} aria-label="Убрать"><Trash2 /></Button>
        </div>
      ))}
      <div className="px-4 py-3">
        <Button onClick={() => save([...pairs, { trigger: "", emoji: "🙂" }])}><Plus />Добавить</Button>
      </div>
    </Card>
  )
}

const popular = [
  "🙂", "😊", "😄", "😁", "😂", "🤣", "😅", "😆", "😉", "😍", "🥰", "😘",
  "😎", "🤔", "😏", "🙃", "🫠", "😌", "🥲", "🙁", "😔", "😢", "😭", "🥺",
  "😡", "🤯", "😱", "💀", "👍", "🙏", "🔥", "❤️", "✨", "🤡", "👀", "🫶",
]

/** Квадрат со смайликом: по клику — частые смайлики и все остальные из системы */
function EmojiPicker({ value, onChange }: { value: string; onChange: (e: string) => void }) {
  const [open, setOpen] = useState(false)
  const box = useRef<HTMLDivElement>(null)
  useEffect(() => {
    if (!open) return
    const close = (e: MouseEvent) => { if (!box.current?.contains(e.target as Node)) setOpen(false) }
    document.addEventListener("mousedown", close)
    return () => document.removeEventListener("mousedown", close)
  }, [open])
  return (
    <div ref={box} className="relative">
      <button type="button" onClick={() => setOpen(!open)} aria-label="Выбрать смайлик"
              className="grid size-9 place-items-center rounded-lg border border-border bg-field text-xl hover:bg-hover">
        {value || "＋"}
      </button>
      {open && (
        <div className="absolute left-0 top-11 z-50 w-[292px] rounded-xl border border-border bg-card p-2 shadow-lg">
          <div className="grid grid-cols-8 gap-0.5">
            {popular.map(e => (
              <button key={e} type="button" className="grid size-8 place-items-center rounded-md text-lg hover:bg-hover"
                      onClick={() => { onChange(e); setOpen(false) }}>{e}</button>
            ))}
          </div>
          <EmojiField onPick={e => { onChange(e); setOpen(false) }} />
        </div>
      )}
    </div>
  )
}

/** Любой другой смайлик: поле, куда его можно вставить или выбрать системной панелью */
function EmojiField({ onPick }: { onPick: (e: string) => void }) {
  const input = useRef<HTMLInputElement>(null)
  return (
    <div className="mt-2 flex items-center gap-2 border-t border-border pt-2">
      <Input ref={input} className="h-8 flex-1" placeholder="Другой смайлик"
             onChange={ev => { const v = ev.target.value.trim(); if (v) onPick(v) }} />
      <Button variant="ghost" onClick={() => { input.current?.focus(); action("emojiPanel") }}>Все…</Button>
    </div>
  )
}
