// Окно настроек: боковое меню слева, страница справа
import { useEffect, useState } from "react"
import { CircleHelp, Info, Languages, Settings, SpellCheck } from "lucide-react"
import { hasHost, onState, send, type PageId, type State } from "./bridge"
import { sample } from "./sample"
import { AboutPage, CorrectPage, FigmaPage, GeneralPage, HelpPage, LayoutPage } from "./pages"
import { cn } from "@/lib/utils"

/** Логотип Figma в стиле Lucide (в самом наборе брендовых значков больше нет) */
function FigmaIcon(props: React.SVGProps<SVGSVGElement>) {
  return (
    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth={1.75} strokeLinecap="round" strokeLinejoin="round" {...props}>
      <path d="M5 5.5A3.5 3.5 0 0 1 8.5 2H12v7H8.5A3.5 3.5 0 0 1 5 5.5z" />
      <path d="M12 2h3.5a3.5 3.5 0 1 1 0 7H12V2z" />
      <path d="M12 12.5a3.5 3.5 0 1 1 7 0 3.5 3.5 0 1 1-7 0z" />
      <path d="M5 19.5A3.5 3.5 0 0 1 8.5 16H12v3.5a3.5 3.5 0 1 1-7 0z" />
      <path d="M5 12.5A3.5 3.5 0 0 1 8.5 9H12v7H8.5A3.5 3.5 0 0 1 5 12.5z" />
    </svg>
  )
}

type Icon = React.ComponentType<{ className?: string; strokeWidth?: number }>

function NavItem({ id, icon: Icon, title, page, go }: { id: PageId; icon: Icon; title: string; page: PageId; go: (p: PageId) => void }) {
  const active = page === id
  return (
    <button
      onClick={() => go(id)}
      className={cn(
        "flex w-full items-center gap-2.5 rounded-lg px-2.5 py-[7px] text-left outline-none hover:bg-hover focus-visible:ring-2 focus-visible:ring-primary/50",
        active && "bg-hover font-medium",
      )}
    >
      <Icon className={cn("size-[17px]", active ? "text-foreground" : "text-muted-foreground")} strokeWidth={1.75} />
      {title}
    </button>
  )
}

export default function App() {
  const [state, setState] = useState<State | null>(hasHost ? null : sample)
  const [page, setPage] = useState<PageId>("general")

  useEffect(() => {
    onState(s => {
      setState(s)
      // приложение просит открыть окно на нужной странице (меню у часов: «Справка», «О программе»)
      if (s.page) setPage(s.page)
    })
    send({ type: "ready" })
  }, [])

  if (!state) return null
  const correct = state.correct.available
  const go = (p: PageId) => {
    setPage(p)
    document.getElementById("page")?.scrollTo(0, 0)
  }

  return (
    <div className="grid h-full grid-cols-[236px_1fr]">
      <aside className={cn("flex flex-col border-r border-border bg-sidebar px-2.5 pb-3.5", state.platform === "mac" ? "pt-[52px]" : "pt-4")}>
        <h1 className="mx-2 mb-3.5 text-xl font-semibold">Настройки</h1>
        <nav className="flex flex-col gap-px">
          <NavItem id="general" icon={Settings} title="Основное" page={page} go={go} />
          <NavItem id="layout" icon={Languages} title="Раскладка" page={page} go={go} />
          {correct && <NavItem id="correct" icon={SpellCheck} title="Исправления" page={page} go={go} />}
          {state.integrations.figma && (
            <>
              <div className="mx-2.5 mt-3.5 mb-1.5 text-xs text-muted-foreground">Интеграции</div>
              <NavItem id="figma" icon={FigmaIcon} title="Figma" page={page} go={go} />
            </>
          )}
        </nav>
        <div className="flex-1" />
        <nav className="flex flex-col gap-px">
          <NavItem id="help" icon={CircleHelp} title="Справка" page={page} go={go} />
          <NavItem id="about" icon={Info} title="О программе" page={page} go={go} />
        </nav>
      </aside>
      <main id="page" className="overflow-y-auto px-12 pt-9 pb-12">
        <div className="max-w-[600px]">
          {page === "general" && <GeneralPage s={state} go={go} />}
          {page === "layout" && <LayoutPage s={state} />}
          {page === "correct" && <CorrectPage s={state} />}
          {page === "figma" && <FigmaPage />}
          {page === "help" && <HelpPage s={state} />}
          {page === "about" && <AboutPage s={state} />}
        </div>
      </main>
    </div>
  )
}
