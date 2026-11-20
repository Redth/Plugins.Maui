using Platform.Maui.Linux.SecureStorage;

if (!OperatingSystem.IsLinux())
{
    Console.Error.WriteLine("Run this sample on a Linux desktop with libsecret and a Secret Service keyring.");
    return 1;
}

try
{
    using var storage = new LinuxSecureStorage("com.example.pluginsmaui.securestorage.sample");
    const string key = "sample-token";
    try
    {
        await storage.SetAsync(key, "sample-only-not-a-real-token");
        var value = await storage.GetAsync(key);
        if (value != "sample-only-not-a-real-token")
            throw new InvalidOperationException("The stored value did not round-trip.");
        Console.WriteLine("Secret stored and retrieved successfully. Its value is not printed.");
    }
    finally
    {
        storage.Remove(key);
    }

    return 0;
}
catch (Exception error) when (error is PlatformNotSupportedException or InvalidOperationException)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}
