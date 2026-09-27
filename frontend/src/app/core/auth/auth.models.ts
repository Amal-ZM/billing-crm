export type Role = 'Admin' | 'Agent';

/** What the API returns at sign-in; kept in the browser for display, expiry and the Bearer header. */
export interface Session {
  accessToken: string;
  expiresAtUtc: string;
  email: string;
  role: Role;
}
