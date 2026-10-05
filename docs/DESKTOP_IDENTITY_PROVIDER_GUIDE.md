# Desktop Identity Provider Architecture Guide

> Status: shared reference for future desktop application design. This is an engineering guide, not a replacement for repository or project rules.

## 1. Purpose

Desktop applications that require accounts, authentication or authorization should avoid hard-coding credential verification, role lookup, account storage or Cloud identity assumptions directly into UI forms, business workflows or repositories.

The preferred design is to put identity acquisition behind a stable provider boundary such as `IIdentityProvider`, normalize the result into an application principal, and let the application apply its own authorization policy.

This pattern is intended to keep future desktop products compatible with more than one authority model without rewriting security-sensitive flows.

Typical product modes may include:

- local-only identity;
- application-owned Cloud identity;
- shared / external identity authority.

A product does not need to support all three. The important rule is that business code should not assume one specific credential store unless the product explicitly guarantees that architecture.

## 2. Public repository privacy boundary

AITeam is a Public repository. This guide therefore uses only generic contracts and synthetic examples.

Do not place any of the following in this document, examples, tests or derived prompts:

- real Workspace / tenant / organization identifiers;
- real Employee / User IDs, names, Email addresses or account assignments;
- production or private Cloud endpoints;
- API keys, OAuth tokens, cookies, OTPs, recovery codes, Device Tokens or secrets;
- provider resource IDs, binding IDs or production deployment topology;
- customer, invoice, accounting or other runtime business data;
- credential verifiers derived from real passwords;
- content copied from another repository when that content is not already safe for the Public repository.

Use placeholders such as `<workspace-id>`, `<user-id>`, `<application-id>` and synthetic values only.

## 3. Separate authentication from authorization

Authentication answers:

```text
Who is the operator?
Was the credential accepted by the active identity authority?
What current identity metadata is trusted?
```

Authorization answers:

```text
May this principal perform this application action now?
```

Do not merge these concerns into one UI-specific password check.

Preferred flow:

```text
UI / command
    ↓
Identity Provider
    ↓
Authenticated Principal
    ↓
Application Authorization Policy
    ↓
Business Operation
```

The identity provider proves identity and returns normalized current authority. The application remains responsible for product-specific actions and permissions.

## 4. Minimal provider boundary

A desktop application may define a small provider interface similar to:

```csharp
public interface IIdentityProvider
{
    IdentityProviderKind Kind { get; }

    Task<AuthenticationResult> AuthenticateAsync(
        AuthenticationRequest request,
        CancellationToken cancellationToken);

    Task<AuthorityRefreshResult> RefreshAuthorityAsync(
        AuthorityRefreshRequest request,
        CancellationToken cancellationToken);
}
```

The exact method names are product-specific. The architectural requirement is more important than the exact interface shape.

Do not add a large generic plugin framework unless the product actually needs one. A small adapter boundary is usually enough.

Typical implementations may look like:

```text
LocalIdentityProvider
BuiltInCloudIdentityProvider
SharedIdentityProvider
```

The UI and business layer should depend on the interface or an application service that wraps it, not on a concrete password database.

## 5. Normalized principal

Provider-specific schemas should be converted to an application-facing principal before business logic sees them.

Example:

```csharp
public sealed record AppPrincipal(
    string SubjectId,
    string DisplayName,
    bool Enabled,
    string Role,
    long AuthorityRevision);
```

The application may use an enum or stronger type for `Role` when its own role model is fixed.

The principal should contain only information required by the application. Do not expose provider secrets, password hashes, OTP material or unnecessary identity database fields.

A shared identity service may internally use Groups, application grants or other policy structures. The desktop application should normally consume the resulting principal / app role rather than reconstructing the provider's internal authorization model.

## 6. Provider selection must be centralized

Do not scatter logic such as this across forms and services:

```csharp
if (settings.UseSharedIdentity) { ... }
else if (settings.UseCloud) { ... }
else { ... }
```

