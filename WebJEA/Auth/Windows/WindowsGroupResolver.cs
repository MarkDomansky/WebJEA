using System.DirectoryServices.AccountManagement;
using System.Runtime.Versioning;

namespace WebJEA.Auth.Windows;

[SupportedOSPlatform("windows")]
public class WindowsGroupResolver : IGroupResolver
{
    private NLog.Logger dlog = NLog.LogManager.GetCurrentClassLogger();

    private Dictionary<string, PrincipalContext> prvPC = new Dictionary<string, PrincipalContext>();

    public string GetSID(string input)
    {
        dlog.Trace("GroupFinder: GetSID: Cached Contexts: " + prvPC.Count.ToString());

        string groupname = "";
        string groupcontext = "";
        dlog.Trace("GroupFinder: GetSID: Input: " + input);
        if (input == "*") // allow all users
        {
            dlog.Trace("GroupFinder: GetSID: * All Authd Users");
            return "*";
        }
        else if (input.Contains("\\")) // process as domain\sam
        {
            groupname = input.Split('\\')[1];
            groupcontext = input.Split('\\')[0].ToUpper();
        }
        else if (input.Contains("@")) // process as upn
        {
            groupname = input.Split('@')[0];
            groupcontext = input.Split('@')[1].ToUpper();
        }
        else
        {
            // no groupcontext
            groupname = input;
        }

        if (groupcontext == ".") // context doesn't seem to reliably support ".", so we convert to machinename for clarity.
        {
            groupcontext = Environment.MachineName;
        }

        // create a context for the domain/machine in the group
        PrincipalContext pc = null;
        if (prvPC.ContainsKey(groupcontext)) // sid already found in cache
        {
            dlog.Trace("GroupFinder: GetSID: Found cached Context: " + groupcontext);
            pc = prvPC[groupcontext];
        }
        else
        {
            // try connecting to domain
            if (groupcontext == "")
            {
                dlog.Trace("GroupFinder: GetSID: Adding Default Domain Context");
                try
                {
                    pc = new PrincipalContext(ContextType.Domain);
                }
                catch (Exception)
                {
                    dlog.Trace("GroupFinder: GetSID: Failed to resolve as Default Domain Context ()");
                }
            }
            else
            {
                try
                {
                    pc = new PrincipalContext(ContextType.Domain, groupcontext);
                    dlog.Trace("GroupFinder: GetSID: Adding Domain Context: " + groupcontext);
                }
                catch (Exception)
                {
                    dlog.Trace("GroupFinder: GetSID: Failed to resolve as Domain Context (" + groupcontext + ")");
                }

                if (pc == null)
                {
                    try
                    {
                        pc = new PrincipalContext(ContextType.Machine, groupcontext);
                        dlog.Trace("GroupFinder: GetSID: Adding Machine Context: " + groupcontext);
                    }
                    catch (Exception)
                    {
                        dlog.Trace("GroupFinder: GetSID: Failed to resolve as Machine Context (" + groupcontext + ")");
                    }
                }
            }

            if (pc != null)
            {
                prvPC.Add(groupcontext, pc);
            }
        }

        if (pc != null)
        {
            try
            {
                GroupPrincipal grp = GroupPrincipal.FindByIdentity(pc, groupname);
                if (grp != null)
                {
                    dlog.Trace("GroupFinder: GetSID: Found Group SID: " + groupname + ": " + grp.Sid.ToString());
                    return grp.Sid.ToString();
                }
            }
            catch (Exception)
            {
                // dlog.Error("GroupFinder: GetSID: Error Trying as Group. (" + groupname + ")");
            }

            try
            {
                UserPrincipal usr = UserPrincipal.FindByIdentity(pc, groupname);
                if (usr != null)
                {
                    dlog.Trace("GroupFinder: GetSID: Found User SID: " + groupname + ": " + usr.Sid.ToString());
                    return usr.Sid.ToString();
                }
            }
            catch (Exception)
            {
                // dlog.Error("GroupFinder: GetSID: Error Trying as User. (" + groupname + ")");
            }
        }

        dlog.Error("GroupFinder: GetSID: No SID Matched.");
        return "";
    }
}
