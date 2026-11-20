# Platform.Maui.Linux.SecureStorage

A .NET 10 implementation of `Microsoft.Maui.Storage.ISecureStorage` for Linux,
using **libsecret and the freedesktop Secret Service** (for example, GNOME Keyring).
It does not depend on GTK or a MAUI workload.

This extracts the functionality removed by
[dotnet/maui-labs#717](https://github.com/dotnet/maui-labs/pull/717), without its
automatic encrypted-file fallback. A missing library, unavailable service, locked
item that cannot be unlocked, or failed operation is an error, not a fallback to
files or a successful no-op.

**This is desktop keyring storage, not per-application isolation from other
programs running as the same user.** Read [SECURITY.md](SECURITY.md) before choosing
it for sensitive data.

## Requirements

- .NET 10 or later on Linux.
- `libsecret-1.so.0`, `libglib-2.0.so.0`, and `libgobject-2.0.so.0`.
- A working D-Bus user session and a Secret Service provider with a default
  collection. A locked keyring may display an unlock prompt; cancellation is an
  error when a matching item cannot be accessed or removed.

On Debian/Ubuntu, install `libsecret-1-0` and a provider such as `gnome-keyring`.
Other distributions use different package names. Installing libsecret alone does
not start or unlock a keyring. Headless environments need an explicitly provisioned
service; do not disable security or introduce a local file fallback to bypass it.

## MAUI Linux backend registration

Reference the `Platform.Maui.Linux.SecureStorage` package in the **Linux** host.
For local development, use a project reference to
`src/Platform.Maui.Linux.SecureStorage/Platform.Maui.Linux.SecureStorage.csproj`.

Register it before building the MAUI application:

```csharp
using Platform.Maui.Linux.SecureStorage;

builder.Services.AddLinuxSecureStorage("com.example.myapp");
builder.AddLinuxGtk4Essentials();
```

Use a stable application ID, ideally the same value as the host's `ApplicationId`.
There is deliberately no fallback to a shared `"unknown"` ID or an inferred
assembly name. The extension replaces existing `ISecureStorage` registrations and
works before or after the Linux GTK4 backend's `TryAddSingleton` registration.

The maui-labs Linux backend's Essentials initializer connects the registered
`ISecureStorage` to `SecureStorage.Default` during application initialization.
The plugin itself does **not** reflect into MAUI's private static setters. With
another backend, consume the registered `ISecureStorage` through DI unless that
backend supplies equivalent facade initialization:

```csharp
public sealed class TokenStore(Microsoft.Maui.Storage.ISecureStorage storage)
{
    public Task SaveAsync(string token) => storage.SetAsync("access-token", token);
    public Task<string?> ReadAsync() => storage.GetAsync("access-token");
}
```

Non-Linux hosts should keep their platform's existing MAUI storage implementation.
Do not register this plugin unconditionally in a shared cross-platform startup.

## Direct usage

```csharp
using Platform.Maui.Linux.SecureStorage;

using var storage = new LinuxSecureStorage("com.example.myapp");
await storage.SetAsync("access-token", token);
string? saved = await storage.GetAsync("access-token");
bool removed = storage.Remove("access-token");
storage.RemoveAll();
```

`GetAsync` returns `null` only when no matching item exists. Empty string values
are supported. Application IDs and keys must be nonempty, non-whitespace text;
all inputs must contain valid Unicode and no embedded NULs. Native strings
otherwise risk truncating or aliasing distinct keys.

`GetAsync` and `SetAsync` run blocking native work off the caller's thread.
`Remove` and `RemoveAll` are synchronous to match `ISecureStorage` and can block
on D-Bus or an unlock prompt; dispatch them off the UI thread when appropriate.
Operations on one instance, including disposal, are serialized. `Backend` is
always `"libsecret"` and identifies the configuration, not service availability.

Native/service failures throw `InvalidOperationException`; missing native
dependencies or a non-Linux host throw `PlatformNotSupportedException`. Do not
interpret those exceptions as a missing credential. Avoid logging secret values.

## Compatibility and migration

The schema remains `org.maui.gtk.securestorage.v2` with `application` and `key`
attributes. Existing **libsecret** entries from the GTK4 implementation remain
accessible if you provide the same application ID it previously resolved.
Preview-era schemas are not imported.

The old `secure_store.dat` / `secure_store.key` fallback is **never read, written,
migrated, or deleted** by this package, including during `RemoveAll`. If you used
it, explicitly plan migration and cleanup with your application, and consider
rotating credentials that were protected only by a key beside their ciphertext.
Changing the application ID creates a separate namespace and does not erase old
entries.

## Build and sample

From the repository root:

```sh
dotnet build Platform/Linux.SecureStorage/Linux.SecureStorage.slnx
dotnet test Platform/Linux.SecureStorage/Linux.SecureStorage.slnx
dotnet run --project Platform/Linux.SecureStorage/samples/Linux.SecureStorage.Sample
dotnet pack Platform/Linux.SecureStorage/src/Platform.Maui.Linux.SecureStorage -c Release -o artifacts/packages
```

The console sample stores, reads, and removes a sample-only value without printing
the secret. It uses its own application ID and needs no GTK window.

Unit tests run on any supported SDK host. Native tests are opt-in because they
touch a real keyring. Run them **only in a disposable Linux account/container and
D-Bus session**, never against your everyday desktop keyring: one regression test
locks the entire default collection.

In that disposable environment, with `gnome-keyring`, `dbus-run-session`, and the
native libraries installed:

```sh
export LINUX_SECURE_STORAGE_INTEGRATION=1
export LINUX_SECURE_STORAGE_TEST_PASSWORD=disposable-test-password
dbus-run-session -- bash -euc '
  printf "%s" "$LINUX_SECURE_STORAGE_TEST_PASSWORD" | gnome-keyring-daemon --unlock --components=secrets
  dotnet test Platform/Linux.SecureStorage/Linux.SecureStorage.slnx --filter Category=Keyring
'
```

The lock regression is skipped by default even with native tests enabled. To run
it, start a **separate disposable home and D-Bus session**, set
`LINUX_SECURE_STORAGE_LOCK_TEST=1`, and select `--filter Category=LockedKeyring`.
It intentionally leaves the default collection locked, so do not share that
session with other tests or applications.

The test password is for a disposable keyring, not an application secret. CI
creates private home directories and sessions for both runs. Separately, an
intentionally broken bus verifies all four operations fail without a service:

```sh
LINUX_SECURE_STORAGE_NO_SERVICE=1 DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent/securestorage-test-bus \
  dotnet test Platform/Linux.SecureStorage/Linux.SecureStorage.slnx --filter Category=NoService
```

Upstream MIT attribution is preserved in
[THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt), also included in the NuGet package.
