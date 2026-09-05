using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;

namespace Taskdeck.Cli;

/// <summary>
/// Creates secret files that are born owner-only (#1262) and re-restricts existing ones (#2667).
///
/// This is a deliberate, faithful copy of the API-side helper
/// <c>Taskdeck.Api.FirstRun.FirstRunBootstrapper.WriteRestrictedFile</c> /
/// <c>CreateRestrictedNewFile</c> / <c>CreateOwnerOnlyFileWindows</c> /
/// <c>BuildOwnerOnlyFileSecurity</c> / <c>RestrictFileToCurrentUser</c>
/// (backend/src/Taskdeck.Api/FirstRun/FirstRunBootstrapper.cs), shipped by PR #1267 for #1264 and by
/// #1241 for the forward-remediation helper. The CLI cannot reference the API project (Taskdeck.Cli
/// references Application and Infrastructure only, and Taskdeck.Architecture.Tests enforces that), so the
/// implementation is duplicated here rather than shared. Keep the two in sync: a change to the
/// lockdown contract on either side belongs on both, and
/// <c>CliRestrictedFileWriterParityTests</c> fails when the two copies drift.
/// </summary>
internal static class RestrictedFileWriter
{
    /// <summary>
    /// Creates <paramref name="path"/> ATOMICALLY with owner-only permissions and
    /// <see cref="FileShare.None"/>, then writes <paramref name="contents"/> through that same handle.
    /// On Unix the file is born <c>0600</c> (<see cref="FileStreamOptions.UnixCreateMode"/>) and the exact
    /// mode is then pinned through the open handle (umask-proof); on Windows the protected owner-only DACL
    /// is supplied to <c>CreateFile</c> itself and read back through the open handle, so a filesystem that
    /// silently ignores security descriptors (FAT32/exFAT, some SMB shares) fails closed instead of
    /// persisting the secret unprotected. Unlike create-then-restrict there is no instant at which another
    /// local user can open the file, and no pre-opened handle can survive into the written secret;
    /// <see cref="FileMode.CreateNew"/> additionally refuses to adopt a file someone pre-created at the
    /// path. Any failure is normalized to <see cref="IOException"/> so first-run callers'
    /// <c>catch (IOException)</c> falls back to an in-memory value; a partially-written file is
    /// best-effort deleted so callers never observe a half-written secret file.
    /// </summary>
    internal static void WriteRestrictedFile(string path, string contents)
        => WriteRestrictedFile(path, Encoding.UTF8.GetBytes(contents));

