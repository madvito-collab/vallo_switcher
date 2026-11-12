// Страницы окна настроек. Тексты — по утверждённому мокапу docs/mockups/settings.html
import { ArrowRight, CircleCheck, Download, ExternalLink, LoaderCircle, Trash2, TriangleAlert } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Switch } from "@/components/ui/switch"
import { action, set, type ListId, type PageId, type State } from "./bridge"
import { AppList, BackButton, Card, HotkeySelect, Kbd, LinkRow, Note, PageTitle, Row, SectionTitle, ToggleRow, WordList } from "./kit"
import { EmojiCustom, EmojiRow } from "./emoji"
import { MenuBarHint } from "./menubar"
import appIcon from "./app-icon.png"

const osName = (s: State) => (s.platform === "mac" ? "macOS" : "Windows")

export function GeneralPage({ s, go }: { s: State; go: (p: PageId) => void }) {
  const { modelMissing, macAutocorrect } = s.attention
  const warn = <TriangleAlert className="size-[18px] shrink-0 text-warning" />
  return (
    <>
      <PageTitle>Основное</PageTitle>
      {s.general.menuBarHint && <MenuBarHint />}
      {(modelMissing || macAutocorrect) && (
        <>
          <SectionTitle title="Требует внимания" first />
          <Card>
            {modelMissing && (
              <Row icon={warn} title="Модель запятых не скачана" desc="Без неё запятые не расставляются">
                <Button onClick={() => go("about")}>Скачать</Button>
              </Row>
            )}
            {macAutocorrect && (
              <Row icon={warn} title="Мешает автозамена macOS" desc="Она спорит со свитчером — лучше выключить">
                <Button onClick={() => action("openKeyboardSettings")}>Открыть настройки</Button>
              </Row>
            )}
          </Card>
        </>
      )}
      <SectionTitle title="Приложение" first={!(modelMissing || macAutocorrect)} />
      <Card>
        <ToggleRow title="Запускать вместе с системой" desc={`Свитчер включится сам после перезагрузки ${osName(s)}`}
                   value={s.general.launchAtLogin} setting="launchAtLogin" />
        <ToggleRow title="Режим отладки" desc="Пишет журнал для разбора ошибок, обычно не нужен"
                   value={s.general.debugMode} setting="debugMode" />
        {/* на Маке журнал читают в Терминале, на Windows это файл */}
        {s.platform === "windows" && s.general.debugMode && (
          <Row title="Журнал" desc="Только что и когда сделал свитчер — без вашего текста. Приложите файл к вопросу на GitHub">
            <Button onClick={() => action("openLog")}>Открыть журнал</Button>
          </Row>
        )}
      </Card>
    </>
  )
}

/** Исключения — на своих подстраницах, как в Системных настройках: их бывает много */
export type Sub = { kind: "words" | "apps"; list: ListId }

const subs: Record<"words" | "apps", Record<ListId, { title: string; desc: string; empty?: string }>> = {
  words: {
    layout: { title: "Не переключать слова", desc: "Свитчер не будет их трогать",
              empty: "Пока пусто. Слово появится здесь само, если дважды отменить его переключение" },
    correct: { title: "Не исправлять слова", desc: "Опечатки и запятые их не тронут",
               empty: "Пока пусто. Слово появится здесь само, если сразу отменить его исправление клавишей Backspace" },
  },
  apps: {
    layout: { title: "Не переключать в приложениях", desc: "Здесь раскладка меняется только вручную" },
    correct: { title: "Не исправлять в приложениях", desc: "Здесь не работает исправление на лету" },
  },
}
const backTitle: Record<ListId, string> = { layout: "Раскладка", correct: "Исправления" }

export function SubPage({ s, sub, back }: { s: State; sub: Sub; back: () => void }) {
  const t = subs[sub.kind][sub.list]
  const data = sub.list === "layout" ? s.layout : s.correct
  return (
    <>
      <BackButton title={backTitle[sub.list]} onClick={back} />
      <PageTitle>{t.title}</PageTitle>
      <p className="-mt-3 mb-4 text-[13px] text-muted-foreground">{t.desc}</p>
      {sub.kind === "words"
        ? <WordList list={sub.list} words={data.words} empty={t.empty!} />
        : <AppList list={sub.list} apps={data.apps} />}
    </>
  )
}