Instead select the active provider once at a composition boundary:

```text
Application startup / workspace bootstrap
        ↓
IdentityProviderFactory
        ↓
active IIdentityProvider
        ↓
all authentication consumers
```

Changing identity mode should therefore change the provider binding, not require rewriting every security-sensitive workflow.

## 7. Never authenticate directly in UI forms

A form should collect credentials and call an authentication service. It should not:

- query an account table directly;
- decrypt password material directly;
- decide Cloud vs Local credential authority itself;
- infer privileges from UI controls;
- cache a successful administrator object indefinitely;
- duplicate role checks already defined in an authorization service.

Bad pattern:

```text
Button click
→ query SQLite users table
→ compare password
→ if role == "ADMIN" do operation
```

Preferred pattern:

```text
Button click
→ authentication service
→ current principal
→ authorization policy
→ operation
```

## 8. Execution-time authentication for sensitive operations

Desktop products that do not maintain a persistent signed-in session should verify the operator at the time of a sensitive action.

Examples include:

- administrative changes;
- account or permission changes;
- destructive data operations;
- device enrollment / revoke;
- high-impact business actions.

A previous successful authentication in another window must not silently authorize later unrelated operations unless the product explicitly implements and secures a session model.

## 9. Online authority and offline fallback

If a Cloud-capable desktop product supports offline operation, define one authority and one fallback path.

Recommended model:

```text
Online
→ contact current Cloud / shared authority
→ authenticate or refresh current authority
→ update protected local cache

Cloud genuinely unavailable
→ use the last trusted protected offline cache
```

Do not use this pattern:

```text
Cloud authentication failed
→ silently fall back to an old Local account store
```

That creates dual authority and can re-enable accounts or roles that should no longer be valid.

Offline fallback should remain the same identity authority in cached form, not a second independent account system.

## 10. Offline credential cache

When offline password verification is a product requirement:

- store only what is needed for offline verification;
- protect sensitive local material with the operating system's secure storage boundary, such as Windows DPAPI or an approved equivalent;
- bind cached authority to revision / credential-version metadata when available;
- refresh or invalidate it after successful online authority refresh;
- never log plaintext credentials or offline verifier material.

If the central identity contract does not allow exporting its credential verifier, do not bypass the contract by reading the central database directly.

A reviewed design may create a dedicated local offline verifier from the user-supplied password only after successful online authentication, then protect that verifier locally. The exact mechanism is product-specific and must be threat-modeled before implementation.

## 11. Account-management ownership

The desktop application should have exactly one account-management owner for the active identity mode.

Examples:

```text
Local provider
→ desktop application owns account management

Built-in Cloud provider
→ application Cloud owns account management

Shared Identity provider
→ shared account center owns account management
```

When account management belongs elsewhere, prefer hiding the desktop account-management feature instead of leaving a partially functional duplicate UI.

Do not let two authorities modify the same account set concurrently.

## 12. Application access vs application permissions

Shared identity systems may answer whether a user can enter an application at all. That is different from product-specific authorization.

Conceptually:

```text
Shared Identity
→ "May this user enter Application X?"

Application
→ "May this authenticated principal perform Action Y?"
```

Do not copy a shared identity provider's entire group / grant model into the desktop product unless the product actually needs those concepts.

The desktop application should prefer a small normalized contract such as:

```text
subject identity
account enabled state
application access result
application role / coarse authority
revision or credential version when required
```

## 13. Device identity is not user identity

For desktop Cloud applications, a trusted device credential and a human account are separate security principals.

A device token can prove:

```text
This installation / device is enrolled.
```

It must not automatically prove:

```text
The current operator is an administrator.
```

Likewise, a valid human credential does not automatically authorize an untrusted device if the product requires device enrollment.

Keep device lifecycle, user identity and business authorization as separate layers.

## 14. Role naming and mapping

Prefer one normalized application role model inside the desktop product.

If an external identity provider exposes different internal groups or labels, map them once at the adapter boundary rather than throughout the UI.

