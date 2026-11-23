using System.Text;
using Microsoft.Maui.Storage;

namespace Platform.Maui.Linux.SecureStorage;

/// <summary>Stores MAUI secrets in the current Linux user's Secret Service.</summary>
public sealed class LinuxSecureStorage : ISecureStorage, IDisposable
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly object sync = new();
    private readonly ISecretStore store;
    private bool disposed;

    /// <summary>Creates storage scoped to an explicit, stable application identifier.</summary>
    /// <param name="applicationId">The application's unique identifier, such as com.example.myapp.</param>
    /// <exception cref="PlatformNotSupportedException">The current operating system is not Linux.</exception>
    public LinuxSecureStorage(string applicationId)
        : this(applicationId, new LibSecretStore())
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("Linux SecureStorage requires Linux and libsecret.");
    }

    internal LinuxSecureStorage(string applicationId, ISecretStore store)
    {
        ValidateText(applicationId, nameof(applicationId), allowEmpty: false);
        ArgumentNullException.ThrowIfNull(store);
        ApplicationId = applicationId;
        this.store = store;
    }

    /// <summary>Gets the application attribute used to scope every operation.</summary>
    public string ApplicationId { get; }

    /// <summary>Gets the configured backend name; this does not probe service availability.</summary>
    public string Backend => "libsecret";

    /// <inheritdoc />
    public Task<string?> GetAsync(string key)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        return Task.Run(() => Execute(() => store.Get(ApplicationId, key)));
    }

    /// <inheritdoc />
    public Task SetAsync(string key, string value)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        ValidateText(value, nameof(value), allowEmpty: true);
        return Task.Run(() => Execute(() =>
        {
            store.Set(ApplicationId, key, value);
            return true;
        }));
    }

    /// <inheritdoc />
    public bool Remove(string key)
    {
        ValidateText(key, nameof(key), allowEmpty: false);
        return Execute(() => store.Remove(ApplicationId, key));
    }

    /// <inheritdoc />
    public void RemoveAll() => Execute(() => store.Remove(ApplicationId, null));

    /// <summary>Prevents further operations, waiting for an active operation to finish.</summary>
    public void Dispose()
    {
        lock (sync)
            disposed = true;
    }

    private T Execute<T>(Func<T> operation)
    {
        lock (sync)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            try
            {
                return operation();
            }
            catch (DllNotFoundException error)
            {
                throw new PlatformNotSupportedException(
                    "Linux SecureStorage requires libsecret-1.so.0, libglib-2.0.so.0, and libgobject-2.0.so.0.",
                    error);
            }
            catch (EntryPointNotFoundException error)
            {
                throw new PlatformNotSupportedException(
                    "The installed libsecret or GLib version does not provide the required APIs.", error);
            }
        }
    }

    internal static void ValidateText(string value, string parameterName, bool allowEmpty)
    {
        ArgumentNullException.ThrowIfNull(value, parameterName);
        if (!allowEmpty && string.IsNullOrWhiteSpace(value))
            throw new ArgumentException("The value must not be empty or whitespace.", parameterName);
        if (value.Contains('\0'))
            throw new ArgumentException("Embedded NUL characters are not supported by libsecret.", parameterName);

        try
        {
            _ = StrictUtf8.GetByteCount(value);
        }
        catch (EncoderFallbackException)
        {
            throw new ArgumentException("The value must contain valid Unicode text.", parameterName);
        }
    }
}
