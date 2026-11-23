param([string]$CertificateThumbprint)
$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$package = Join-Path $repo '.artifacts/driver-package'
$evidence = Join-Path $repo '.artifacts/driver-build'
$sdkTools = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits/10/bin/10.0.26100.0/x64'
$signTool = Join-Path $sdkTools 'signtool.exe'
if (!(Test-Path $signTool)) { throw 'Windows SDK signtool.exe is required' }
foreach ($name in 'InputProbeHid.sys','InputProbeHid.inf') {
    if (!(Test-Path (Join-Path $package $name))) { throw "Missing package file: $name. Run Build-Package.ps1 first." }
}
# Development certificate only. Never install it in Root or TrustedPublisher.
if ($CertificateThumbprint) {
    $cert = Get-Item "Cert:/CurrentUser/My/$CertificateThumbprint"
} else {
    $cert = Get-ChildItem Cert:/CurrentUser/My | Where-Object { $_.Subject -eq 'CN=Input Probe Local Development' -and $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date).AddDays(7) } | Sort-Object NotAfter -Descending | Select-Object -First 1
    if (!$cert) {
        $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject 'CN=Input Probe Local Development' -CertStoreLocation Cert:/CurrentUser/My -KeyAlgorithm RSA -KeyLength 3072 -HashAlgorithm SHA256 -KeyExportPolicy NonExportable -NotAfter (Get-Date).AddMonths(6)
    }
}
& $signTool sign /fd SHA256 /sha1 $cert.Thumbprint /s My "$package/InputProbeHid.sys"
if ($LASTEXITCODE -ne 0) { throw 'Development SYS signing failed' }
# MakeCat builds a SHA256 catalog over the final signed SYS and INF.
$cdf = Join-Path $evidence 'InputProbeHid.cdf'
@"
[CatalogHeader]
CatalogVersion=2
HashAlgorithms=SHA256
Name=InputProbeHid.cat
ResultDir=$package
PublicVersion=0x0000001
EncodingType=0x00010001
CATATTR1=0x10010001:OSAttr:2:10.0
[CatalogFiles]
<HASH>InputProbeHid.sys=$package\InputProbeHid.sys
<HASH>InputProbeHid.inf=$package\InputProbeHid.inf
"@ | Set-Content $cdf -Encoding ASCII
$catalog = Join-Path $package 'InputProbeHid.cat'
if (Test-Path -LiteralPath $catalog) { Remove-Item -LiteralPath $catalog }
& "$sdkTools/makecat.exe" /v $cdf
if ($LASTEXITCODE -ne 0) { throw 'Catalog generation failed' }
& $signTool sign /fd SHA256 /sha1 $cert.Thumbprint /s My "$package/InputProbeHid.cat"
if ($LASTEXITCODE -ne 0) { throw 'Development catalog signing failed' }
Export-Certificate -Cert $cert -FilePath "$evidence/InputProbeDevelopment.cer" -Force | Out-Null
$rows = foreach ($name in 'InputProbeHid.sys','InputProbeHid.cat') {
    $sig = Get-AuthenticodeSignature (Join-Path $package $name)
    if (!$sig.SignerCertificate -or $sig.SignerCertificate.Thumbprint -ne $cert.Thumbprint) { throw "Unexpected signing identity: $name" }
    [pscustomobject]@{File=$name;Signer=$sig.SignerCertificate.Thumbprint;Status=$sig.Status.ToString();Message=$sig.StatusMessage}
}
$rows | ConvertTo-Json | Set-Content "$evidence/development-signatures.json"
$rows | Format-Table File,Status,Signer
Get-FileHash "$package/InputProbeHid.sys", "$package/InputProbeHid.inf", "$package/InputProbeHid.cat" -Algorithm SHA256 | Select-Object Path,Hash | ConvertTo-Json | Set-Content "$evidence/package-hashes.json"
Write-Host 'Development signatures created. Self-signed certificate is not trusted by Windows kernel policy.'
Write-Host 'No driver installed, no trusted certificate store or boot configuration changed.'

