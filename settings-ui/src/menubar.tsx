// «Где значок «ё»»: на Маке с чёлкой значок в строке меню может спрятаться. Схема + три шага + «Понятно»
import { useEffect, useRef, useState } from "react"
import appleLogo from "./assets/menubar-apple.svg"
import notch from "./assets/menubar-notch.svg"
import yo from "./assets/menubar-yo.svg"
import { Button } from "@/components/ui/button"
import { action } from "./bridge"
import { Card, Kbd } from "./kit"

/** Размер схемы в макете: верх экрана MacBook с чёлкой */
const W = 603, H = 83

/** Раскадровка автора (Figma, кадры 18 → 13 → 15 → 16 → 17 → 14 → 19): положения в каждом кадре — точные, из макета.
 *  Координаты — как в Фигме (от края рамки, с её 2 px); в коде вычитаем рамку. */
type Pose = [x: number, y: number, opacity: number]
const STEP = 1000, HOLD = 600, FRAMES = 7, CYCLE = STEP * FRAMES  // кадр стоит 0,6 с, переход 0,4 с, цикл 7 с
const pct = (t: number) => `${((t / CYCLE) * 100).toFixed(3)}%`
const pose = ([x, y, o]: Pose) => `transform: translate(${x - 2}px, ${y - 2}px); opacity: ${o};`

/** Ключевые кадры одного элемента: в каждом кадре раскадровки стоит ровно в своей позе.
 *  fadeOut — из последнего кадра в первый не ехать, а погаснуть и появиться на старте */
function track(name: string, poses: Pose[], fadeOut = false) {
  const steps = poses.flatMap((p, k) => [`${pct(k * STEP)} { ${pose(p)} }`, `${pct(k * STEP + HOLD)} { ${pose(p)} }`])
  const [first, last] = [poses[0], poses[FRAMES - 1]]
  if (fadeOut) {
    const t = (FRAMES - 1) * STEP + HOLD + 200
    steps.push(`${pct(t)} { ${pose([last[0], last[1], 0])} }`, `${pct(t + 1)} { ${pose([first[0], first[1], 0])} }`)
  }
  steps.push(`100% { ${pose(first)} }`)
  return `@keyframes ${name} { ${steps.join(" ")} }`
}

const at = (x: number, n = FRAMES, y = 10, o = 1): Pose[] => Array.from({ length: n }, () => [x, y, o])
/** крайний значок у чёлки: ⌘ → вниз на 12 px → исчез (невидимым возвращается на место) */
const EXTRA: Pose[] = [[379, 10, 1], [379, 10, 1], [379, 22, 1], [379, 22, 0], [379, 22, 0], [379, 22, 0], [379, 10, 0]]
/** остальные восемь — всегда через 20 px (отступ 8): кадры 18–15 — от 399, 16–17 — от 396, 14 и 19 — от 379 */
const ROW: Pose[][] = Array.from({ length: 8 }, (_, i) => [
  [399 + 20 * i, 10, 1], [399 + 20 * i, 10, 1], [399 + 20 * i, 10, 1],
  [396 + 20 * i, 10, 1], [396 + 20 * i, 10, 1], [379 + 20 * i, 10, 1], [379 + 20 * i, 10, 1],
])
/** «ё»: под чёлкой → на место убранного (379) → к часам (539) */
const YO: Pose[] = [...at(350, 3), ...at(379, 2), ...at(539, 2)]
/** ⌘ под крайним значком (кадры 13, 15) и под «ё» (кадры 17, 14) */
const CMD_EXTRA: Pose[] = [[379, 26, 0], [379, 26, 1], [379, 38, 1], [379, 38, 0], ...at(379, 3, 26, 0)]
const CMD_YO: Pose[] = [...at(378, 4, 26, 0), [378, 26, 1], [537, 26, 1], [537, 26, 0]]

const KEYFRAMES = [
  track("vallo-extra", EXTRA),
  ...ROW.map((p, i) => track(`vallo-row-${i}`, p)),
  track("vallo-yo", YO, true),
  track("vallo-cmd-extra", CMD_EXTRA),
  track("vallo-cmd-yo", CMD_YO),
].join("\n")
const anim = (name: string) => ({ animation: `${name} ${CYCLE}ms ease-in-out infinite` })

