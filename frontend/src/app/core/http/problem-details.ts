import { HttpErrorResponse } from '@angular/common/http';
import { AbstractControl, FormArray, FormGroup } from '@angular/forms';

/** The error key used on a form control for a message that came from the API. */
export const SERVER_ERROR_KEY = 'server';

/** The API's message for a control, if the last save was refused because of it. */
export function serverMessage(control: AbstractControl | null): string | null {
  const message = control?.errors?.[SERVER_ERROR_KEY];
  return typeof message === 'string' ? message : null;
}

/**
 * Removes the API's messages from a control and everything inside it (a form array's lines too), so the next save is judged on
 * what is in the form now. Without this a message from the last refusal keeps the form invalid, and a field the person never
 * touched (for example an existing line's `id`) could never be cleared by editing it.
 */
export function clearServerErrors(control: AbstractControl): void {
  if (control instanceof FormGroup || control instanceof FormArray) {
    for (const child of Object.values(control.controls)) {
      clearServerErrors(child);
    }
  }

  if (control.errors && SERVER_ERROR_KEY in control.errors) {
    // Re-running the control's own validators drops the server message and keeps any real problem. It is NOT onlySelf: the
    // groups and arrays above it must recompute their status too, or they would stay invalid after the message is gone.
    control.updateValueAndValidity({ emitEvent: false });
  }
}

export interface ProblemFormOptions {
  /** The control a 409 belongs to. Customers: `email` (the default). Products: `sku`. */
  conflictField?: string;
  /** What is being saved, for the fallback message ("customer", "product"). */
  subject?: string;
}

/**
 * Finds the control a server error key belongs to. A plain key (`email`) is a control of the form; a key with brackets
 * (`lines[0].quantity`) is a path through a form array (`lines`, then item 0, then its `quantity` control). Anything that
 * does not resolve is null, never an exception.
 */
function controlFor(form: FormGroup, key: string): AbstractControl | null {
  if (!key.includes('[')) {
    return form.get(key);
  }

  // Every bracket must hold a plain non-negative whole number (`lines[0]`); `lines[-1]` or `lines[x]` names nothing.
  if ([...key.matchAll(/\[([^\]]*)\]/g)].some((match) => !/^\d+$/.test(match[1]))) {
    return null;
  }

  const path = (key.match(/[^.[\]]+/g) ?? []).map((part) => (/^\d+$/.test(part) ? Number(part) : part));
  return path.length > 0 ? form.get(path) : null;
}

/**
 * Turns a refused save into form errors and, for anything that belongs to no field, one message to show
 * at the top of the form.
 *
 * - 400 with `errors`: each key is a camelCase field name (or an array path such as `lines[0].quantity`); the message goes on
 *   that control.
 * - 409: a duplicate value; the API's message goes on the `conflictField` control (`email` by default).
 * - Anything else (network down, 404, 5xx, a malformed body): a top-level message.
 *
 * The values the person typed are never touched. A server message on a control disappears by itself the
 * next time they edit that control, because validation re-runs.
 */
export function applyProblemToForm(form: FormGroup, error: unknown, options: ProblemFormOptions = {}): string | null {
  const conflictField = options.conflictField ?? 'email';
  const subject = options.subject ?? 'customer';

  if (!(error instanceof HttpErrorResponse)) {
    return 'Something went wrong. Try again.';
  }

  const body = error.error;

  if (error.status === 400 && body?.errors && typeof body.errors === 'object') {
    const unplaced: string[] = [];
    let placed = 0;
    for (const [field, messages] of Object.entries<unknown>(body.errors)) {
      const message = Array.isArray(messages) ? messages.find((m) => typeof m === 'string') : undefined;
      if (typeof message !== 'string') {
        continue;
      }
      const control = controlFor(form, field);
      if (control) {
        control.setErrors({ [SERVER_ERROR_KEY]: message });
        control.markAsTouched();
        placed++;
      } else {
        unplaced.push(message);
      }
    }
    if (unplaced.length > 0) {
      return unplaced.join(' ');
    }
    // A refusal that named nothing we can show must still tell the person something went wrong.
    return placed > 0 ? null : `The ${subject} could not be saved. Check the details and try again.`;
  }

  if (error.status === 409) {
    const message = typeof body?.detail === 'string' ? body.detail : 'This value is already in use.';
    const control = form.get(conflictField);
    if (control) {
      control.setErrors({ [SERVER_ERROR_KEY]: message });
      control.markAsTouched();
      return null;
    }
    return message;
  }

  if (error.status === 0) {
    return 'Cannot reach the service. Check that the API is running and try again.';
  }

  return typeof body?.detail === 'string' ? body.detail : 'Something went wrong. Try again.';
}

/**
 * The most useful single message in a failed response, for screens that are not forms: the first field
 * error of a 400, otherwise the ProblemDetails `detail`. Null when the API said nothing usable.
 */
export function problemDetail(error: unknown): string | null {
  if (!(error instanceof HttpErrorResponse)) {
    return null;
  }

  const body = error.error;
  if (body?.errors && typeof body.errors === 'object') {
    for (const messages of Object.values<unknown>(body.errors)) {
      const first = Array.isArray(messages) ? messages.find((m) => typeof m === 'string') : undefined;
      if (typeof first === 'string') {
        return first;
      }
    }
  }
  return typeof body?.detail === 'string' ? body.detail : null;
}
