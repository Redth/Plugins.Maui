using Microsoft.Extensions.DependencyInjection;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.SecureStorage.Tests;

public sealed class LinuxSecureStorageTests
{
    public static TheoryData<string> InvalidIdentifiers => new()
    {
        "", " \t", "key\0suffix"
    };

    [Theory]
    [MemberData(nameof(InvalidIdentifiers))]
    public void RejectsInvalidApplicationIds(string applicationId)
    {
        Assert.Throws<ArgumentException>(() => new LinuxSecureStorage(applicationId, new MemoryStore()));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLinuxSecureStorage(applicationId));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => new LinuxSecureStorage(null!, new MemoryStore()));
        Assert.Throws<ArgumentNullException>(() => new LinuxSecureStorage("app", null!));
        Assert.Throws<ArgumentNullException>(() => ServiceCollectionExtensions.AddLinuxSecureStorage(null!, "app"));
        using var storage = new LinuxSecureStorage("app", new MemoryStore());
        Assert.Throws<ArgumentNullException>(() => { _ = storage.GetAsync(null!); });
        Assert.Throws<ArgumentNullException>(() => { _ = storage.SetAsync(null!, "value"); });
        Assert.Throws<ArgumentNullException>(() => { _ = storage.SetAsync("key", null!); });
        Assert.Throws<ArgumentNullException>(() => storage.Remove(null!));
    }

    [Theory]
    [MemberData(nameof(InvalidIdentifiers))]
    public void RejectsInvalidKeysBeforeCallingTheBackend(string key)
    {
        var backend = new MemoryStore();
        using var storage = new LinuxSecureStorage("app", backend);
        Assert.Throws<ArgumentException>(() => { _ = storage.GetAsync(key); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync(key, "value"); });
        Assert.Throws<ArgumentException>(() => storage.Remove(key));
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public void RejectsValuesContainingNul()
    {
        var backend = new MemoryStore();
        using var storage = new LinuxSecureStorage("app", backend);
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync("key", "value\0suffix"); });
        Assert.Equal(0, backend.Calls);
    }

    [Theory]
    [InlineData(0xd800)]
    [InlineData(0xdc00)]
    public void RejectsUnpairedSurrogates(int codeUnit)
    {
        var text = new string((char)codeUnit, 1);
        var backend = new MemoryStore();
        Assert.Throws<ArgumentException>(() => new LinuxSecureStorage(text, backend));
        Assert.Throws<ArgumentException>(() => new ServiceCollection().AddLinuxSecureStorage(text));
        using var storage = new LinuxSecureStorage("app", backend);
        Assert.Throws<ArgumentException>(() => { _ = storage.GetAsync(text); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync(text, "value"); });
        Assert.Throws<ArgumentException>(() => { _ = storage.SetAsync("key", text); });
        Assert.Throws<ArgumentException>(() => storage.Remove(text));
        Assert.Equal(0, backend.Calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("secret-\u00e9-\U0001f512")]
    [InlineData("replacement-\ufffd")]
    public async Task RoundTripsTextIncludingEmptyValues(string value)
    {
        using var storage = new LinuxSecureStorage("app", new MemoryStore());
        Assert.Null(await storage.GetAsync("key"));
        await storage.SetAsync("key", value);
        Assert.Equal(value, await storage.GetAsync("key"));
        await storage.SetAsync("key", "replacement");
        Assert.Equal("replacement", await storage.GetAsync("key"));
        Assert.True(storage.Remove("key"));
        Assert.False(storage.Remove("key"));
        Assert.Null(await storage.GetAsync("key"));
    }

    [Fact]
    public async Task ScopesEveryOperationToTheApplication()
    {
        var backend = new MemoryStore();
        using var first = new LinuxSecureStorage("first", backend);
        using var second = new LinuxSecureStorage("second", backend);
        await first.SetAsync("shared", "first-value");
        await first.SetAsync("other", "other-value");
        await second.SetAsync("shared", "second-value");
        first.RemoveAll();
        Assert.Null(await first.GetAsync("shared"));
        Assert.Null(await first.GetAsync("other"));
        Assert.Equal("second-value", await second.GetAsync("shared"));
        Assert.Equal("libsecret", first.Backend);
        Assert.Equal("first", first.ApplicationId);
    }

    [Fact]
    public async Task PropagatesAllBackendFailures()
    {
        var error = new InvalidOperationException("Unavailable or locked.");
        using var storage = new LinuxSecureStorage("app", new FailingStore(error));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("key")));
        Assert.Same(error, await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("key", "value")));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(() => storage.Remove("key")));
        Assert.Same(error, Assert.Throws<InvalidOperationException>(storage.RemoveAll));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ExplainsMissingNativeDependencies(bool missingLibrary)
    {
        Exception nativeError = missingLibrary ? new DllNotFoundException() : new EntryPointNotFoundException();
        using var storage = new LinuxSecureStorage("app", new FailingStore(nativeError));
        var error = await Assert.ThrowsAsync<PlatformNotSupportedException>(() => storage.GetAsync("key"));
        Assert.Same(nativeError, error.InnerException);
    }

    [Fact]
    public async Task RejectsAllOperationsAfterDisposal()
    {
        var backend = new MemoryStore();
        var storage = new LinuxSecureStorage("app", backend);
        storage.Dispose();
        storage.Dispose();
        await Assert.ThrowsAsync<ObjectDisposedException>(() => storage.GetAsync("key"));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => storage.SetAsync("key", "value"));
        Assert.Throws<ObjectDisposedException>(() => storage.Remove("key"));
        Assert.Throws<ObjectDisposedException>(storage.RemoveAll);
        Assert.Equal(0, backend.Calls);
    }

    [Fact]
    public async Task SerializesConcurrentOperations()
    {
        var backend = new ConcurrencyStore();
        using var storage = new LinuxSecureStorage("app", backend);
        await Task.WhenAll(Enumerable.Range(0, 64).Select(index => storage.SetAsync(index.ToString(), "value")));
        Assert.Equal(1, backend.MaximumConcurrency);
    }

    [Fact]
    public async Task DisposalWaitsForAnActiveOperation()
    {
        using var backend = new BlockingStore();
        var storage = new LinuxSecureStorage("app", backend);
        var read = storage.GetAsync("key");
        Assert.True(backend.Started.Wait(TimeSpan.FromSeconds(10)));
        using var disposing = new ManualResetEventSlim();
        var dispose = Task.Run(() =>
        {
            disposing.Set();
            storage.Dispose();
            return backend.Finished;
        });
        try
        {
            Assert.True(disposing.Wait(TimeSpan.FromSeconds(10)));
        }
        finally
        {
            backend.Release.Set();
        }
        Assert.True(await dispose.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.Null(await read);
    }

    [Fact]
    public void RegistrationReplacesOtherStorageAndUsesASingleton()
    {
        using var original = new LinuxSecureStorage("original", new MemoryStore());
        var services = new ServiceCollection();
        services.AddSingleton<ISecureStorage>(original);
        services.AddSingleton<ISecureStorage>(original);
        Assert.Same(services, services.AddLinuxSecureStorage("first").AddLinuxSecureStorage("replacement"));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISecureStorage));
        using var provider = services.BuildServiceProvider();
        if (OperatingSystem.IsLinux())
        {
            var storage = Assert.IsType<LinuxSecureStorage>(provider.GetRequiredService<ISecureStorage>());
            Assert.Equal("replacement", storage.ApplicationId);
            Assert.Same(storage, provider.GetRequiredService<ISecureStorage>());
        }
        else
        {
            Assert.Throws<PlatformNotSupportedException>(() => provider.GetRequiredService<ISecureStorage>());
        }
    }

    private sealed class MemoryStore : ISecretStore
    {
        private readonly Dictionary<(string App, string Key), string> values = new();
        internal int Calls { get; private set; }

        public string? Get(string applicationId, string key)
        {
            Calls++;
            return values.GetValueOrDefault((applicationId, key));
        }

        public void Set(string applicationId, string key, string value)
        {
            Calls++;
            values[(applicationId, key)] = value;
        }

        public bool Remove(string applicationId, string? key)
        {
            Calls++;
            var matches = values.Keys.Where(entry => entry.App == applicationId && (key is null || entry.Key == key)).ToArray();
            foreach (var match in matches)
                values.Remove(match);
            return matches.Length > 0;
        }
    }

    private sealed class FailingStore(Exception error) : ISecretStore
    {
        public string? Get(string applicationId, string key) => throw error;
        public void Set(string applicationId, string key, string value) => throw error;
        public bool Remove(string applicationId, string? key) => throw error;
    }

    private sealed class ConcurrencyStore : ISecretStore
    {
        private int active;
        internal int MaximumConcurrency { get; private set; }

        public string? Get(string applicationId, string key) => throw new NotSupportedException();
        public bool Remove(string applicationId, string? key) => throw new NotSupportedException();

        public void Set(string applicationId, string key, string value)
        {
            var count = Interlocked.Increment(ref active);
            MaximumConcurrency = Math.Max(MaximumConcurrency, count);
            Thread.SpinWait(10_000);
            Interlocked.Decrement(ref active);
        }
    }

    private sealed class BlockingStore : ISecretStore, IDisposable
    {
        internal ManualResetEventSlim Started { get; } = new();
        internal ManualResetEventSlim Release { get; } = new();
        internal bool Finished { get; private set; }

        public string? Get(string applicationId, string key)
        {
            Started.Set();
            if (!Release.Wait(TimeSpan.FromSeconds(10)))
                throw new TimeoutException("The blocking test operation was not released.");
            Finished = true;
            return null;
        }

        public void Set(string applicationId, string key, string value) => throw new NotSupportedException();
        public bool Remove(string applicationId, string? key) => throw new NotSupportedException();

        public void Dispose()
        {
            Started.Dispose();
            Release.Dispose();
        }
    }
}
