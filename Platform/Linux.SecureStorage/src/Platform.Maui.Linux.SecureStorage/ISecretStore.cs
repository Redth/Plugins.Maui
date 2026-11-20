namespace Platform.Maui.Linux.SecureStorage;

internal interface ISecretStore
{
    string? Get(string applicationId, string key);
    void Set(string applicationId, string key, string value);
    bool Remove(string applicationId, string? key);
}
