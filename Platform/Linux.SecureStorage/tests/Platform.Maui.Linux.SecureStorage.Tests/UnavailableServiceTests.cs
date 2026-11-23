namespace Platform.Maui.Linux.SecureStorage.Tests;

[Trait("Category", "NoService")]
public sealed class UnavailableServiceTests
{
    [UnavailableServiceFact]
    public async Task AllOperationsFailExplicitlyWithoutAService()
    {
        using var storage = new LinuxSecureStorage("com.example.securestorage.unavailable");
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.GetAsync("key"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => storage.SetAsync("key", "value"));
        Assert.Throws<InvalidOperationException>(() => storage.Remove("key"));
        Assert.Throws<InvalidOperationException>(storage.RemoveAll);
    }
}

public sealed class UnavailableServiceFactAttribute : FactAttribute
{
    public UnavailableServiceFactAttribute()
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("LINUX_SECURE_STORAGE_NO_SERVICE") != "1")
            Skip = "Requires a Linux test process with an intentionally unavailable session bus.";
    }
}
