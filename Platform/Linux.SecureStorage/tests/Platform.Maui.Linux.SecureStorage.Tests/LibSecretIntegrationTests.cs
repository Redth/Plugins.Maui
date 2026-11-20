using System.Runtime.InteropServices;
using static Platform.Maui.Linux.SecureStorage.LibSecretInterop;

namespace Platform.Maui.Linux.SecureStorage.Tests;

[CollectionDefinition("Secret Service", DisableParallelization = true)]
public sealed class SecretServiceCollection;

[Collection("Secret Service")]
[Trait("Category", "Keyring")]
public sealed class LibSecretIntegrationTests
{
    [LinuxKeyringFact]
    public async Task NativeRoundTripUpdateAndRemoval()
    {
        using var storage = CreateStorage();
        try
        {
            Assert.Null(await storage.GetAsync("missing"));
            Assert.False(storage.Remove("missing"));
            foreach (var value in new[] { "first", "", "  ", "secret-\u00e9-\U0001f512" })
            {
                await storage.SetAsync("unicode-\u00e9-\U0001f512", value);
                Assert.Equal(value, await storage.GetAsync("unicode-\u00e9-\U0001f512"));
            }
            Assert.True(storage.Remove("unicode-\u00e9-\U0001f512"));
            Assert.Null(await storage.GetAsync("unicode-\u00e9-\U0001f512"));
        }
        finally
        {
            storage.RemoveAll();
        }
    }

    [LinuxKeyringFact]
    public async Task NativeStoragePersistsAcrossInstancesAndScopesBulkDeletion()
    {
        using var first = CreateStorage();
        using var reopened = new LinuxSecureStorage(first.ApplicationId);
        using var second = CreateStorage();
        try
        {
            await first.SetAsync("same-key", "first");
            await first.SetAsync("another-key", "another");
            await second.SetAsync("same-key", "second");
            Assert.Equal("first", await reopened.GetAsync("same-key"));
            Assert.True(reopened.Remove("same-key"));
            Assert.Equal("second", await second.GetAsync("same-key"));
            first.RemoveAll();
            Assert.Null(await reopened.GetAsync("another-key"));
            Assert.Equal("second", await second.GetAsync("same-key"));
            first.RemoveAll();
        }
        finally
        {
            first.RemoveAll();
            second.RemoveAll();
        }
    }

    [LinuxKeyringFact]
    public async Task NativeCallsAreSafeAcrossConcurrentInstances()
    {
        using var first = CreateStorage();
        using var second = new LinuxSecureStorage(first.ApplicationId);
        try
        {
            await Task.WhenAll(Enumerable.Range(0, 16).Select(async index =>
            {
                var storage = index % 2 == 0 ? first : second;
                var key = $"key-{index}";
                await storage.SetAsync(key, $"value-{index}");
                Assert.Equal($"value-{index}", await storage.GetAsync(key));
            }));
            first.RemoveAll();
            for (var index = 0; index < 16; index++)
                Assert.Null(await second.GetAsync($"key-{index}"));
        }
        finally
        {
            first.RemoveAll();
        }
    }

    [LinuxLockedKeyringFact]
    [Trait("Category", "LockedKeyring")]
    public async Task LockedItemsAreNotReportedAsMissingOrSuccessfullyDeleted()
    {
        using var storage = CreateStorage();
        await storage.SetAsync("locked", "must-survive-a-failed-delete");
        StoreInSessionCollection(storage.ApplicationId, "unlocked", "must-not-be-partially-deleted");
        LockDefaultCollection();
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("locked"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("locked", "must-not-overwrite"));
        Assert.Throws<InvalidOperationException>(() => storage.Remove("locked"));
        Assert.Throws<InvalidOperationException>(storage.RemoveAll);
        AssertLockedItemRemains(storage.ApplicationId, "locked");
        Assert.Equal("must-not-be-partially-deleted", await storage.GetAsync("unlocked"));
        Assert.True(storage.Remove("unlocked"));
    }

    private static LinuxSecureStorage CreateStorage() => new($"com.example.securestorage.tests.{Guid.NewGuid():N}");