    internal static void WriteRestrictedFile(string path, byte[] contents)
    {
        FileStream stream;
        try
        {
            stream = CreateRestrictedNewFile(path);
        }
        catch (IOException)
        {
            // Creation failed -> nothing of ours remains at the path (CreateRestrictedNewFile removes its
            // own file when the post-create lockdown pin/verification fails); never delete what we did not
            // create (CreateNew fails precisely when the path is already occupied).
            throw;
        }
        catch (Exception ex)
        {
            // Normalize (e.g. UnauthorizedAccessException, PlatformNotSupportedException) so the callers'
            // catch(IOException) handles it uniformly -- the secret is never written to an unprotected file.
            throw new IOException(
                $"Could not create {path} restricted to the current user; refusing to write the secret to it.", ex);
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
            // We created the file, so a failed/partial write cleans it up (it was restricted from birth, so
            // this is consistency hygiene, not exposure). Best-effort; the original failure propagates.
            try { File.Delete(path); } catch { /* ignore */ }
            if (ex is IOException)
            {
                throw;
            }

            throw new IOException(
                $"Could not write the restricted file {path}; refusing to leave a partial secret file.", ex);
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
            UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
        });
        try
        {
            // open(2)'s mode argument is umask-masked (the mask can only STRIP bits from 0600, never widen
            // it) and is silently ignored on non-POSIX filesystems (e.g. vfat). Pin exactly 0600 through the
            // open handle (fchmod -- race-free): this restores the exact-mode guarantee regardless of umask,
            // and it FAILS on filesystems that cannot store the mode exactly where the pre-fix
            // SetUnixFileMode(path) call failed -- keeping the fail-closed contract instead of silently
            // persisting the secret with whatever mode the mount dictates.
            File.SetUnixFileMode(stream.SafeFileHandle, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            return stream;
        }
        catch
        {
            // We created this file; a failed lockdown must not leave it behind. Best-effort; the original
            // exception propagates (normalized to IOException by the caller).
            stream.Dispose();
            try { File.Delete(path); } catch { /* ignore */ }
            throw;
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileStream CreateOwnerOnlyFileWindows(string path)
    {
        // ReadPermissions (READ_CONTROL) is requested alongside Write so the DACL VERIFICATION below can
        // read the security descriptor back through this same exclusive handle.
        var stream = new FileInfo(path).Create(
            FileMode.CreateNew,
            FileSystemRights.Write | FileSystemRights.ReadPermissions,
            FileShare.None,
            bufferSize: 4096,
            FileOptions.None,
            BuildOwnerOnlyFileSecurity());
        try
        {
            // CreateFileW silently IGNORES the supplied security descriptor on filesystems without ACL
            // support (FAT32/exFAT, some SMB shares): the create succeeds and the file is world-readable.
            // Read the DACL back through the open handle -- on NTFS this merely confirms the
            // atomically-applied descriptor; on a non-ACL volume it throws or reports an unprotected DACL
            // and we refuse rather than persist the key unprotected.
            var applied = stream.GetAccessControl();
            if (!applied.AreAccessRulesProtected)
            {
                throw new IOException(
                    $"The filesystem hosting {path} did not honor the owner-only ACL (FAT32/exFAT and some " +
                    "network shares cannot store it); refusing to write the secret to an unprotected file.");
            }

            return stream;
        }
        catch
        {
            // We created this file; a failed lockdown verification must not leave it behind. Best-effort;
            // the original exception propagates (normalized to IOException by the caller).
            stream.Dispose();
            try { File.Delete(path); } catch { /* ignore */ }
            throw;
        }
    }

    /// <summary>
    /// Restricts an EXISTING file to the current user only (#1241). On Unix this is <c>0600</c>; on Windows
    /// it replaces the DACL with a single owner-only ACE and disables inheritance (so the directory's
    /// default ACEs -- e.g. BUILTIN\Users read -- do not apply). Used for forward remediation of a connector
    /// key file that a pre-#1262 CLI build already wrote unprotected; NEW secret files are instead created
    /// atomically restricted via <see cref="WriteRestrictedFile(string, string)"/>.
    /// Any failure is normalized to <see cref="IOException"/> so callers'
    /// <c>catch (IOException)</c> handle it uniformly.
    ///
    /// Faithful copy of <c>Taskdeck.Api.FirstRun.FirstRunBootstrapper.RestrictFileToCurrentUser</c>.
    /// </summary>
    internal static void RestrictFileToCurrentUser(string path)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                return;
            }

            new FileInfo(path).SetAccessControl(BuildOwnerOnlyFileSecurity());
        }
        catch (IOException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Normalize (e.g. UnauthorizedAccessException, PlatformNotSupportedException) so the callers'
            // catch(IOException) handles it uniformly -- never leave the plaintext secret in an unprotected file.
            throw new IOException(
                $"Could not restrict {path} to the current user; refusing to write the secret to it.", ex);
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSecurity BuildOwnerOnlyFileSecurity()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var owner = identity.User;
        if (owner is null)
        {
            // Without a resolvable SID we cannot scope the ACL; fail loudly (normalized to IOException by
            // the caller) rather than leave the secret with the inherited, potentially world-readable ACL.
            throw new InvalidOperationException(
                "Could not resolve the current Windows user SID to restrict the secrets file.");
        }

        var security = new FileSecurity();
        // Drop inherited ACEs and grant the current user full control -- the only ACE on the file.
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(owner, FileSystemRights.FullControl, AccessControlType.Allow));
        return security;
    }
}
