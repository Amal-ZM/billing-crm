/**
 * The Home dashboard's five figures (spec 011): computed fresh by the API on every read, never cached here either. `unpaidCount`
 * includes every invoice counted in `overdueCount` (Overdue is a subset of Unpaid, never a separate stored status).
 */
export interface DashboardSummary {
  draftCount: number;
  unpaidCount: number;
  overdueCount: number;
  paidThisMonthCount: number;
  /** SAR, two decimals: the sum of every Sent invoice's own remaining balance. */
  unpaidAmount: number;
}
