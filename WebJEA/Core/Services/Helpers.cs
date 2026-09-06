using System.Security.Cryptography;
using System.Text;

namespace WebJEA;

public static class Helpers
{
    public static string CoalesceString(params string[] arguments)
    {
        foreach (string argument in arguments)
        {
            if (argument is not null)
            {
                return argument;
            }
        }

        return null;
    }

    public static string GetFileContent(string filename)
    {
        if (File.Exists(filename))
        {
            using var fsobj = new FileStream(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var streamobj = new StreamReader(fsobj);
            return streamobj.ReadToEnd();
        }

        return null;
    }

    public static string StringHash256(string strin)
    {
        var uEncode = new UnicodeEncoding();

        byte[] bytin = uEncode.GetBytes(strin);

        using var sha = SHA256.Create();
        byte[] hash = sha.ComputeHash(bytin);
        return ByteArrayToHexString(hash);
    }

    private static string ByteArrayToHexString(byte[] bytesInput)
    {
        var strTemp = new StringBuilder(bytesInput.Length * 2);
        foreach (byte b in bytesInput)
        {
            strTemp.Append(b.ToString("X02"));
        }

        return strTemp.ToString();
    }
}
