/**
 * Checks an amount or quantity **as text**, so nothing is ever converted through a floating-point number before it has been
 * judged: a third decimal place cannot hide behind rounding. Used by the invoice form for quantities and the discount, and by the payment form for the amount; the
 * product form has its own copy of similar logic and is deliberately not refactored to use this.
 */
export type DecimalTextProblem = 'required' | 'notNumber' | 'negative' | 'zero' | 'decimals' | 'tooBig';

export interface DecimalTextOptions {
  /** The largest allowed value, for example 999999.99. */
  max: number;
  /** Whether 0 is acceptable (a price may be 0, a quantity may not). */
  allowZero: boolean;
}

/** Plain ASCII digits, an optional leading minus and an optional decimal part; no thousands separators, no comma decimal. */
const NUMBER_SHAPE = /^-?\d+(\.\d+)?$/;

/**
 * The first problem with the typed text, or null when it is a valid amount: a number from 0 (or above 0) up to `max` with at most
 * two decimal places. `1.500` is fine (same value as `1.5`); `1.005` is not.
 */
export function decimalTextProblem(text: string | null | undefined, options: DecimalTextOptions): DecimalTextProblem | null {
  const value = (text ?? '').trim();
  if (value.length === 0) {
    return 'required';
  }
  if (!NUMBER_SHAPE.test(value)) {
    return 'notNumber';
  }

  const unsigned = value.startsWith('-') ? value.slice(1) : value;
  const [whole, fraction = ''] = unsigned.split('.');
  const isZero = /^0*$/.test(whole) && /^0*$/.test(fraction);

  if (value.startsWith('-') && !isZero) {
    return 'negative';
  }
  // Anything beyond two decimals must be zeros only.
  if (fraction.length > 2 && !/^0*$/.test(fraction.slice(2))) {
    return 'decimals';
  }
  if (isZero && !options.allowZero) {
    return 'zero';
  }

  // The value and the limit are parsed the same way, and the invoice limits (999,999.99 and 9,999,999,999,999.99) are far below
  // 2^53 halalas (about 9e13 riyals), where two-decimal values are still distinct doubles, so this comparison is exact too.
  // Only ASCII digits are accepted (Arabic-Indic digits are "not a number"), as the API reads numbers the same way.
  return Number(value) > options.max ? 'tooBig' : null;
}

/** The number to send, exactly as typed. Only call it after `decimalTextProblem` returned null. */
export function decimalTextToNumber(text: string): number {
  return Number(text.trim());
}

/** The largest amount the system accepts (the API refuses more); at most this many riyals stay exact in a browser number. */
const MAX_PAYMENT_AMOUNT = 9999999999999.99;

export const PAYMENT_AMOUNT_REQUIRED = 'Amount is required.';
export const PAYMENT_AMOUNT_MESSAGE = 'Amount must be a number greater than zero with at most two decimal places.';
export const PAYMENT_AMOUNT_TOO_BIG = 'Amount cannot be more than 9,999,999,999,999.99.';

/**
 * The message for a typed payment amount that cannot be sent, or null when it can: a number greater than zero with at most two
 * decimal places, judged as text like a quantity. Whether it is more than what remains needs the invoice's amounts, which only the
 * API works out, so that check is the server's and its message is shown on the amount field.
 */
export function paymentAmountProblem(text: string | null | undefined): string | null {
  const problem = decimalTextProblem(text, { max: MAX_PAYMENT_AMOUNT, allowZero: false });
  if (problem === null) {
    return null;
  }
  if (problem === 'required') {
    return PAYMENT_AMOUNT_REQUIRED;
  }
  return problem === 'tooBig' ? PAYMENT_AMOUNT_TOO_BIG : PAYMENT_AMOUNT_MESSAGE;
}
