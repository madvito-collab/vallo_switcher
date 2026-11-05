// Страницы окна настроек. Тексты — по утверждённому мокапу docs/mockups/settings.html
import { ArrowRight, CircleCheck, Download, ExternalLink, Trash2, TriangleAlert } from "lucide-react"
import { Button } from "@/components/ui/button"
import { Switch } from "@/components/ui/switch"
import { action, set, type PageId, type State } from "./bridge"
import { AppList, Card, HotkeySelect, Kbd, Note, PageTitle, Row, SectionTitle, ToggleRow, WordList } from "./kit"
import appIcon from "./app-icon.png"

const osName = (s: State) => (s.platform === "mac" ? "macOS" : "Windows")

export function GeneralPage({ s, go }: { s: State; go: (p: PageId) => void }) {
  const { modelMissing, macAutocorrect } = s.attention
  const warn = <TriangleAlert className="size-[18px] shrink-0 text-warning" />
  return (
    <>
      <PageTitle>Основное</PageTitle>
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
      </Card>
    </>
  )
}

export function LayoutPage({ s }: { s: State }) {
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

      <SectionTitle title="Не переключать в приложениях" sub="Здесь раскладка меняется только вручную" />
      <AppList list="layout" apps={s.layout.apps} />

      <SectionTitle title="Не переключать слова" sub="Свитчер не будет их трогать" />
      <WordList list="layout" words={s.layout.words}
                empty="Пока пусто. Слово появится здесь само, если дважды отменить его переключение" />
    </>
  )
}

export function CorrectPage({ s }: { s: State }) {
  const c = s.correct
  return (
    <>
      <PageTitle>Исправления</PageTitle>
      <SectionTitle title="По сочетанию клавиш" first />
      <Card>
        <Row title="Исправить выделенный текст" desc={hotkeyDesc(s, "correct", "Опечатки, «ё», запятые и типографика разом")}>
          <HotkeySelect state={s} which="correct" />
        </Row>
        <ToggleRow title="Заглавная буква и точка" desc="Для писем и документов, в чатах лучше выключить"
                   value={c.capitalize} setting="capitalize" />
      </Card>

      <SectionTitle title="Пока вы печатаете" />
      <Card>
        <ToggleRow title="Типографика" desc="Тире, «ёлочки», неразрывные пробелы" value={c.typography} setting="typography" />
        <ToggleRow title="Опечатки" desc="првет → привет" value={c.typos} setting="typos" />
        <ToggleRow title="Буква «ё»" desc="еще → ещё, где без вариантов" value={c.yo} setting="yo" />
      </Card>
      <Note>Не то исправилось? Сразу нажмите <Kbd>Backspace</Kbd></Note>

      <SectionTitle title="Не исправлять в приложениях" sub="Здесь не работает исправление на лету" />
      <AppList list="correct" apps={c.apps} />

      <SectionTitle title="Не исправлять слова" sub="Опечатки и запятые их не тронут" />
      <WordList list="correct" words={c.words}
                empty="Пока пусто. Слово появится здесь само, если сразу отменить его исправление клавишей Backspace" />
    </>
  )
}

/** Подпись под сочетанием: ошибка «уже занято» — вместо обычной */
function hotkeyDesc(s: State, which: "convert" | "correct", normal: string) {
  if (s.hotkeyError?.action === which) return <span className="text-destructive">{s.hotkeyError.text}</span>
  return normal
}

export function FigmaPage() {
  return (
    <>
      <PageTitle>Figma</PageTitle>
      <Card>
        <Row title="Плагин Vallo Typograph" desc="Исправляет все тексты в выделенных фреймах">
          <Button variant="default" onClick={() => action("openFigmaPlugin")}><ExternalLink />Установить</Button>
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
      <Card><Examples pairs={[["ghbdtn", "привет"], [",eltn", "будет"]]} /></Card>
      <Note>Свитчер сам исправит слово и переключит раскладку. Не нужно — дважды нажмите левый <Kbd>Shift</Kbd>, и всё вернётся</Note>

      {s.correct.available && (
        <>
          <SectionTitle title="Исправления" />
          <Card>
            <Examples pairs={[["првет", "привет"], ["еще", "ещё"], ["\"текст\" - тут", "«текст» — тут"], ["короче я хз", "короче, я хз"]]} />
          </Card>
          <Note>Выделите текст и дважды нажмите правый <Kbd>Shift</Kbd> — исправится всё разом, вместе с запятыми</Note>
        </>
      )}

      <SectionTitle title="Где свитчер молчит" />
      <Card>
        <Row desc="Пароли, почта, телефоны и ссылки · приложения из списков «Не переключать» и «Не исправлять» · раскладки кроме русской и английской" />
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

      <SectionTitle title="Обновления" />
      <Card>
        <Row title="Обновлять автоматически" desc="Новые версии ставятся сами при перезапуске">
          <Switch checked={s.about.autoUpdate} onCheckedChange={v => set("autoUpdate", v)} aria-label="Обновлять автоматически" />
        </Row>
        <Row title={checked(s.about.lastCheck)} desc="Свитчер проверяет раз в день">
          <Button onClick={() => action("checkUpdates")}>Проверить</Button>
        </Row>
      </Card>

      {s.correct.available && (
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