Do not compare provider display names in business code.

Bad:

```csharp
if (groups.Contains("Managers")) { ... }
```

Better:

```csharp
if (principal.Role == AppRole.Admin) { ... }
```

If the provider already returns the application's canonical role, no additional mapping is needed.

## 15. Error semantics

Authentication failure, authorization denial, network failure and provider failure are different states.

Do not collapse all failures into "wrong password".

At minimum distinguish:

```text
Invalid credentials
Account disabled
Application access denied
Insufficient application permission
Cloud / provider unavailable
Request timeout / result unknown
Local offline cache unavailable
```

This is important both for security and for deciding whether offline fallback is allowed.

## 16. UI guidance

The UI should reflect the active identity owner without exposing backend complexity.

Useful patterns include:

- a read-only label showing the current account source;
- hiding account-management commands when an external account center owns them;
- keeping security-sensitive re-authentication dialogs provider-neutral;
- showing an explicit offline state when cached authority is being used;
- never displaying or logging raw tokens, verifiers or secrets.

The UI must not become the authority source. Hidden or disabled controls are presentation rules; backend authorization must still validate the operation.

## 17. Testing requirements

A desktop identity abstraction is incomplete until tests cover authority boundaries.

At minimum test:

1. each supported provider authenticates a valid synthetic account;
2. invalid credentials are rejected;
3. disabled / revoked authority is rejected;
4. application role is normalized correctly;
5. business authorization does not query provider-specific storage directly;
6. switching provider mode does not revive another account store;
7. online authority refresh updates the local cache;
8. offline fallback occurs only after the authoritative provider is genuinely unavailable according to the product's policy;
9. reconnect restores current online authority;
10. account-management UI appears only when the active mode owns account management;
11. no plaintext passwords, tokens or real identifiers appear in logs, fixtures or test artifacts.

## 18. Anti-pattern checklist

Avoid the following designs:

- password verification duplicated in multiple forms;
- direct SQLite / D1 / database reads from UI authorization code;
- hard-coded `if cloud` / `if local` branches throughout business services;
- treating a device token as administrator identity;
- treating a previous form login as permanent authorization;
- two account stores simultaneously acting as authority;
- silently falling back from Cloud to an unrelated Local authority;
- copying external Identity database schemas into the desktop app;
- storing plaintext passwords for offline use;
- exporting central credential verifiers merely for convenience;
- assuming group display names are stable permission identifiers;
- using hidden buttons as the only authorization boundary.

## 19. Suggested implementation shape

A practical desktop application can stay small:

```text
UI
↓
AuthenticationService
↓
IIdentityProvider
├─ LocalIdentityProvider
├─ BuiltInCloudIdentityProvider
└─ SharedIdentityProvider

AuthenticationService
↓
AppPrincipal
↓
AuthorizationService
↓
Business Service
```

Optional offline support sits beside the provider boundary:

```text
IIdentityProvider
↓ successful online authority
ProtectedOfflineAuthorityCache
```

The offline cache is not another provider-owned account system. It is a protected snapshot / verifier used only under the product's explicit offline policy.

## 20. Adoption rule for future desktop projects

When a new desktop project introduces authentication or permissions, the design review should answer these questions before implementation:

- What is the single current identity authority?
- Can that authority change by product mode?
- Where is the provider boundary?
- What normalized principal does business code consume?
- Which permissions belong to shared identity, and which belong to the application?
- Who owns account management in each mode?
- Is offline authentication required?
- If yes, what is the protected cache and when may it be used?
- Are device identity and human identity separated?
- Can any UI or repository bypass the provider / authorization service?

If these answers are unclear, do not start by wiring password checks directly into forms. Define the identity boundary first.

## 21. Scope

This guide is intentionally provider-neutral and product-neutral. Individual repositories may impose stricter security, role, session, offline or deployment rules through their own governance and project documentation.

Target repository rules always take precedence over this reference guide.