/** Строки-переходы к исключениям раздела */
function Exclusions({ s, list, open }: { s: State; list: ListId; open: (sub: Sub) => void }) {
  const data = list === "layout" ? s.layout : s.correct
  return (
    <>
      <SectionTitle title="Исключения" />
      <Card>
        <LinkRow title={subs.apps[list].title} desc={subs.apps[list].desc} count={data.apps.length}
                 onClick={() => open({ kind: "apps", list })} />
        {(list === "layout" || s.correct.typosAvailable !== false) &&
          <LinkRow title={subs.words[list].title} desc={subs.words[list].desc} count={data.words.length}
                   onClick={() => open({ kind: "words", list })} />}
      </Card>
    </>
  )
}

export function LayoutPage({ s, open }: { s: State; open: (sub: Sub) => void }) {
  return (
    <>
      <PageTitle>Раскладка</PageTitle>
      <Card>
        <ToggleRow title="Переключать автоматически" desc="Исправляет слова, набранные не в той раскладке: ghbdtn станет «привет»"
                   value={s.layout.autoSwitch} setting="autoSwitch" />
        <Row title="Сочетание клавиш"
             desc={hotkeyDesc(s, "convert", "Меняет раскладку набранного или выделенного текста, а сразу после автоисправления — отменяет его")}>
          <HotkeySelect state={s} which="convert" />
        </Row>
      </Card>

      <Exclusions s={s} list="layout" open={open} />
    </>
  )
}

export function CorrectPage({ s, open }: { s: State; open: (sub: Sub) => void }) {
  const c = s.correct
  // на Windows пока только типографика и смайлики — строк про опечатки, «ё» и запятые не показываем
  const typos = c.typosAvailable !== false
  return (
    <>
      <PageTitle>Исправления</PageTitle>
      <SectionTitle title="По сочетанию клавиш" first />
      <Card>
        <Row title="Исправить выделенный текст"
             desc={hotkeyDesc(s, "correct", typos ? "Опечатки, «ё», запятые и типографика разом" : "Тире, кавычки, неразрывные пробелы, телефоны")}>
          <HotkeySelect state={s} which="correct" />
        </Row>
        {typos && <ToggleRow title="Заглавная буква и точка" desc="Для писем и документов, в чатах лучше выключить"
                             value={c.capitalize} setting="capitalize" />}
      </Card>

      <SectionTitle title="Пока вы печатаете" />
      <Card>
        <ToggleRow title="Типографика" desc="Тире, «ёлочки», неразрывные пробелы" value={c.typography} setting="typography" />
        {typos && <ToggleRow title="Опечатки" desc="првет → привет" value={c.typos} setting="typos" />}
        {typos && <ToggleRow title="Буква «ё»" desc="еще → ещё, где без вариантов" value={c.yo} setting="yo" />}
        <EmojiRow s={s} />
      </Card>
      <Note>Не то исправилось? Сразу нажмите <Kbd>Backspace</Kbd></Note>

      {c.emoji.preset === "custom" && (
        <>
          <SectionTitle title="Свои смайлики" sub="Слева скобки, справа смайлик" />
          <EmojiCustom pairs={c.emoji.custom} />
        </>
      )}

      <Exclusions s={s} list="correct" open={open} />
    </>
  )
}

/** Подпись под сочетанием: ошибка «уже занято» — вместо обычной */
function hotkeyDesc(s: State, which: "convert" | "correct", normal: string) {
  if (s.hotkeyError?.action === which) return <span className="text-destructive">{s.hotkeyError.text}</span>
  return normal
}

export function FigmaPage({ published }: { published: boolean }) {
  return (
    <>
      <PageTitle>Figma</PageTitle>
      <Card>
        <Row title="Плагин Vallo Typograph" desc="Исправляет все тексты в выделенных фреймах">
          {published
            ? <Button variant="default" onClick={() => action("openFigmaPlugin")}><ExternalLink />Установить</Button>
            : <Button disabled>Скоро</Button>}
        </Row>
      </Card>
      <Note>Ставится один раз. Дальше выделите фреймы и нажмите сочетание исправлений — плагин запустится сам</Note>
    </>
  )
}