    private static void LockDefaultCollection()
    {
        using var service = new GObjectHandle(SecretServiceGet(0, IntPtr.Zero, out var error));
        CheckError(error);
        using var collection = new GObjectHandle(CollectionForAlias(service, "default", 0, IntPtr.Zero, out error));
        CheckError(error);
        Assert.False(collection.IsInvalid);
        var input = ListAppend(IntPtr.Zero, collection.DangerousGetHandle());
        try
        {
            var count = ServiceLock(service, input, IntPtr.Zero, out var locked, out error);
            using var output = new ItemListHandle(locked);
            CheckError(error);
            Assert.True(count > 0, "The disposable test keyring could not be locked.");
        }
        finally
        {
            ListFree(input);
        }
    }

    private static void StoreInSessionCollection(string applicationId, string key, string value)
    {
        using var service = new GObjectHandle(SecretServiceOpen(SecretServiceGetType(), IntPtr.Zero, 0, IntPtr.Zero, out var error));
        CheckError(error);
        using var schema = CreateSchema();
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", applicationId);
        attributes.Add("key", key);
        using var secret = new SecretValueHandle(SecretValueNew(value, -1, "text/plain"));
        var collection = Marshal.StringToCoTaskMemUTF8("session");
        try
        {
            var stored = SecretServiceStore(service, schema, attributes, collection, applicationId, secret, IntPtr.Zero, out error);
            CheckError(error);
            Assert.True(stored);
        }
        finally
        {
            Marshal.FreeCoTaskMem(collection);
        }
    }

    private static void AssertLockedItemRemains(string applicationId, string key)
    {
        using var service = new GObjectHandle(SecretServiceOpen(SecretServiceGetType(), IntPtr.Zero, 0, IntPtr.Zero, out var error));
        CheckError(error);
        using var schema = CreateSchema();
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", applicationId);
        attributes.Add("key", key);
        using var matches = new ItemListHandle(SecretServiceSearch(service, schema, attributes, SearchAll, IntPtr.Zero, out error));
        CheckError(error);
        var item = Assert.Single(matches.Items);
        Assert.True(SecretItemGetLocked(item));
    }

    private static SecretSchemaHandle CreateSchema()
    {
        using var attributes = new AttributeTableHandle();
        attributes.Add("application", null);
        attributes.Add("key", null);
        return new SecretSchemaHandle(SecretSchemaNew(LibSecretStore.SchemaName, 0, attributes));
    }

    private static void CheckError(IntPtr error)
    {
        if (error == IntPtr.Zero)
            return;
        GErrorFree(error);
        throw new InvalidOperationException("A native test setup operation failed.");
    }

    [DllImport("libsecret-1.so.0", EntryPoint = "secret_collection_for_alias_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr CollectionForAlias(
        GObjectHandle service, [MarshalAs(UnmanagedType.LPUTF8Str)] string alias, int flags, IntPtr cancellable, out IntPtr error);

    [DllImport("libsecret-1.so.0", EntryPoint = "secret_service_lock_sync", CallingConvention = CallingConvention.Cdecl)]
    private static extern int ServiceLock(
        GObjectHandle service, IntPtr objects, IntPtr cancellable, out IntPtr locked, out IntPtr error);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_list_append", CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr ListAppend(IntPtr list, IntPtr data);

    [DllImport("libglib-2.0.so.0", EntryPoint = "g_list_free", CallingConvention = CallingConvention.Cdecl)]
    private static extern void ListFree(IntPtr list);
}

public class LinuxKeyringFactAttribute : FactAttribute
{
    public LinuxKeyringFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("LINUX_SECURE_STORAGE_INTEGRATION") != "1")
            Skip = "Requires an explicitly enabled, disposable Linux Secret Service session.";
    }
}

public sealed class LinuxLockedKeyringFactAttribute : LinuxKeyringFactAttribute
{
    public LinuxLockedKeyringFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("LINUX_SECURE_STORAGE_LOCK_TEST") != "1")
            Skip = "Requires a separate disposable session: this test leaves the default collection locked.";
    }
}
