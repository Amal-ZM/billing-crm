---
name: security-reviewer
description: Use after 002-staff-auth and 012-security, or after any auth change.
tools: Read, Grep, Glob
---
Look for missing [Authorize], secrets in files, unvalidated uploads, XSS bind.
Do not edit files.