# Linux SecureStorage security model

## What this package does and does not protect

Secrets are entrusted to the user's Secret Service provider. Its configuration
determines at-rest encryption, keyring unlock policy, prompting, and persistence.
The package does not generate its own master key, invent a cryptographic storage
format, invoke command-line secret tools, or fall back to local files.

An application attribute is a **namespace, not authorization**. The Secret Service
specification permits providers to make unlocked secrets available to other
clients in the same user session. This plugin does not isolate secrets from
malware running as that user, a compromised session bus/provider, a debugger, or
root. Choose a different architecture if that isolation is required.

Application IDs, keys, schema names, and labels are searchable metadata, not
encrypted secrets. Do not put credentials or sensitive personal data in them.
Labels contain only the application ID; values are sent through libsecret's secret
API. The D-Bus service and its transport/session negotiation remain trusted.
Strings passed to and returned from `ISecureStorage` necessarily exist in managed
memory and cannot be reliably wiped by this API.

## Review of the extracted implementation

Reviewed the source before
[dotnet/maui-labs#717](https://github.com/dotnet/maui-labs/pull/717), at commit
`435cd7a1ca6d0a1fde9cf10f445612d635688d3b`.

| Finding | Upstream evidence | Treatment here |
|---------|-------------------|----------------|
| Medium: attacker-controlled file/key substitution under a writable storage directory | `LinuxSecureStorage.cs:320-350,372-379`; trusted an existing raw key, followed file paths, and ignored permission failures | Removed the file backend entirely; no filesystem storage or automatic downgrade |
| Medium: bulk removal silently retained locked items or swallowed native failures | `LinuxSecureStorage.cs:244-255`; password-clear API removes only unlocked matches and errors were ignored | Search all matching items, attempt unlock, reject still-locked matches before deletion, check every delete, then search again to verify absence |
| Lookup errors were indistinguishable from a missing item | `LinuxSecureStorage.cs:262-279` | Check service/search errors, locked state, and secret availability; return null only for no matches |
| Ambiguous inferred application identity | `LinuxSecureStorage.cs:282-315` | Require an explicit application ID for direct construction and DI |
| Embedded NULs and invalid Unicode could be truncated/replaced during native marshalling | Native string inputs were not validated | Reject these inputs before any native work |
| Disposal could race native schema usage | `LinuxSecureStorage.cs:417-422` | Serialize disposal with operations and own native allocations through SafeHandle |
| Native schema ABI and manual string lifetimes were fragile | `LibSecretInterop.cs:13-61,147-175` | Use `secret_schema_newv` rather than reproducing the native schema struct, GLib-owned attribute strings, and deterministic native cleanup |

The file attack requires a directory another local user can control; it is not a
claim that a normally private home directory is inherently writable by others.
Even without that attack, storing a raw encryption key beside the encrypted file
does not protect both against the same filesystem or backup compromise.

Additional hardening includes checking native boolean results even without a
`GError`, refusing ambiguous duplicate lookup matches, and excluding raw native
error text from exceptions because it can contain searchable attributes.
Each operation creates a fresh service proxy so cached item lock/secret state
does not depend on a running GLib main loop between calls.

## Failure, concurrency, and deletion guarantees

`Remove` and `RemoveAll` search across all collections for this schema and
application. They do not use libsecret's password-clear API. Failed unlocking is
not assumed to be a failed search: returned items are explicitly checked for
locked state. No deletion is attempted if any initial matching item remains
locked.

Deletion is not a transaction. A provider error partway through a multi-item
delete can leave some entries deleted and others present; the operation throws.
Other instances/processes are not globally locked. A final search detects entries
still present at that point, but cannot prevent another writer from adding a
secret afterward. Stop credential writers before sign-out/cleanup and handle
exceptions instead of assuming all credentials were erased.

Deleting keyring entries does not revoke server-side tokens, securely wipe
backups, remove legacy files, or erase secrets already copied into memory. Revoke
tokens separately where required.

`GetAsync` and `SetAsync` use worker threads. Synchronous removal may block on
provider interaction. The API has no cancellation-token contract; there is no
promise of a bounded response time from an unresponsive provider or user prompt.

## Security regression coverage

Unit tests cover application scoping, validation, failure propagation, DI
replacement, serialization, and disposal. Opt-in tests against GNOME Keyring
exercise native UTF-8 marshalling, empty values, updates, persistence across
instances, scoped deletion, and locked-collection refusal. A separate no-service
test checks that unavailable D-Bus does not produce successful operations or
missing-credential results.
The lock test runs in its own disposable session and verifies both that locked
entries remain and that unlocked matches in another collection are not partially
deleted when a locked match prevents bulk removal.

## References

- [Secret Service security considerations](https://specifications.freedesktop.org/secret-service/latest/ch03.html)
- [Secret Service locking](https://specifications.freedesktop.org/secret-service/latest/locking.html)
- [libsecret search implementation and unlock semantics](https://github.com/GNOME/libsecret/blob/0ee86df896582231417b13faaa5a160aec4a1db1/libsecret/secret-methods.c)
- [Original LinuxSecureStorage implementation](https://github.com/dotnet/maui-labs/blob/435cd7a1ca6d0a1fde9cf10f445612d635688d3b/platforms/Linux.Gtk4/src/Linux.Gtk4.Essentials/Storage/LinuxSecureStorage.cs)
