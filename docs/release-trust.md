# Release Trust And Windows Warnings

Mangosteen Image Viewer is currently distributed as unsigned Windows builds while the project builds public reputation and prepares a realistic code-signing path.

That is common for small open-source Windows projects, but it means Windows may show SmartScreen warnings or, on systems with Smart App Control enabled, block an installer or one of the bundled native DLLs.

## What We Publish

Every GitHub Release should include:

- `Mangosteen-Setup-<version>-x64.exe`
- `Mangosteen-Portable-<version>-x64.zip`
- `SHA256SUMS.txt`
- release notes describing what changed

The release workflow builds these artifacts from the repository source on GitHub Actions. Installer and portable binaries are release assets, not committed files.

## Verify A Download

Download `SHA256SUMS.txt` from the same release as the installer or portable zip, then compare the hash locally:

```powershell
Get-FileHash .\Mangosteen-Setup-0.2.4-x64.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
```

The hash printed by `Get-FileHash` should match the line for the file you downloaded.

## If Windows Blocks A Release

SmartScreen and Smart App Control are different checks:

- SmartScreen often warns because an unsigned file is new or not commonly downloaded.
- Smart App Control can block unsigned or untrusted apps with no "run anyway" button.

For now, the maintainer path is:

1. Confirm the release artifacts were produced by the GitHub Actions release workflow.
2. Verify the SHA256 hashes in `SHA256SUMS.txt`.
3. Submit the blocked file to Microsoft Security Intelligence at https://www.microsoft.com/en-us/wdsi/filesubmission.
4. Choose the software-developer / false-positive path when available.
5. Include the GitHub Release URL, the exact version, the SHA256 hash, and a short note that Mangosteen is an MIT-licensed, open-source, privacy-respecting image viewer with no telemetry.

If Windows names a bundled DLL, submit the installer or portable zip and mention the DLL name shown in the Windows Security dialog.

## Longer-Term Fix

The proper long-term fix is Authenticode signing for the installer and shipped executable files, ideally through a service such as SignPath Foundation once the project qualifies.
