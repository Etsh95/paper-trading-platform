import clsx from 'clsx'
import { motion } from 'motion/react'
import { useRef, type KeyboardEvent } from 'react'

export type Tone = 'rise' | 'fall' | 'lantern' | 'neutral'

export interface SegmentOption<T extends string> {
  value: T
  label: string
  tone?: Tone
}

const indicator: Record<Tone, string> = {
  rise: 'bg-rise',
  fall: 'bg-fall',
  lantern: 'bg-lantern',
  neutral: 'bg-deck shadow-[0_0_0_1px_var(--rule)]',
}

const activeText: Record<Tone, string> = {
  rise: 'text-on-signal',
  fall: 'text-on-signal',
  lantern: 'text-lantern-ink',
  neutral: 'text-fog',
}

/**
 * Radio-group toggle whose selection indicator slides between options (shared layout animation).
 * Arrow keys move the selection; in RTL the arrows follow the visual direction.
 */
export function Segmented<T extends string>(props: {
  id: string
  label: string
  options: SegmentOption<T>[]
  value: T
  onChange: (value: T) => void
  size?: 'md' | 'sm'
}) {
  const { id, label, options, value, onChange, size = 'md' } = props
  const refs = useRef<(HTMLButtonElement | null)[]>([])

  const onKeyDown = (e: KeyboardEvent<HTMLDivElement>) => {
    const rtl = getComputedStyle(e.currentTarget).direction === 'rtl'
    const steps: Record<string, number> = { ArrowRight: rtl ? -1 : 1, ArrowLeft: rtl ? 1 : -1, ArrowDown: 1, ArrowUp: -1 }
    const step = steps[e.key]
    if (!step) return
    e.preventDefault()
    const index = options.findIndex((o) => o.value === value)
    const next = (index + step + options.length) % options.length
    onChange(options[next]!.value)
    refs.current[next]?.focus()
  }

  return (
    <div
      role="radiogroup"
      aria-label={label}
      onKeyDown={onKeyDown}
      className="grid auto-cols-fr grid-flow-col gap-1 rounded-lg border border-rule bg-harbor p-1"
    >
      {options.map((option, i) => {
        const active = option.value === value
        return (
          <button
            key={option.value}
            ref={(el) => {
              refs.current[i] = el
            }}
            type="button"
            role="radio"
            aria-checked={active}
            tabIndex={active ? 0 : -1}
            onClick={() => onChange(option.value)}
            className={clsx(
              'relative isolate rounded-md font-medium transition-colors duration-200',
              size === 'md' ? 'px-3 py-2 text-base' : 'px-2.5 py-1.5 text-sm',
              active ? activeText[option.tone ?? 'neutral'] : 'text-muted hover:text-fog',
            )}
          >
            {active && (
              <motion.span
                layoutId={`${id}-indicator`}
                className={clsx('absolute inset-0 -z-10 rounded-md', indicator[option.tone ?? 'neutral'])}
                transition={{ type: 'spring', stiffness: 520, damping: 40 }}
              />
            )}
            {option.label}
          </button>
        )
      })}
    </div>
  )
}
