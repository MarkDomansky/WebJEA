using System.DirectoryServices;
using System.DirectoryServices.ActiveDirectory;
using System.Runtime.Versioning;
using System.Security.Claims;
using System.Security.Principal;

namespace WebJEA.Auth.Windows;

/// <summary>
/// Windows-authentication user context (the old UserInfo class). MemberOfIds carries the
/// SID claim values of the WindowsIdentity, plus the DOMAIN\user name claim value.
/// </summary>
[SupportedOSPlatform("windows")]
public class WindowsUserContext : IUserContext
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private string uname;
    private List<string> prvSIDs = new List<string>();
    private string prvDomainSID = "-";
    private string prvDomainDNSRoot = "-";

    public WindowsUserContext(ClaimsPrincipal curuser)
    {
        var winID = (WindowsIdentity)curuser.Identity;
        dlog.Trace("UserInfo: User: " + winID.Name);
        uname = winID.Name;

        foreach (Claim clm in winID.UserClaims)
        {
            dlog.Trace("UserInfo: Claims: " + clm.Value);
            // includes the domain\user entry
            prvSIDs.Add(clm.Value);
        }

        // get domain sid and dnsroot, if they don't exist, use machinename and sid
        SetMachineProperties();
        SetDomainProperties();
    }

    private void SetMachineProperties()
    {
        // preset to local machine values in case we're not domain joined
        try
        {
            const string regpath = @"HKEY_LOCAL_MACHINE\SOFTWARE\Microsoft\Cryptography";
            var readValue = Microsoft.Win32.Registry.GetValue(regpath, "MachineGuid", "-"); // i think this always fails for permissions
            prvDomainSID = Convert.ToString(readValue);
            prvDomainDNSRoot = Environment.MachineName;
        }
        catch
        {
        }
    }

    private void SetDomainProperties()
    {
        try
        {
            Domain domain = Domain.GetCurrentDomain();
            using DirectoryEntry de = domain.GetDirectoryEntry();
            var sidBytes = (byte[])de.Properties["objectSid"].Value;
            var domainSid = new SecurityIdentifier(sidBytes, 0);

            prvDomainSID = domainSid.AccountDomainSid.ToString();
            prvDomainDNSRoot = domain.Name;
        }
        catch (Exception)
        {
        }
    }

    public IReadOnlyList<string> MemberOfIds => prvSIDs;

    public string UserName => uname;

    public bool IsMemberOf(string groupId)
    {
        if (groupId == "*") return true;

        foreach (string usersid in prvSIDs)
        {
            if (groupId == usersid) return true;
        }

        return false;
    }

    public string OrgId => prvDomainSID;

    public string OrgName => prvDomainDNSRoot;
}
