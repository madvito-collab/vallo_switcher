// «Где значок «ё»»: на Маке с чёлкой значок в строке меню может спрятаться. Схема + три шага + «Понятно»
import { useEffect, useRef, useState } from "react"
import appleLogo from "./assets/menubar-apple.svg"
import notch from "./assets/menubar-notch.svg"
import { Button } from "@/components/ui/button"
import { action } from "./bridge"
import { Card, Kbd } from "./kit"

/** Размер схемы в макете (Figma, «Frame 13»): верх экрана MacBook с чёлкой */
const W = 603, H = 83

/** Схема по макету автора, анимация в три такта (цикл 6 с): крайний значок у чёлки с ⌘ уводят вниз →
 *  «ё» выходит из-под чёлки на его место → «ё» с ⌘ тянут к часам, остальные значки сдвигаются.
 *  Рисуем в размерах макета и масштабируем под ширину карточки. */
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
  // координаты — внутри рамки (2 px), как в макете
  const icon = "absolute top-[8px] size-[12px] rounded-[4px] bg-white"
  const cmd = "absolute top-[24px] -translate-x-[2px] rounded-[4px] border border-black/15 bg-white px-[3px] text-[10px] font-medium leading-[14px] text-black shadow-sm"
  return (
    <div ref={box} className="relative w-full" style={{ height: H * scale }}>
      <style>{`
        @keyframes vallo-extra {
          0%, 8% { transform: translateY(0); opacity: 1; }
          24% { transform: translateY(30px); opacity: 1; }
          28%, 94% { transform: translateY(30px); opacity: 0; }
          95% { transform: translateY(0); opacity: 0; }
          100% { transform: translateY(0); opacity: 1; }
        }
        @keyframes vallo-extra-cmd { 0%, 7% { opacity: 0; } 10%, 24% { opacity: 1; } 27%, 100% { opacity: 0; } }
        @keyframes vallo-yo {
          0%, 30% { left: 350px; opacity: 1; }
          42%, 54% { left: 377px; opacity: 1; }
          80%, 90% { left: 549px; opacity: 1; }
          94% { left: 549px; opacity: 0; }
          95%, 100% { left: 350px; opacity: 1; }
        }
        @keyframes vallo-yo-cmd { 0%, 53% { opacity: 0; } 56%, 80% { opacity: 1; } 83%, 100% { opacity: 0; } }
        @keyframes vallo-row {
          0%, 54% { transform: translateX(0); }
          80%, 94% { transform: translateX(-20px); }
          95%, 100% { transform: translateX(0); }
        }
      `}</style>
      <div className="absolute left-0 top-0 origin-top-left overflow-hidden rounded-t-[16px] border-x-2 border-t-2 border-black"
           style={{ width: W, height: H, transform: `scale(${scale})`, background: "linear-gradient(to right, #9dc1fa, #dac2fa)" }}>
        <img src={appleLogo} alt="" className="absolute left-[9.67px] top-[5.75px] h-[17.022px] w-[14.016px]" />
        <div className="absolute left-[35px] top-[11px] flex items-center gap-[11px]">
          <span className="h-[8px] w-[51px] rounded-[6px] bg-white" />
          <span className="h-[8px] w-[42px] rounded-[6px] bg-white" />
          <span className="h-[8px] w-[42px] rounded-[6px] bg-white" />
        </div>
        {/* крайний значок у чёлки — его уберут */}
        <span className={icon} style={{ left: 377, animation: "vallo-extra 6s ease-in-out infinite" }} />
        <span className={cmd} style={{ left: 377, animation: "vallo-extra-cmd 6s ease-in-out infinite" }}>⌘</span>
        {/* остальные восемь значков: сдвигаются, освобождая место у часов */}
        <div className="absolute left-[397px] top-[8px] flex gap-[8px]" style={{ animation: "vallo-row 6s ease-in-out infinite" }}>
          {Array.from({ length: 8 }, (_, i) => <span key={i} className="size-[12px] rounded-[4px] bg-white" />)}
        </div>
        {/* «ё»: под чёлкой → на свободное место → к часам */}
        <div className="absolute top-[3px] flex flex-col items-center" style={{ animation: "vallo-yo 6s ease-in-out infinite" }}>
          <span className="w-[12px] text-center text-[16px] font-bold leading-[20px] text-white">ё</span>
          <span className="mt-[1px] rounded-[4px] border border-black/15 bg-white px-[3px] text-[10px] font-medium leading-[14px] text-black shadow-sm"
                style={{ animation: "vallo-yo-cmd 6s ease-in-out infinite" }}>⌘</span>
        </div>
        <span className="absolute left-[561px] top-[7px] whitespace-nowrap text-[11px] font-semibold text-black">12:32</span>
        {/* чёлка — поверх: под ней значок не виден */}
        <img src={notch} alt="" className="absolute left-[calc(50%+0.5px)] top-[-2px] z-10 h-[32px] w-[146px] -translate-x-1/2" />
      </div>
    </div>
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
