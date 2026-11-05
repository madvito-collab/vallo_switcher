import * as SwitchPrimitive from "@radix-ui/react-switch"
import { cn } from "@/lib/utils"

/** shadcn/ui Switch: включено — системный акцент */
export function Switch({ className, ...props }: React.ComponentProps<typeof SwitchPrimitive.Root>) {
  return (
    <SwitchPrimitive.Root
      className={cn(
        "peer inline-flex h-[22px] w-[38px] shrink-0 items-center rounded-full p-[2px] transition-colors outline-none",
        "bg-input data-[state=checked]:bg-primary focus-visible:ring-2 focus-visible:ring-primary/50",
        "disabled:cursor-not-allowed disabled:opacity-50",
        className,
      )}
      {...props}
    >
      <SwitchPrimitive.Thumb
        className="pointer-events-none block size-[18px] rounded-full bg-white shadow-[0_1px_2px_rgba(0,0,0,.25)] transition-transform data-[state=checked]:translate-x-4"
      />
    </SwitchPrimitive.Root>
  )
}
