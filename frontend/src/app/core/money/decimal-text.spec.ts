import { describe, expect, it } from 'vitest';
import {
  PAYMENT_AMOUNT_MESSAGE,
  PAYMENT_AMOUNT_REQUIRED,
  PAYMENT_AMOUNT_TOO_BIG,
  decimalTextProblem,
  decimalTextToNumber,
  paymentAmountProblem,
} from './decimal-text';

const quantity = { max: 999999.99, allowZero: false };
const price = { max: 999999999.99, allowZero: true };

describe('decimalTextProblem', () => {
  it.each(['1', '2', '1.5', '1.50', '1.500', '0.01', '10', '999999.99', '  2  ', '1.000000'])('accepts the quantity "%s"', (text) => {
    expect(decimalTextProblem(text, quantity)).toBeNull();
  });

  it.each([
    ['', 'required'],
    ['   ', 'required'],
    ['abc', 'notNumber'],
    ['1,5', 'notNumber'],
    ['.5', 'notNumber'],
    ['5.', 'notNumber'],
    ['1e2', 'notNumber'],
    ['+1', 'notNumber'],
    ['1 2', 'notNumber'],
    ['١٢', 'notNumber'],
    ['-1', 'negative'],
    ['-0.01', 'negative'],
    ['0', 'zero'],
    ['0.00', 'zero'],
    ['-0', 'zero'],
    ['1.005', 'decimals'],
    ['0.001', 'decimals'],
    ['1.5001', 'decimals'],
    ['1000000', 'tooBig'],
    ['999999.995', 'decimals'],
    ['1000000.00', 'tooBig'],
  ])('refuses the quantity "%s" as %s', (text, problem) => {
    expect(decimalTextProblem(text, quantity)).toBe(problem);
  });

  it('treats null and undefined as required', () => {
    expect(decimalTextProblem(null, quantity)).toBe('required');
    expect(decimalTextProblem(undefined, quantity)).toBe('required');
  });

  it('accepts zero when the option allows it (a free item price)', () => {
    expect(decimalTextProblem('0', price)).toBeNull();
    expect(decimalTextProblem('0.00', price)).toBeNull();
    expect(decimalTextProblem('999999999.99', price)).toBeNull();
    expect(decimalTextProblem('1000000000', price)).toBe('tooBig');
    expect(decimalTextProblem('-1', price)).toBe('negative');
  });

  it('judges the digits, not a rounded number', () => {
    // 0.1 + 0.2 style values must not sneak through: the text is checked, never converted first.
    expect(decimalTextProblem('0.30000000000000004', quantity)).toBe('decimals');
    expect(decimalTextProblem('100.10', quantity)).toBeNull();
  });
});

describe('decimalTextToNumber', () => {
  it('returns the number exactly as typed', () => {
    expect(decimalTextToNumber('2')).toBe(2);
    expect(decimalTextToNumber(' 1.50 ')).toBe(1.5);
    expect(decimalTextToNumber('999999.99')).toBe(999999.99);
    expect(decimalTextToNumber('0.01')).toBe(0.01);
  });
});

describe('paymentAmountProblem', () => {
  it.each(['1', '0.01', '100.10', '100.100', '  230  ', '9999999999999.99'])('accepts the payment amount "%s"', (text) => {
    expect(paymentAmountProblem(text)).toBeNull();
  });

  it('asks for an amount when it is empty', () => {
    expect(paymentAmountProblem('')).toBe(PAYMENT_AMOUNT_REQUIRED);
    expect(paymentAmountProblem('   ')).toBe(PAYMENT_AMOUNT_REQUIRED);
  });

  it.each(['abc', '1,5', '.5', '-1', '-0.01', '0', '0.00', '10.005', '0.001', '1e2', '١٢'])(
    'refuses "%s" with the amount rule (greater than zero, at most two decimals)',
    (text) => {
      expect(paymentAmountProblem(text)).toBe(PAYMENT_AMOUNT_MESSAGE);
    },
  );

  it('refuses an amount above the largest the system accepts, in its own words', () => {
    expect(paymentAmountProblem('10000000000000')).toBe(PAYMENT_AMOUNT_TOO_BIG);
  });

  it('judges the text, not a rounded number', () => {
    expect(paymentAmountProblem('0.30000000000000004')).toBe(PAYMENT_AMOUNT_MESSAGE);
  });
});
