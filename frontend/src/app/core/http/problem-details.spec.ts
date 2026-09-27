import { HttpErrorResponse } from '@angular/common/http';
import { FormArray, FormControl, FormGroup } from '@angular/forms';
import { describe, expect, it } from 'vitest';
import { applyProblemToForm, clearServerErrors, serverMessage } from './problem-details';

function form() {
  return new FormGroup({
    name: new FormControl('Acme'),
    email: new FormControl('a@example.com'),
    phone: new FormControl('0500000000'),
  });
}

describe('applyProblemToForm', () => {
  it('puts each 400 field error on its control, keeps the typed values and returns no top-level message', () => {
    const f = form();
    const error = new HttpErrorResponse({
      status: 400,
      error: { errors: { email: ['Email must be a valid email address.'], phone: ['Phone is required.'] } },
    });

    const top = applyProblemToForm(f, error);

    expect(top).toBeNull();
    expect(serverMessage(f.controls['email'])).toBe('Email must be a valid email address.');
    expect(serverMessage(f.controls['phone'])).toBe('Phone is required.');
    expect(f.controls['email'].touched).toBe(true);
    expect(f.getRawValue()).toEqual({ name: 'Acme', email: 'a@example.com', phone: '0500000000' });
  });

  it('returns errors for fields the form does not have as a top-level message', () => {
    const f = form();
    const error = new HttpErrorResponse({ status: 400, error: { errors: { vatNumber: ['VAT number is too long.'] } } });

    expect(applyProblemToForm(f, error)).toBe('VAT number is too long.');
  });

  it('puts a 409 on the email control using the API message', () => {
    const f = form();
    const error = new HttpErrorResponse({
      status: 409,
      error: { title: 'Email already in use', detail: 'Another customer already uses this email address.' },
    });

    const top = applyProblemToForm(f, error);

    expect(top).toBeNull();
    expect(serverMessage(f.controls['email'])).toBe('Another customer already uses this email address.');
    expect(f.controls['email'].value).toBe('a@example.com');
  });

  it('clears a server message once the person edits the control', () => {
    const f = form();
    applyProblemToForm(f, new HttpErrorResponse({ status: 409, error: { detail: 'Already used.' } }));
    expect(serverMessage(f.controls['email'])).toBe('Already used.');

    f.controls['email'].setValue('other@example.com');

    expect(serverMessage(f.controls['email'])).toBeNull();
  });

  it('falls back to a generic 409 message when the body has no detail', () => {
    const f = form();

    applyProblemToForm(f, new HttpErrorResponse({ status: 409 }));

    expect(serverMessage(f.controls['email'])).toBe('This value is already in use.');
  });

  it('puts a 409 on the configured conflict field, for products the sku control', () => {
    const f = new FormGroup({ name: new FormControl('Consulting'), sku: new FormControl('CNS-001') });
    const error = new HttpErrorResponse({
      status: 409,
      error: { title: 'SKU already in use', detail: 'Another product already uses this SKU.' },
    });

    const top = applyProblemToForm(f, error, { conflictField: 'sku', subject: 'product' });

    expect(top).toBeNull();
    expect(serverMessage(f.controls['sku'])).toBe('Another product already uses this SKU.');
    expect(f.controls['sku'].value).toBe('CNS-001');
  });

  it('returns the 409 message at the top when the configured conflict field does not exist', () => {
    const f = new FormGroup({ name: new FormControl('Consulting') });
    const error = new HttpErrorResponse({ status: 409, error: { detail: 'Another product already uses this SKU.' } });

    expect(applyProblemToForm(f, error, { conflictField: 'sku' })).toBe('Another product already uses this SKU.');
  });

  it('names the subject in the fallback 400 message', () => {
    const f = new FormGroup({ name: new FormControl('Consulting') });
    const error = new HttpErrorResponse({ status: 400, error: { errors: { nothing: [] } } });

    expect(applyProblemToForm(f, error)).toBe('The customer could not be saved. Check the details and try again.');
    expect(applyProblemToForm(f, error, { subject: 'product' })).toBe('The product could not be saved. Check the details and try again.');
  });

  describe('array paths', () => {
    function invoiceForm() {
      return new FormGroup({
        customerId: new FormControl('c1'),
        dueDate: new FormControl('2030-01-01'),
        lines: new FormArray([
          new FormGroup({ productId: new FormControl('p1'), quantity: new FormControl('2') }),
          new FormGroup({ productId: new FormControl('p2'), quantity: new FormControl('0') }),
        ]),
      });
    }

    const lineControl = (f: FormGroup, index: number, name: string) => ((f.controls['lines'] as FormArray).at(index) as FormGroup).controls[name];

    it('puts a lines[i].field message on that line\'s control, keeps every value and returns no top message', () => {
      const f = invoiceForm();
      const error = new HttpErrorResponse({
        status: 400,
        error: {
          errors: {
            'lines[1].quantity': ['Quantity must be a number greater than zero with at most two decimal places.'],
            'lines[0].productId': ['This product is inactive or does not exist. Choose an active product.'],
            dueDate: ['Due date cannot be in the past.'],
          },
        },
      });

      const top = applyProblemToForm(f, error, { subject: 'invoice' });

      expect(top).toBeNull();
      expect(serverMessage(lineControl(f, 1, 'quantity'))).toBe('Quantity must be a number greater than zero with at most two decimal places.');
      expect(serverMessage(lineControl(f, 0, 'productId'))).toBe('This product is inactive or does not exist. Choose an active product.');
      expect(serverMessage(f.controls['dueDate'])).toBe('Due date cannot be in the past.');
      expect(lineControl(f, 1, 'quantity').touched).toBe(true);
      expect(lineControl(f, 1, 'quantity').value).toBe('0');
      expect(lineControl(f, 0, 'quantity').errors).toBeNull();
    });

    it('a server message on a line control disappears when the person edits it', () => {
      const f = invoiceForm();
      applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { 'lines[1].quantity': ['Bad.'] } } }));
      expect(serverMessage(lineControl(f, 1, 'quantity'))).toBe('Bad.');

      lineControl(f, 1, 'quantity').setValue('3');

      expect(serverMessage(lineControl(f, 1, 'quantity'))).toBeNull();
    });

    it('puts the message of a key that names the whole array (lines) on the top when the array control has no place for it', () => {
      const f = invoiceForm();

      const top = applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { lines: ['A Draft needs at least one line.'] } } }));

      expect(serverMessage(f.controls['lines'])).toBe('A Draft needs at least one line.');
      expect(top).toBeNull();
    });

    it('returns a message for a path that does not resolve (a line that is not on the screen) instead of throwing', () => {
      const f = invoiceForm();

      const top = applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { 'lines[7].quantity': ['Quantity is required.'] } } }));

      expect(top).toBe('Quantity is required.');
    });

    it('never throws on odd bracket keys', () => {
      const f = invoiceForm();
      const odd = ['[', ']', '[0]', 'lines[', 'lines[]', 'lines[0]', 'lines[0].', '.lines', 'lines[abc].quantity', '[0].[1]'];

      for (const key of odd) {
        expect(() => applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { [key]: ['x'] } } }))).not.toThrow();
      }
    });

    it('a negative or non-numeric index names no line and its message goes to the top', () => {
      const f = invoiceForm();

      const top = applyProblemToForm(
        f,
        new HttpErrorResponse({ status: 400, error: { errors: { 'lines[-1].quantity': ['Negative.'], 'lines[x].quantity': ['Text.'] } } }),
      );

      expect(top).toBe('Negative. Text.');
      expect(serverMessage(lineControl(f, 0, 'quantity'))).toBeNull();
      expect(serverMessage(lineControl(f, 1, 'quantity'))).toBeNull();
    });

    it('clearServerErrors removes every server message, in lines too, and keeps a real validation error', () => {
      const f = invoiceForm();
      lineControl(f, 1, 'quantity').setValidators((control) => (control.value === '0' ? { quantity: 'zero' } : null));
      applyProblemToForm(
        f,
        new HttpErrorResponse({
          status: 400,
          error: { errors: { customerId: ['Choose an active customer.'], 'lines[0].productId': ['Inactive.'], 'lines[1].quantity': ['Bad.'] } },
        }),
      );
      expect(f.invalid).toBe(true);

      clearServerErrors(f);

      expect(serverMessage(f.controls['customerId'])).toBeNull();
      expect(serverMessage(lineControl(f, 0, 'productId'))).toBeNull();
      expect(serverMessage(lineControl(f, 1, 'quantity'))).toBeNull();
      expect(lineControl(f, 0, 'productId').valid).toBe(true);
      expect(lineControl(f, 1, 'quantity').errors).toEqual({ quantity: 'zero' });
    });

    it('keys without brackets behave exactly as before', () => {
      const f = invoiceForm();

      applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { customerId: ['Choose an active customer.'] } } }));

      expect(serverMessage(f.controls['customerId'])).toBe('Choose an active customer.');
    });
  });

  it('describes a network failure as unreachable', () => {
    expect(applyProblemToForm(form(), new HttpErrorResponse({ status: 0 }))).toContain('Cannot reach the service');
  });

  it('uses the detail of other problems, or a generic message', () => {
    expect(applyProblemToForm(form(), new HttpErrorResponse({ status: 500, error: { detail: 'Boom detail.' } }))).toBe('Boom detail.');
    expect(applyProblemToForm(form(), new HttpErrorResponse({ status: 500, error: '<html>oops</html>' }))).toBe('Something went wrong. Try again.');
  });

  it('never throws on a malformed body or a non-HTTP error', () => {
    const f = form();

    expect(() => applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: 'not an object' } }))).not.toThrow();
    expect(() => applyProblemToForm(f, new HttpErrorResponse({ status: 400, error: { errors: { email: [42] } } }))).not.toThrow();
    expect(applyProblemToForm(f, new Error('plain'))).toBe('Something went wrong. Try again.');
  });
});
