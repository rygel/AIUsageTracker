using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace AIConsumptionTracker.Infrastructure.Helpers;

public class WindowsBrowserCookieService
{
    private readonly ILogger<WindowsBrowserCookieService> _logger;

    public WindowsBrowserCookieService(ILogger<WindowsBrowserCookieService> logger)
    {
        _logger = logger;
    }

    public record BrowserProfile(string Name, string UserDataRoot, string ProfileName);

    public async Task<string> GetCookieHeaderAsync(string domain)
    {
        var browserRoots = new Dictionary<string, string>
        {
            { "Chrome", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google\\Chrome\\User Data") },
            { "Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft\\Edge\\User Data") },
            { "Brave", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "BraveSoftware\\Brave-Browser\\User Data") },
            { "Vivaldi", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Vivaldi\\User Data") },
            { "Opera", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opera Software\\Opera Stable") },
            { "Opera GX", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Opera Software\\Opera GX Stable") }
        };

        foreach (var (browserName, userDataRoot) in browserRoots)
        {
            if (!Directory.Exists(userDataRoot)) continue;

            var localStatePath = Path.Combine(userDataRoot, "Local State");
            if (!File.Exists(localStatePath)) continue;

            byte[]? masterKey = null;
            try
            {
                masterKey = GetMasterKey(localStatePath);
            }
            catch (Exception ex)
            {
                 _logger.LogWarning("Failed to decrypt master key for {Browser}: {Message}", browserName, ex.Message);
            }

            if (masterKey == null) continue;
            
            // Find all potential profile directories
            // Profiles usually look like: "Default", "Profile *", or custom named folders in Edge
            var profileDirs = Directory.GetDirectories(userDataRoot);
            
            foreach (var profileDir in profileDirs)
            {
                var profileName = Path.GetFileName(profileDir);
                // Simple filter to avoid scanning cache folders or other non-profile dirs excessively
                // Valid profiles usually have "Preferences" file or "Network" folder or "Cookies" file
                if (!File.Exists(Path.Combine(profileDir, "Preferences")) && 
                    !File.Exists(Path.Combine(profileDir, "Network", "Cookies")) &&
                    !File.Exists(Path.Combine(profileDir, "Cookies")))
                {
                    continue;
                }

                try
                {
                    var browserProfile = new BrowserProfile(browserName, userDataRoot, profileName);
                    // Pass pre-calculated master key to avoid re-decrypting
                    var cookies = await ExtractCookiesWithKeyAsync(browserProfile, masterKey, domain);
                    
                    if (cookies.Count > 0)
                    {
                        _logger.LogInformation("Successfully extracted {Count} cookies from {Browser} ({Profile})", cookies.Count, browserName, profileName);
                        return string.Join("; ", cookies.Select(c => $"{c.Key}={c.Value}"));
                    }
                }
                catch (Exception ex)
                {
                   _logger.LogDebug(ex, "Failed to check profile {Profile} in {Browser}", profileName, browserName);
                }
            }
        }
        
        // Check Firefox
        try 
        {
            var firefoxCookies = await ExtractFirefoxCookiesAsync(domain);
             if (firefoxCookies.Count > 0)
            {
                _logger.LogInformation("Successfully extracted {Count} cookies from Firefox", firefoxCookies.Count);
                return string.Join("; ", firefoxCookies.Select(c => $"{c.Key}={c.Value}"));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check Firefox");
        }

        return string.Empty;
    }

    private async Task<Dictionary<string, string>> ExtractCookiesWithKeyAsync(BrowserProfile browser, byte[] masterKey, string domain)
    {
        var cookies = new Dictionary<string, string>();
        var cookieDbPath = Path.Combine(browser.UserDataRoot, browser.ProfileName, "Network", "Cookies");

        if (!File.Exists(cookieDbPath))
        {
            // Try without "Network" subfolder (older Chrome versions)
            cookieDbPath = Path.Combine(browser.UserDataRoot, browser.ProfileName, "Cookies");
            if (!File.Exists(cookieDbPath)) return cookies;
        }

        var tempFile = Path.GetTempFileName();
        _logger.LogDebug("Decryption key ready. Copying cookie DB to {Temp}...", tempFile);

        // SQLite might be locked if browser is open, so copy it to a temp file
        try 
        {
            CopyLockedFile(cookieDbPath, tempFile);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to copy locked file {Source}: {Message}", cookieDbPath, ex.Message);
            return cookies;
        }

        try
        {
            using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=False"))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = "SELECT name, encrypted_value FROM cookies WHERE host_key LIKE $domain";
                command.Parameters.AddWithValue("$domain",  "%" + domain);

                _logger.LogDebug("Executing SQLite query for {Domain} in {DB}", domain, tempFile);
                using (var reader = await command.ExecuteReaderAsync())
                {
                    while (await reader.ReadAsync())
                    {
                        var name = reader.GetString(0);
                        var encryptedValue = (byte[])reader.GetValue(1);

                        try
                        {
                            var decryptedValue = DecryptCookie(encryptedValue, masterKey);
                            if (!string.IsNullOrEmpty(decryptedValue))
                            {
                                cookies[name] = decryptedValue;
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogTrace(ex, "Failed to decrypt cookie {Name}", name);
                        }
                    }
                }
            }
        }
        finally
        {
            try 
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to delete temp file {File}: {Message}", tempFile, ex.Message);
            }
        }

        return cookies;
    }

    private void CopyLockedFile(string sourcePath, string destinationPath)
    {
        using var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var destinationStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
        sourceStream.CopyTo(destinationStream);
    }

    private byte[] GetMasterKey(string localStatePath)
    {
        try
        {
            var json = File.ReadAllText(localStatePath);
            using var doc = JsonDocument.Parse(json);
            var encryptedKeyBase64 = doc.RootElement.GetProperty("os_crypt").GetProperty("encrypted_key").GetString();
            var encryptedKey = Convert.FromBase64String(encryptedKeyBase64);

            // Remove "DPAPI" header (5 bytes)
            var keyBuffer = new byte[encryptedKey.Length - 5];
            Array.Copy(encryptedKey, 5, keyBuffer, 0, keyBuffer.Length);

            return ProtectedData.Unprotect(keyBuffer, null, DataProtectionScope.CurrentUser);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get master key from {Path}", localStatePath);
            return null;
        }
    }

    private string DecryptCookie(byte[] encryptedValue, byte[] masterKey)
    {
        if (encryptedValue == null || encryptedValue.Length < 3) return string.Empty;

        // Check for v10 or v11 prefix
        string prefix = Encoding.ASCII.GetString(encryptedValue, 0, 3);
        if (prefix != "v10" && prefix != "v11")
        {
            // Old DPAPI-only encryption (pre-Chrome 80?)
            try
            {
                return Encoding.UTF8.GetString(ProtectedData.Unprotect(encryptedValue, null, DataProtectionScope.CurrentUser));
            }
            catch
            {
                return string.Empty;
            }
        }

        // AES-GCM decryption
        // [3 bytes prefix] [12 bytes nonce] [ciphertext...] [16 bytes tag]
        byte[] nonce = new byte[12];
        Array.Copy(encryptedValue, 3, nonce, 0, 12);

        int ciphertextLength = encryptedValue.Length - 3 - 12 - 16;
        byte[] ciphertext = new byte[ciphertextLength];
        Array.Copy(encryptedValue, 3 + 12, ciphertext, 0, ciphertextLength);

        byte[] tag = new byte[16];
        Array.Copy(encryptedValue, encryptedValue.Length - 16, tag, 0, 16);

        byte[] plaintext = new byte[ciphertextLength];

        using (var aesGcm = new AesGcm(masterKey, 16))
        {
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
        }

        return Encoding.UTF8.GetString(plaintext);
    }

    private async Task<Dictionary<string, string>> ExtractFirefoxCookiesAsync(string domain)
    {
        var cookies = new Dictionary<string, string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var profilesRoot = Path.Combine(appData, "Mozilla\\Firefox\\Profiles");

        if (!Directory.Exists(profilesRoot)) return cookies;

        foreach (var profileDir in Directory.GetDirectories(profilesRoot))
        {
            var cookieDbPath = Path.Combine(profileDir, "cookies.sqlite");
            if (!File.Exists(cookieDbPath)) continue;

             var tempFile = Path.GetTempFileName();
            _logger.LogDebug("Checking Firefox profile: {Profile}. Copying DB to {Temp}...", Path.GetFileName(profileDir), tempFile);

            try
            {
                CopyLockedFile(cookieDbPath, tempFile);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Failed to copy locked Firefox DB {Source}: {Message}", cookieDbPath, ex.Message);
                 continue;
            }

            try
            {
                 using (var connection = new SqliteConnection($"Data Source={tempFile};Pooling=False"))
                {
                    await connection.OpenAsync();
                    var command = connection.CreateCommand();
                    // Firefox stores cookies in moz_cookies table
                    command.CommandText = "SELECT name, value FROM moz_cookies WHERE host LIKE $domain";
                    command.Parameters.AddWithValue("$domain", "%" + domain);

                    using (var reader = await command.ExecuteReaderAsync())
                    {
                        while (await reader.ReadAsync())
                        {
                            var name = reader.GetString(0);
                            var value = reader.GetString(1);
                             if (!string.IsNullOrEmpty(value))
                            {
                                cookies[name] = value;
                            }
                        }
                    }
                }

                if (cookies.Count > 0) return cookies; // Found cookies, return immediately
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to read Firefox cookies from {Profile}", Path.GetFileName(profileDir));
            }
            finally
            {
                try 
                {
                    if (File.Exists(tempFile)) File.Delete(tempFile);
                }
                catch { }
            }
        }

        return cookies;
    }
}

