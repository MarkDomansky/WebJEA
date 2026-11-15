# Certificate Renewal

WebJEA's HTTPS listener can get its certificate from one of two places. Set **exactly one** of these in the settings file, and leave the other empty:

| | `CertPath`: a PFX file | `CertSubject`: the machine store |
|---|---|---|
| Best for | ACME certificates (Let's Encrypt and others) that renew every month or two | Internal CA certificates, including AD CS autoenrollment |
| On renewal | Nothing to do. Kestrel notices the new file and switches to it without a restart | Grant the service account read on the new private key, then restart the service ([below](#store-mode-certsubject)) |
| Private key | A file in a folder you lock down | Protected by Windows in the machine key store |

`Deploy.ps1` checks the certificate before changing anything. It stops only when Kestrel couldn't serve the certificate at all: no private key, a PFX it can't open, or no Server Authentication usage. Everything else (untrusted issuer, expired, a host name not covered, expiring soon) is a warning, and the deploy goes on.

## File mode (`CertPath`)

### Where to put the PFX

Use a folder that holds nothing but this certificate, such as `C:\webjea\tls\webjea.pfx` next to the default `site`, `scripts` and `logs` folders. The folder should:

* **Hold only WebJEA's PFX.** `Deploy.ps1` grants the service account read on the whole folder, because a renewal replaces the file and the new file takes its permissions from the folder. Don't point `CertPath` into your ACME client's own working folder: those hold the ACME account key and any DNS provider credentials.
* **Be outside `SitePath`, `ScriptsPath` and `LogPath`.** Upgrades replace the site folder, script authors may be able to write to the scripts folder, and the service account can write to the log folder, which would let it replace its own certificate. `Deploy.ps1` warns if `CertPath` is inside any of them.
* **Be on a local disk**, not a network share or a folder synced by OneDrive or Dropbox.
* **Be excluded from backups**, or backed up encrypted. A copy of a PFX without a password is a copy of the key.

Lock the folder down once, before the first deploy. This turns off inheritance and gives full control to SYSTEM (which the ACME client's renewal task runs as) and Administrators only. `Deploy.ps1` then adds read for the service account:

```powershell
New-Item -ItemType Directory C:\webjea\tls
icacls C:\webjea\tls /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)(F)' '*S-1-5-32-544:(OI)(CI)(F)'
```

The SIDs are SYSTEM and Administrators, so this also works on non-English Windows. If your ACME client runs as some other account, grant that account Modify on the folder too.

`Deploy.ps1` warns when a broad group (Everyone, Users, Authenticated Users and similar) can write to the folder, or read it while the PFX has no password.

### Password or not

A PFX without a password in a folder locked down as above is as safe as one with a password. The service account can read both the file and the password, and so can administrators. A password only protects copies of the file that end up somewhere else, such as a backup.

If the PFX has a password, pass it to `Deploy.ps1` as a parameter. It is never read from the settings file:

```powershell
.\Deploy.ps1 -SettingsFile .\settings.json -CertPassword (Read-Host -AsSecureString 'PFX password')
```

`Deploy.ps1` stores it in `appsettings.Production.json` for Kestrel, and later deploys reuse it, so you only pass it again when it changes. Renewals must keep using the same password.

### simple-acme / win-acme

[simple-acme](https://simple-acme.com) (the successor to [win-acme](https://win-acme.com)) can write each renewal straight to the PFX file with its `pfxfile` store:

```powershell
wacs.exe --source manual --host webjea.example.com `
    --validation cloudflare --cloudflareapitoken <token> `
    --store pfxfile --pfxfilepath C:\webjea\tls --pfxfilename webjea
```

* **Use DNS validation.** Choose a DNS plugin with `--validation` (Cloudflare above; Azure, Route 53, a script and others exist). HTTP validation needs the ACME server to reach this server on port 80 from the internet, which internal WebJEA servers usually can't do. Kestrel also holds port 80 whenever `HttpPort` is set, so the client's own HTTP listener couldn't bind it anyway.
* **Check the file name after the first run.** By default the file is named after the certificate's common name, and `--pfxfilename` overrides that. Set `CertPath` to the file it actually writes, e.g. `C:\webjea\tls\webjea.pfx`.
* **Leave the renewal task running as SYSTEM**, the default. Don't run it as the WebJEA service account: that account runs your users' scripts, and shouldn't be able to read the ACME account key or DNS credentials, or write its own certificate.
* **Don't pass `--reuse-privatekey`.** A new key with every renewal means a leaked key is only useful until the next one.
* Option names differ slightly between simple-acme and win-acme versions. Check `wacs.exe --help` for yours.

### Posh-ACME

[Posh-ACME](https://poshac.me) keeps certificates in its own profile folder, which also holds the ACME account key, so copy the PFX into the WebJEA folder after each renewal instead of pointing `CertPath` there. Copy to a temporary name and then rename it, so Kestrel never sees a half-written file:

```powershell
$cert = Get-PACertificate 'webjea.example.com'
Copy-Item $cert.PfxFullChain 'C:\webjea\tls\webjea.pfx.new' -Force
Move-Item 'C:\webjea\tls\webjea.pfx.new' 'C:\webjea\tls\webjea.pfx' -Force
```

Run this after `Submit-Renewal` in the same scheduled task. Posh-ACME protects its PFX files with the password `poshacme` unless you set `-PfxPass`, so deploy with `-CertPassword` set to that password.

### Confirming a renewal was picked up

Kestrel watches the PFX file and reloads it when it changes, with no restart. New connections get the new certificate; connections already open keep the old one until they close. To see which certificate is being served:

```powershell
function Get-ServedThumbprint([string]$HostName, [int]$Port = 443)
{
    $tcp = [Net.Sockets.TcpClient]::new($HostName, $Port)
    try
    {
        # Accept any certificate; this only reports which one is served
        $ssl = [Net.Security.SslStream]::new($tcp.GetStream(), $false, { $true })
        $ssl.AuthenticateAsClient($HostName)
        [Security.Cryptography.X509Certificates.X509Certificate2]::new($ssl.RemoteCertificate).Thumbprint
    }
    finally { $tcp.Dispose() }
}

Get-ServedThumbprint webjea.example.com
```

Compare it with the thumbprint of the new file: `(Get-PfxData C:\webjea\tls\webjea.pfx).EndEntityCertificates.Thumbprint` (add `-Password` if it has one).

## Store mode (`CertSubject`)

Kestrel picks the certificate from `LocalMachine\My` when the service starts: among certificates whose subject contains `CertSubject` and that allow Server Authentication, it serves the one that **expires last**. See [How the certificate is selected](System-Requirements.md#how-the-certificate-is-selected).

A renewed certificate has a **new** private key file, which only SYSTEM and Administrators can read. After importing or auto-enrolling a renewal, grant the service account read on its key and restart the service:

```powershell
$subject = 'webjea.example.com'
$account = 'DOMAIN\gmsa1$'
$cert = Get-ChildItem Cert:\LocalMachine\My |
    Where-Object { $_.HasPrivateKey -and $_.Subject -like "*$subject*" } |
    Sort-Object NotAfter -Descending | Select-Object -First 1

# Find the key file behind the certificate (RSA keys; legacy CryptoAPI or CNG storage)
$rsa = [Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($cert)
$name = if ($rsa -is [Security.Cryptography.RSACng]) { $rsa.Key.UniqueName } else { $rsa.CspKeyContainerInfo.UniqueKeyContainerName }
$keyFile = "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys", "$env:ProgramData\Microsoft\Crypto\Keys" |
    ForEach-Object { Join-Path $_ $name } | Where-Object { Test-Path $_ } | Select-Object -First 1

icacls $keyFile /grant "${account}:(R)"
Restart-Service WebJEA
```

Do this **before** the service next restarts for any other reason: Kestrel switches to the renewal at that restart, and HTTPS fails to start if the service account can't read its key. `certlm.msc` → the certificate → **All Tasks** → **Manage Private Keys** does the same by hand. Re-running `.\Deploy.ps1 -SettingsFile .\settings.json -OnlySections Service` also grants it, for every certificate that matches `CertSubject`.

## Graph client certificate

In [Entra mode](entra.md), WebJEA uses a second certificate to call Microsoft Graph. It's usually self-signed, because Entra trusts the public key you uploaded to the app registration, not a CA. So it can last a year or two, and ACME doesn't apply: every renewal also needs a new upload. `Deploy.ps1` manages it when the settings file has `GraphCertSubject` or `GraphCertPath` ([settings reference](Installation.md#settings-reference)).

Either way, WebJEA loads the certificate when the service starts, so a renewal takes effect at the next restart. To renew:

1. Create the new certificate with the **same subject** ([App registration, Option A](entra-appreg.md#option-a-certificate-recommended)) and upload its `.cer` to the app registration. Keep the old one there until WebJEA has switched.
2. Put it where WebJEA reads it from:
   * **`GraphCertSubject`:** import it into `LocalMachine\My` with its private key, then re-run `.\Deploy.ps1 -SettingsFile .\settings.json -OnlySections Service` to grant the service account read on the new key. WebJEA uses the currently valid certificate with that exact subject that was issued last.
   * **`GraphCertPath`:** replace the PFX file, in a folder that follows [the same rules as `CertPath`](#where-to-put-the-pfx). `C:\webjea\tls` can hold both. If the password changed, re-run `Deploy.ps1` with `-GraphCertPassword`.
3. Restart the service, and check the log for `Entra: Graph client credential = certificate (...)`.
4. Delete the old certificate from the app registration.

`Deploy.ps1` warns when the Graph certificate expires within 30 days or isn't valid yet. Sign-in keeps working without it, but [group overage](entra.md#group-overage-users-in-more-than-200-groups), group display names and [revalidation](entra.md#near-real-time-revocation) don't.
