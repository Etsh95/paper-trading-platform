import { useSearchParams } from 'react-router'
import { AccountSummary } from '../components/AccountSummary'
import { MarketsTable } from '../components/MarketsTable'
import { OpenOrders } from '../components/OpenOrders'
import { OrderTicket } from '../components/OrderTicket'
import { useInstruments } from '../state/queries'

export function TradePage() {
  const [params, setParams] = useSearchParams()
  const { data: instruments } = useInstruments()
  const requested = params.get('symbol')
  const symbol = instruments?.some((i) => i.symbol === requested) ? requested! : (instruments?.[0]?.symbol ?? 'BTCUSDT')

  // The selected market lives in the URL, so it survives reloads and can be shared.
  const select = (s: string) => setParams({ symbol: s }, { replace: true })

  return (
    <div className="grid items-start gap-4 lg:grid-cols-[minmax(0,1fr)_23rem] lg:gap-6">
      <div className="divide-y divide-rule overflow-hidden rounded-xl border border-rule bg-deck">
        <AccountSummary />
        <MarketsTable selected={symbol} onSelect={select} />
        <OpenOrders />
      </div>
      <aside className="overflow-hidden rounded-xl border border-rule bg-deck lg:sticky lg:top-20">
        <OrderTicket symbol={symbol} onSymbolChange={select} />
      </aside>
    </div>
  )
}
