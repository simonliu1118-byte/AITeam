# AITeam versioning

AITeam uses semantic-style three-part versions with the following project policy:

- Patch / test / small fixes: increment the third number, e.g. `0.2.1` -> `0.2.2`.
- Functional change: increment the second number and reset patch to zero, e.g. `0.2.x` -> `0.3.0`.
- Major feature milestone / production-level major release: increment the first number, e.g. `0.x.x` -> `1.0.0`.

A completed release is version bump -> commit -> merge main -> tag -> GitHub Release.