/** Схема строки меню по макету автора; рисуем в размерах макета и масштабируем под ширину карточки */
function MenuBarSketch() {
  const box = useRef<HTMLDivElement>(null)
  const [scale, setScale] = useState(1)
  useEffect(() => {
    const el = box.current
    if (!el) return
    const update = () => setScale(el.clientWidth / W)
    update()
    const ro = new ResizeObserver(update)
    ro.observe(el)
    return () => ro.disconnect()
  }, [])
  const icon = "absolute left-0 top-0 size-[12px] rounded-[4px] bg-white"
  return (
    <div ref={box} className="relative w-full" style={{ height: H * scale }}>
      <style>{KEYFRAMES}</style>
      <div className="absolute left-0 top-0 origin-top-left overflow-hidden rounded-t-[16px] border-x-2 border-t-2 border-black"
           style={{ width: W, height: H, transform: `scale(${scale})`, borderColor: "#000", background: "linear-gradient(to right, #9dc1fa, #dac2fa)" }}>
        <img src={appleLogo} alt="" className="absolute left-[9.67px] top-[5.75px] h-[17.022px] w-[14.016px]" />
        <div className="absolute left-[35px] top-[11px] flex items-center gap-[11px]">
          <span className="h-[8px] w-[51px] rounded-[6px] bg-white" />
          <span className="h-[8px] w-[42px] rounded-[6px] bg-white" />
          <span className="h-[8px] w-[42px] rounded-[6px] bg-white" />
        </div>
        <img src={yo} alt="" className="absolute left-0 top-0 h-[12px] w-[9px]" style={anim("vallo-yo")} />
        <span className={icon} style={anim("vallo-extra")} />
        {ROW.map((_, i) => <span key={i} className={icon} style={anim(`vallo-row-${i}`)} />)}
        <CmdBadge style={anim("vallo-cmd-extra")} />
        <CmdBadge style={anim("vallo-cmd-yo")} />
        <span className="absolute left-[561px] top-[7px] whitespace-nowrap text-[11px] font-semibold text-black">12:32</span>
        {/* чёлка — поверх: под ней «ё» не видна */}
        <img src={notch} alt="" className="absolute left-[calc(50%+0.5px)] top-[-2px] z-10 h-[32px] w-[146px] -translate-x-1/2" />
      </div>
    </div>
  )
}

/** Плашка ⌘ из макета: 12×12, белая, скругление 4, знак 10 px */
function CmdBadge({ style }: { style: React.CSSProperties }) {
  return (
    <span className="absolute left-0 top-0 size-[12px] rounded-[4px] bg-white text-center text-[10px] font-semibold leading-[12px] text-black" style={style}>⌘</span>
  )
}

/** Карточка на «Основном». Нажали «Понятно» — больше не показываем (снова — если запустить свитчер из «Программ») */
export function MenuBarHint() {
  const [hidden, setHidden] = useState(false)
  if (hidden) return null
  return (
    <div className="mb-[26px]">
      <h3 className="mb-2 text-sm font-semibold">Где значок «ё»</h3>
      <Card>
        <div className="px-4 py-3.5">
          <MenuBarSketch />
          <ol className="mt-3.5 list-decimal space-y-1.5 pl-5 text-[13px]">
            <li>Значок «ё» — в строке меню справа, рядом с часами</li>
            <li>Не видно — значков слишком много, и «ё» спряталась за чёлкой. Уберите лишние — кнопка ниже откроет нужную страницу настроек. Системные значки (звук, Wi-Fi) можно и просто вытащить вниз, удерживая <Kbd>⌘</Kbd></li>
            <li>Когда «ё» появится, удерживая <Kbd>⌘</Kbd>, перетащите её к часам — там она не спрячется</li>
          </ol>
          <div className="mt-3.5 flex justify-end gap-2">
            <Button onClick={() => action("openMenuBarSettings")}>Открыть настройки строки меню</Button>
            <Button onClick={() => { setHidden(true); action("dismissMenuBarHint") }}>Понятно</Button>
          </div>
        </div>
      </Card>
    </div>
  )
}
