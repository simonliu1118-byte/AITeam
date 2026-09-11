# AITeam versioning

AITeam uses a simple three-part version number:

`MAJOR.MINOR.PATCH`

## PATCH

Increment the third number for small fixes, packaging changes, UI polish, CI fixes, or test builds that do not introduce a new functional capability.

Examples:

- `0.2.1`
- `0.2.2`
- `0.2.3`

## MINOR

Increment the second number when a real feature or meaningful functional capability is added or changed.

Examples:

- `0.3.0`
- `0.4.0`

When MINOR increments, PATCH resets to `0`.

## MAJOR

Increment the first number when a major product capability or major stable milestone goes live.

Example:

- `1.0.0`

When MAJOR increments, MINOR and PATCH reset to `0`.

## Delivery rule

Each completed version must have one matching `VERSION` value, Git tag, GitHub Release, and Windows portable ZIP.

The Windows ZIP is named:

`AITeam-<version>-win-x64.zip`