function Examples({ pairs }: { pairs: [string, string][] }) {
  return (
    <div className="grid grid-cols-[1fr_auto_1fr] items-center gap-x-2.5 gap-y-1.5 px-4 py-3 text-[13px]">
      {pairs.map(([from, to]) => (
        <Pair key={from} from={from} to={to} />
      ))}
    </div>
  )
}
const Pair = ({ from, to }: { from: string; to: string }) => (
  <>
    <span className="font-mono text-muted-foreground">{from}</span>
    <ArrowRight className="size-3.5 text-muted-foreground" />
    <span>{to}</span>
  </>
)

export function HelpPage({ s }: { s: State }) {
  return (
    <>
      <PageTitle>Справка</PageTitle>
      <SectionTitle title="Раскладка" first />
      <Card><Examples pairs={[["ghbdtn", "привет"], [",eltn", "будет"], ["№аааааа", "#ffffff"]]} /></Card>
      <Note>Свитчер сам исправит слово и переключит раскладку. Не нужно — дважды нажмите левый <Kbd>Shift</Kbd>, и всё вернётся</Note>

      {s.correct.available && (
        <>
          <SectionTitle title="Исправления" />
          <Card>
            <Examples pairs={s.correct.typosAvailable === false
              ? [["\"текст\" - тут", "«текст» — тут"], ["89161234567", "+7 916 123-45-67"], ["5000р", "5 000 ₽"], ["ок))", "ок😄"]]
              : [["првет", "привет"], ["еще", "ещё"], ["\"текст\" - тут", "«текст» — тут"], ["89161234567", "+7 916 123-45-67"], ["5000р", "5 000 ₽"], ["короче я хз", "короче, я хз"]]} />
          </Card>
          <Note>{s.correct.typosAvailable === false
            ? <>Выделите текст и дважды нажмите правый <Kbd>Shift</Kbd> — расставится типографика</>
            : <>Выделите текст и дважды нажмите правый <Kbd>Shift</Kbd> — исправится всё разом, вместе с запятыми</>}</Note>
        </>
      )}

      <SectionTitle title="Где свитчер молчит" />
      <Card>
        <Row desc={s.platform === "mac"
          ? "Пароли, почта, телефоны и ссылки · приложения из списков «Не переключать» и «Не исправлять» · раскладки кроме русской и английской"
          : "Пароли, полноэкранные игры · приложения из списков «Не переключать» и «Не исправлять» · раскладки кроме русской и английской"} />
      </Card>
      <Note>
        <a className="cursor-pointer text-primary" onClick={() => action("openFullHelp")}>Полная справка</a>
        {" · "}
        <a className="cursor-pointer text-primary" onClick={() => action("openGitHub")}>Вопрос или ошибка — на GitHub</a>
      </Note>
    </>
  )
}

/** «Проверено сегодня/вчера/5 ноября» */
function checked(iso: string | null) {
  if (!iso) return "Ещё не проверяли"
  const d = new Date(iso)
  const days = Math.floor((new Date().setHours(0, 0, 0, 0) - new Date(d).setHours(0, 0, 0, 0)) / 864e5)
  if (days <= 0) return "Проверено сегодня"
  if (days === 1) return "Проверено вчера"
  return `Проверено ${d.toLocaleDateString("ru-RU", { day: "numeric", month: "long" })}`
}

