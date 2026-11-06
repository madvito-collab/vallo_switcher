// Общие кирпичики страниц: карточка, строка, заголовки, списки приложений и слов, выбор сочетания
import { useState } from "react"
import { Plus, Trash2, X, AppWindow } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Switch } from "@/components/ui/switch"
import { Select, SelectContent, SelectItem, SelectSeparator, SelectTrigger, SelectValue } from "@/components/ui/select"
import { action, set, type AppEntry, type ListId, type State } from "./bridge"
import { cn } from "@/lib/utils"

export function PageTitle({ children }: { children: React.ReactNode }) {
  return <h2 className="mb-[22px] text-[26px] font-semibold tracking-[-0.01em]">{children}</h2>
}

export function SectionTitle({ title, sub, first }: { title: string; sub?: string; first?: boolean }) {
  return (
    <>
      <h3 className={cn("mb-2 text-sm font-semibold", first ? "mt-0" : "mt-[26px]")}>{title}</h3>
      {sub && <p className="-mt-1 mb-2.5 text-[13px] text-muted-foreground">{sub}</p>}
    </>
  )
}

export function Card({ children }: { children: React.ReactNode }) {
  return <div className="rounded-xl border border-border bg-card [&>*+*]:border-t [&>*+*]:border-border">{children}</div>
}

export function Note({ children }: { children: React.ReactNode }) {
  return <p className="mx-1 mt-2 text-[12.5px] text-muted-foreground">{children}</p>
}

export function Row({ title, desc, icon, children }: {
  title?: React.ReactNode; desc?: React.ReactNode; icon?: React.ReactNode; children?: React.ReactNode
}) {
  return (
    <div className="flex min-h-[52px] items-center gap-4 px-4 py-3">
      {icon}
      <div className="min-w-0 flex-1">
        {title && <div className="font-medium">{title}</div>}
        {desc && <div className="mt-px text-[13px] text-muted-foreground">{desc}</div>}
      </div>
      {children}
    </div>
  )
}

/** Строка с переключателем: ключ уходит в приложение как есть */
export function ToggleRow({ title, desc, value, setting }: { title: string; desc: string; value: boolean; setting: string }) {
  return (
    <Row title={title} desc={desc}>
      <Switch checked={value} onCheckedChange={v => set(setting, v)} aria-label={title} />
    </Row>
  )
}

export function Kbd({ children }: { children: React.ReactNode }) {
  return <kbd className="rounded-[5px] border border-border bg-hover px-1.5 py-px font-mono text-xs">{children}</kbd>
}

/** Выбор сочетания: двойные модификаторы, своё сочетание (запись), для исправлений — «Выключено» */
export function HotkeySelect({ state, which }: { state: State; which: "convert" | "correct" }) {
  const current = which === "convert" ? state.layout : state.correct
  const key = which === "convert" ? "hotkey.convert" : "hotkey.correct"
  if (state.recording === which) {
    return (
      <div className="flex items-center gap-1.5">
        <div className="flex h-8 min-w-[170px] items-center rounded-lg border-2 border-primary bg-field px-2.5 text-sm text-muted-foreground">
          Нажмите сочетание
        </div>
        <Button variant="ghost" size="icon" onClick={() => action("cancelRecording")} aria-label="Отмена"><X /></Button>
      </div>
    )
  }
  return (
    <Select value={current.hotkey} onValueChange={v => set(key, v)}>
      <SelectTrigger aria-label="Сочетание клавиш"><SelectValue /></SelectTrigger>
      <SelectContent>
        {state.hotkeyOptions.map(o => <SelectItem key={o.id} value={o.id}>{o.title}</SelectItem>)}
        {(state.canRecordHotkey || which === "correct" || current.hotkey === "custom") && <SelectSeparator />}
        {current.hotkey === "custom" && <SelectItem value="custom">{current.hotkeyTitle}</SelectItem>}
        {state.canRecordHotkey && <SelectItem value="record">Своё сочетание…</SelectItem>}
        {which === "correct" && <SelectItem value="off">Выключено</SelectItem>}
      </SelectContent>
    </Select>
  )
}

/** Список приложений-исключений */
export function AppList({ list, apps }: { list: ListId; apps: AppEntry[] }) {
  const remove = (id: string) => set(`apps.${list}`, apps.filter(a => a.id !== id).map(a => a.id))
  return (
    <Card>
      {apps.map(app => (
        <Row key={app.id} icon={
          app.icon
            ? <img src={app.icon} alt="" className="size-6" />
            : <span className="grid size-6 place-items-center rounded-md bg-hover"><AppWindow className="size-[15px] text-muted-foreground" /></span>
        } title={<span className="font-normal">{app.name}</span>}>
          <Button variant="destructive" size="icon" onClick={() => remove(app.id)} aria-label={`Убрать ${app.name}`}><Trash2 /></Button>
        </Row>
      ))}
      <div className="px-4 py-3">
        <Button onClick={() => action("pickApps", list)}><Plus />Добавить приложение</Button>
      </div>
    </Card>
  )
}

/** Список слов с полем «Добавить слово» */
export function WordList({ list, words, empty }: { list: ListId; words: string[]; empty: string }) {
  const [word, setWord] = useState("")
  const add = () => {
    const w = word.trim()
    if (!w) return
    action("addWord", { list, word: w })
    setWord("")
  }
  const remove = (w: string) => set(`words.${list}`, words.filter(x => x !== w))
  return (
    <Card>
      {words.length === 0 && <div className="px-4 py-3.5 text-[13px] text-muted-foreground">{empty}</div>}
      {words.map(w => (
        <Row key={w} title={<span className="font-normal">{w}</span>}>
          <Button variant="destructive" size="icon" onClick={() => remove(w)} aria-label={`Убрать ${w}`}><Trash2 /></Button>
        </Row>
      ))}
      <div className="flex gap-2 px-4 py-3">
        <Input placeholder="Добавить слово" value={word} onChange={e => setWord(e.target.value)}
               onKeyDown={e => { if (e.key === "Enter") add() }} />
        <Button onClick={add} disabled={!word.trim()}>Добавить</Button>
      </div>
    </Card>
  )
}
