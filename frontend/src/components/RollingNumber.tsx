import clsx from 'clsx'
import { motion } from 'motion/react'

const DIGITS = ['0', '1', '2', '3', '4', '5', '6', '7', '8', '9']

/**
 * A number whose digits roll individually to their new value, like a departures board.
 * Screen readers get the plain text once; the animated strips are hidden from them.
 */
export function RollingNumber({ text, className }: { text: string; className?: string }) {
  const chars = Array.from(text)
  return (
    <span className={clsx('num relative inline-flex', className)}>
      <span className="sr-only">{text}</span>
      <span aria-hidden className="inline-flex">
        {chars.map((ch, i) => {
          // Key from the right so units stay in place when the number gains a digit.
          const key = chars.length - i
          return /\d/.test(ch) ? <Digit key={key} digit={Number(ch)} /> : <span key={`s${key}`}>{ch}</span>
        })}
      </span>
    </span>
  )
}

function Digit({ digit }: { digit: number }) {
  return (
    <span className="digit-window inline-block overflow-hidden" style={{ height: '1lh' }}>
      <motion.span
        className="flex flex-col"
        initial={false}
        animate={{ y: `${-digit * 10}%` }}
        transition={{ type: 'spring', stiffness: 210, damping: 26, mass: 0.9 }}
      >
        {DIGITS.map((d) => (
          <span key={d} style={{ height: '1lh' }}>
            {d}
          </span>
        ))}
      </motion.span>
    </span>
  )
}