/** Строка «Проверить»: что сейчас с обновлением — проверяем, качаем (с процентом), готово к перезапуску */
function UpdateRow({ s }: { s: State }) {
  const u = s.about.update
  const pct = Math.round((u?.progress ?? 0) * 100)
  switch (u?.state) {
    case "checking":
      return (
        <Row title={checked(s.about.lastCheck)} desc="Свитчер проверяет раз в день">
          <Button disabled><LoaderCircle className="animate-spin" />Проверяю…</Button>
        </Row>
      )
    case "upToDate":
      return (
        <Row title={checked(s.about.lastCheck)} desc={`У вас последняя версия — ${s.version}`}>
          <Button onClick={() => action("checkUpdates")}>Проверить</Button>
        </Row>
      )
    case "downloading":
      return (
        <Row title={`Скачиваю версию ${u.version}`} desc="Можно пользоваться свитчером, это не мешает">
          <div className="flex items-center gap-2.5">
            <div className="h-1.5 w-[120px] overflow-hidden rounded-full bg-hover">
              <div className="h-full bg-primary transition-[width]" style={{ width: `${pct}%` }} />
            </div>
            <span className="w-9 text-[13px] tabular-nums text-muted-foreground">{pct}%</span>
          </div>
        </Row>
      )
    case "available":
      return (
        <Row title={`Вышла версия ${u.version}`} desc="Скачается и поставится за пару минут">
          <Button variant="default" onClick={() => action("installUpdate")}><Download />Обновить</Button>
        </Row>
      )
    case "ready":
      return (
        <Row title={`Версия ${u.version} готова`} desc="Перезапуск займёт пару секунд">
          <Button variant="default" onClick={() => action("installUpdate")}>Перезапустить</Button>
        </Row>
      )
    case "failed":
      return (
        <Row title={checked(s.about.lastCheck)}
             desc={<span className="text-destructive">Не получилось проверить обновления. Проверьте интернет</span>}>
          <Button onClick={() => action("checkUpdates")}>Повторить</Button>
        </Row>
      )
    default:
      return (
        <Row title={checked(s.about.lastCheck)} desc="Свитчер проверяет раз в день">
          <Button onClick={() => action("checkUpdates")}>Проверить</Button>
        </Row>
      )
  }
}

export function AboutPage({ s }: { s: State }) {
  const m = s.about.model
  return (
    <>
      <PageTitle>О программе</PageTitle>
      <Card>
        <div className="flex items-center gap-4 px-4 py-[18px]">
          <img src={appIcon} alt="" className="size-16" />
          <div>
            <div className="text-lg font-semibold">Vallo Switcher</div>
            <div className="text-muted-foreground">Версия {s.version} · бесплатно, открытый код</div>
            <a className="mt-1 inline-block cursor-pointer text-primary" onClick={() => action("openGitHub")}>Сообщить об ошибке на GitHub</a>
          </div>
        </div>
      </Card>

      {s.about.updates && (
        <>
          <SectionTitle title="Обновления" />
          <Card>
            <Row title="Обновлять автоматически" desc="Новые версии ставятся сами при перезапуске">
              <Switch checked={s.about.autoUpdate} onCheckedChange={v => set("autoUpdate", v)} aria-label="Обновлять автоматически" />
            </Row>
            <UpdateRow s={s} />
          </Card>
        </>
      )}

      {/* на Windows запятых пока нет — кнопка «Скачать» там ничего бы не делала */}
      {s.correct.available && s.correct.typosAvailable !== false && (
        <>
          <SectionTitle title="Модель запятых" />
          <Card>
            <Row title="SAGE" desc={m.state === "failed" ? <span className="text-destructive">{m.message}</span>
                                                         : `Расставляет запятые без интернета · ${m.size.replace(" ", "\u00A0")}`}>
              {m.state === "notDownloaded" && <Button variant="default" onClick={() => action("model.download")}><Download />Скачать</Button>}
              {m.state === "failed" && <Button onClick={() => action("model.download")}>Повторить</Button>}
              {m.state === "downloading" && (
                <div className="flex items-center gap-2.5">
                  <div className="h-1.5 w-[120px] overflow-hidden rounded-full bg-hover">
                    <div className="h-full bg-primary transition-[width]" style={{ width: `${Math.round(m.progress * 100)}%` }} />
                  </div>
                  <span className="w-9 text-[13px] tabular-nums text-muted-foreground">{Math.round(m.progress * 100)}%</span>
                  <Button onClick={() => action("model.cancel")}>Отмена</Button>
                </div>
              )}
              {m.state === "ready" && (
                <div className="flex items-center gap-2">
                  <span className="inline-flex items-center gap-1.5 text-[13px] font-medium text-success"><CircleCheck className="size-[15px]" />Скачана</span>
                  <Button variant="destructive" size="icon" onClick={() => action("model.delete")} aria-label="Удалить модель"><Trash2 /></Button>
                </div>
              )}
            </Row>
          </Card>
        </>
      )}
    </>
  )
}
