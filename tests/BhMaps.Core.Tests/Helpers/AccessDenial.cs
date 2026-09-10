using System.Security.AccessControl;
using System.Security.Principal;

namespace BhMaps.Core.Tests.Helpers;

/// <summary>Denies this user permission to list a folder, so enumerating it throws while it still exists. Restores the ACL on dispose.</summary>
public sealed class AccessDenial : IDisposable
{
    private readonly DirectoryInfo _directory;
    private readonly FileSystemAccessRule _rule;

    private AccessDenial(DirectoryInfo directory, FileSystemAccessRule rule)
    {
        _directory = directory;
        _rule = rule;
    }

    /// <summary>Adds a Deny ListDirectory ACE for the current user. Dispose removes it; a temp folder cannot be deleted until then.</summary>
    public static AccessDenial DenyListing(string directory)
    {
        var info = new DirectoryInfo(directory);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        return new AccessDenial(info, rule);
    }

    public void Dispose()
    {
        var security = _directory.GetAccessControl();
        security.RemoveAccessRule(_rule);
        _directory.SetAccessControl(security);
    }
}
