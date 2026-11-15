import clsx from 'clsx'
import { AnimatePresence, motion } from 'motion/react'
import type { ReactNode, RefObject } from 'react'

/**
 * Decimal input with a unit suffix. A text input with inputmode="decimal" rather than type="number":
 * number inputs coerce to floats, reject locale separators and change value on scroll.
 */
export function NumberField(props: {
  id: string
  label: string
  unit: string
  value: string
  onChange: (value: string) => void
  onBlur?: () => void
  error?: string
  inputRef?: RefObject<HTMLInputElement | null>
  action?: ReactNode
}) {
  const { id, label, unit, value, onChange, onBlur, error, inputRef, action } = props
  const errorId = `${id}-error`
  return (
    <div className="flex flex-col gap-1.5">
      <div className="flex items-baseline justify-between">
        <label htmlFor={id} className="text-sm font-medium">
          {label}
        </label>
        {action}
      </div>
      <div
        className={clsx(
          // The whole field (input + unit) shows focus, instead of an outline around the input alone.
          'flex items-center rounded-lg border bg-harbor transition-colors focus-within:border-lantern focus-within:ring-2 focus-within:ring-lantern/30',
          error ? 'border-fall' : 'border-rule',
        )}
      >
        <input
          id={id}
          ref={inputRef}
          value={value}
          onChange={(e) => onChange(e.target.value)}
          onBlur={onBlur}
          inputMode="decimal"
          autoComplete="off"
          spellCheck={false}
          placeholder="0.00"
          aria-invalid={error ? true : undefined}
          aria-describedby={error ? errorId : undefined}
          className="num min-w-0 flex-1 bg-transparent px-3 py-2.5 text-base outline-none focus-visible:outline-none placeholder:text-muted/60"
          dir="ltr"
        />
        <span className="pe-3 text-sm text-muted">{unit}</span>
      </div>
      <AnimatePresence initial={false}>
        {error && (
          <motion.p
            id={errorId}
            initial={{ opacity: 0, height: 0 }}
            animate={{ opacity: 1, height: 'auto' }}
            exit={{ opacity: 0, height: 0 }}
            className="text-sm text-fall"
          >
            {error}
          </motion.p>
        )}
      </AnimatePresence>
    </div>
  )
}
