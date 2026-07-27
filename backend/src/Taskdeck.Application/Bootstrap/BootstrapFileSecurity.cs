using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Taskdeck.Application.Bootstrap;

/// <summary>Owner-only creation and verification for bootstrap files containing persisted secrets.</summary>
public static class BootstrapFileSecurity
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;

    public static void WriteRestrictedFile(string path, string contents)
        => WriteRestrictedFile(path, Encoding.UTF8.GetBytes(contents));

    public static void WriteRestrictedFile(string path, byte[] contents)
    {
        FileStream stream;
        try
        {
            stream = CreateRestrictedNewFile(path);
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Could not create {path} restricted to the current user; refusing to persist secrets.", ex);
        }

        try
        {
            using (stream)
            {
                stream.Write(contents, 0, contents.Length);
            }
        }
        catch (Exception ex)
        {
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
            if (ex is IOException)
            {
                throw;
            }

            throw new IOException($"Could not write restricted bootstrap file {path}.", ex);
        }
    }

    public static void RestrictFileToCurrentUser(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                VerifyFileOwnerOnly(path);
                return;
            }

            new FileInfo(path).SetAccessControl(BuildOwnerOnlyFileSecurity());
            VerifyFileOwnerOnly(path);
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex)
        {
            throw new IOException(
                $"Could not restrict {path} to the current user; refusing to persist or read secrets.", ex);
        }
    }

    public static void VerifyFileOwnerOnly(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(path);
            if (mode != OwnerReadWrite)
            {
                throw new IOException(
                    $"The filesystem did not retain owner-only mode 0600 for {path}; actual mode was {mode}.");
            }

            return;
        }

        VerifyOwnerOnlyAccessControl(new FileInfo(path).GetAccessControl(), path);
    }

    [SupportedOSPlatform("windows")]
    private static void VerifyOwnerOnlyAccessControl(FileSecurity security, string path)
    {
        if (!security.AreAccessRulesProtected)
        {
            throw new IOException($"The owner-only ACL for {path} still permits inherited access rules.");
        }

        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User
            ?? throw new IOException("Could not resolve the current Windows user SID.");
        var actualOwner = security.GetOwner(typeof(SecurityIdentifier));
        if (!owner.Equals(actualOwner))
        {
            throw new IOException($"The owner-only ACL for {path} is not owned by the current user.");
        }

        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        if (rules.Count == 0)
        {
            throw new IOException($"The owner-only ACL for {path} has no current-user access rule.");
        }

        foreach (FileSystemAccessRule rule in rules)
        {
            if (rule.AccessControlType != AccessControlType.Allow || !owner.Equals(rule.IdentityReference))
            {
                throw new IOException(
                    $"The owner-only ACL for {path} grants another principal or contains a deny rule.");
            }
        }
    }

    private static FileStream CreateRestrictedNewFile(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            return CreateOwnerOnlyFileWindows(path);
        }

        var stream = new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None,
            UnixCreateMode = OwnerReadWrite
        });
        try
        {
            File.SetUnixFileMode(stream.SafeFileHandle, OwnerReadWrite);
            var applied = File.GetUnixFileMode(stream.SafeFileHandle);
            if (applied != OwnerReadWrite)
            {
                throw new IOException(
                    $"The filesystem hosting {path} did not retain owner-only mode 0600; actual mode was " +
                    $"{applied}. Refusing to write the secret to an unprotected file.");
            }

            return stream;
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyFileWindows(string path)
    {
        var stream = new FileInfo(path).Create(
            FileMode.CreateNew,
            FileSystemRights.Write | FileSystemRights.ReadPermissions,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None,
            BuildOwnerOnlyFileSecurity());
        try
        {
            var applied = stream.GetAccessControl();
            VerifyOwnerOnlyAccessControl(applied, path);

            return stream;
        }
        catch
        {
            stream.Dispose();
            try { File.Delete(path); } catch { /* best-effort cleanup */ }
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSecurity BuildOwnerOnlyFileSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User
            ?? throw new InvalidOperationException("Could not resolve the current Windows user SID.");
        var security = new FileSecurity();
        security.SetOwner(owner);
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            owner,
            FileSystemRights.FullControl,
            AccessControlType.Allow));
        return security;
    }
}
