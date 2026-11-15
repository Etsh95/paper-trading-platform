import clsx from 'clsx'
import { RotateCcw } from 'lucide-react'
import { useState } from 'react'
import { useTranslation } from 'react-i18next'
import { toast } from 'sonner'
import { ApiError } from '@/shared/api/client'
import { renewSession } from '@/shared/api/session'

/** Two-step confirm instead of a modal dialog: first click arms, second click resets. */
export function ResetAccount() {
  const { t } = useTranslation()
  const [armed, setArmed] = useState(false)
  const [busy, setBusy] = useState(false)

  const onClick = async () => {
    if (!armed) {
      setArmed(true)
      setTimeout(() => setArmed(false), 4_000)
      return
    }
    setBusy(true)
    try {
      await renewSession('created')
      toast(t('toast.newAccount'))
    } catch (error) {
      const code = error instanceof ApiError ? error.code : 'unknown'
      toast.error(t('toast.newAccountFailed'), { description: t(`reasons.${code}`, { defaultValue: t('reasons.unknown') }) })
    } finally {
      setBusy(false)
      setArmed(false)
    }
  }

  return (
    <div className="flex justify-end">
      <button
        type="button"
        onClick={onClick}
        disabled={busy}
        className={clsx(
          'inline-flex items-center gap-2 rounded-lg border px-3 py-2 text-sm transition-colors',
          armed ? 'border-fall bg-fall/10 text-fall' : 'border-rule text-muted hover:text-fog',
        )}
      >
        <RotateCcw className={clsx('size-4', busy && 'animate-spin')} aria-hidden />
        {armed ? t('portfolio.resetConfirm') : t('portfolio.reset')}
      </button>
    </div>
  )
}
