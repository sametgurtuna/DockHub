# Code signing

Unsigned installers work, but Windows SmartScreen shows "Windows protected your PC" until a download has built up
reputation, and some antivirus products are stricter with unsigned apps. The release workflow
([`.github/workflows/release.yml`](../../.github/workflows/release.yml)) can sign `DockHub.exe`, `DockHub.dll` and the
installer with [Azure Artifact Signing](https://azure.microsoft.com/en-us/products/artifact-signing) (called Azure
Trusted Signing before 2026), through the [`azure/artifact-signing-action`](https://github.com/Azure/artifact-signing-action).

Signing is optional. Without the settings below the workflow builds the release as before, unsigned, and prints a
warning.

## Who can use Artifact Signing

Public Trust certificates, the kind that SmartScreen trusts, need an identity validation by Microsoft:

- **Organizations** in the United States, Canada, the European Union, the United Kingdom, Australia, New Zealand, Japan,
  South Korea, Singapore, Switzerland, Norway and Israel.
- **Individual developers** in the United States and Canada only.

The list changes, so check the [quickstart](https://learn.microsoft.com/en-us/azure/artifact-signing/quickstart) first.
If you are not eligible, see [Other options](#other-options).

## Set up Artifact Signing

1. **Azure subscription.** In the Azure portal, register the `Microsoft.CodeSigning` resource provider
   (*Subscription › Resource providers*).
2. **Artifact Signing account.** Create one in a region near you. Note its endpoint, for example
   `https://weu.codesigning.azure.net` (West Europe) or `https://eus.codesigning.azure.net` (East US).
3. **Identity validation.** In the account, open *Identity validation* and start a Public Trust request, for an
   organization or as an individual. You need the *Artifact Signing Identity Verifier* role for this. It is done in the
   portal only and can take a few days.
4. **Certificate profile.** When the validation is complete, create a *Public Trust* certificate profile.
5. **App registration for GitHub.** In Microsoft Entra ID, create an app registration (for example `DockHub release`)
   and a client secret for it. On the Artifact Signing account (or the certificate profile), give the app the
   *Artifact Signing Certificate Profile Signer* role.
6. **Repository settings.** In GitHub, *Settings › Secrets and variables › Actions*:

   | Kind | Name | Value |
   |---|---|---|
   | Secret | `AZURE_TENANT_ID` | Directory (tenant) ID of the app registration |
   | Secret | `AZURE_CLIENT_ID` | Application (client) ID |
   | Secret | `AZURE_CLIENT_SECRET` | The client secret |
   | Variable | `SIGNING_ENDPOINT` | The account endpoint from step 2 |
   | Variable | `SIGNING_ACCOUNT` | The Artifact Signing account name |
   | Variable | `SIGNING_PROFILE` | The certificate profile name |

   The workflow signs when `AZURE_CLIENT_ID` and `SIGNING_ACCOUNT` are both set.

7. **Test.** Push a tag (for example `v0.9.0`). The *Verify signatures* step fails the run if any of the three files
   isn't signed. On Windows, *Properties › Digital Signatures* of the installer shows the certificate.

Client secrets expire (the portal allows at most two years), so create a new one and update `AZURE_CLIENT_SECRET` before that. The identity
validation has to be renewed too; Azure sends a reminder.

## What gets signed

| File | When |
|---|---|
| `DockHub.exe`, `DockHub.dll` (x64 and ARM64) | After `dotnet publish`, before the installers pack them (`installer/build.ps1 -Stage Publish`) |
| `DockHub-Setup-<version>-x64.exe`, `DockHub-Setup-<version>-arm64.exe` | After the installers are built (`-Stage Installer`), before the checksums |

The Inno Setup uninstaller (`unins000.exe`) is not signed. Signing it would need Inno Setup's `SignTool` directive with
a signing command that runs during the build.

## Other options

- **[SignPath Foundation](https://signpath.org)** signs open-source projects for free. It needs an OSI-approved license,
  and DockHub has no license file yet.
- **A certificate on a cloud HSM from a commercial certificate authority**, for example SSL.com eSigner, which has its
  own GitHub Action. Since 2023, code signing keys must be kept on hardware, so a plain `.pfx` file in a secret is no
  longer possible. Replace the two *Sign* steps in `release.yml` with that provider's action; the *Verify signatures*
  step works for any provider.
- **No signing.** The release still works; SmartScreen warnings fade as the download builds reputation.
