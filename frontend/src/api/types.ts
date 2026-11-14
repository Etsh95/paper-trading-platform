/**
 * Wire types. Every monetary or quantity value arrives as a decimal *string* (the API never sends
 * money as a JSON number), and stays a string or a Decimal on the client. Never parseFloat money.
 */
export type DecimalString = string

export type OrderSide = 'Buy' | 'Sell'
export type OrderType = 'Market' | 'Limit'
export type OrderStatus = 'Open' | 'Filled' | 'Cancelled' | 'Rejected'

export interface Instrument {
  symbol: string
  baseAsset: string
  quoteAsset: string
  tickSize: DecimalString
  stepSize: DecimalString
  minNotional: DecimalString
}

export interface Quote {
  symbol: string
  bid: DecimalString
  ask: DecimalString
  mid: DecimalString
  timestamp: string
}

export interface MarketStatus {
  source: string | null
  connected: boolean
  lastQuoteAt: string | null
}

export interface QuotesResponse {
  status: MarketStatus
  quotes: Quote[]
}

export interface TradingConfig {
  initialCash: DecimalString
  feeRate: DecimalString
  maxQuoteAgeSeconds: number
  maxOpenOrders: number
}

export interface Session {
  accountId: string
  accessToken: string
  expiresAt: string
  initialCash: DecimalString
}

export interface Order {
  id: string
  sequence: number
  symbol: string
  side: OrderSide
  type: OrderType
  quantity: DecimalString
  limitPrice: DecimalString | null
  status: OrderStatus
  filledQuantity: DecimalString
  fillPrice: DecimalString | null
  fee: DecimalString
  rejectReason: string | null
  createdAt: string
  updatedAt: string
}

export interface Trade {
  id: string
  orderId: string
  symbol: string
  side: OrderSide
  quantity: DecimalString
  price: DecimalString
  notional: DecimalString
  fee: DecimalString
  realizedPnl: DecimalString
  executedAt: string
}

export interface Position {
  symbol: string
  quantity: DecimalString
  reservedQuantity: DecimalString
  averageCost: DecimalString
  costBasis: DecimalString
  markPrice: DecimalString | null
  marketValue: DecimalString
  unrealizedPnl: DecimalString
  realizedPnl: DecimalString
}

export interface Portfolio {
  initialCash: DecimalString
  cash: DecimalString
  reservedCash: DecimalString
  availableCash: DecimalString
  positionsValue: DecimalString
  equity: DecimalString
  realizedPnl: DecimalString
  unrealizedPnl: DecimalString
  totalPnl: DecimalString
  totalFees: DecimalString
  positions: Position[]
  asOf: string
}

export interface PlaceOrderInput {
  symbol: string
  side: OrderSide
  type: OrderType
  quantity: DecimalString
  limitPrice?: DecimalString
}

export interface PlaceOrderResult {
  order: Order
  replayed: boolean
}
