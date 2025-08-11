# Minimal AES-256 file encryptor for frpc.exe
# Input:  frpc.exe in the current script directory
# Output: frpc.enc in the same directory, plus frpc_keys.txt with Base64 Key/IV

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# Resolve paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$inPath    = Join-Path $scriptDir "frpc.exe"
$outPath   = Join-Path $scriptDir "frpc.enc"
$keyPath   = Join-Path $scriptDir "frpc_keys.txt"

# Basic checks
if (!(Test-Path $inPath)) {
    Write-Error "frpc.exe not found at: $inPath"
}

# Generate simple random keys (AES-256 key = 32 bytes; IV = 16 bytes)
$keyBytes = New-Object byte[] 32
$ivBytes  = New-Object byte[] 16
[System.Security.Cryptography.RandomNumberGenerator]::Fill($keyBytes)
[System.Security.Cryptography.RandomNumberGenerator]::Fill($ivBytes)

$keyB64 = [Convert]::ToBase64String($keyBytes)
$ivB64  = [Convert]::ToBase64String($ivBytes)

# Create AES encryptor (CBC + PKCS7)
$aes = [System.Security.Cryptography.Aes]::Create()
$aes.Key     = $keyBytes
$aes.IV      = $ivBytes
$aes.Mode    = [System.Security.Cryptography.CipherMode]::CBC
$aes.Padding = [System.Security.Cryptography.PaddingMode]::PKCS7
$encryptor = $aes.CreateEncryptor()

# Encrypt file
$inStream  = [System.IO.File]::OpenRead($inPath)
$outStream = [System.IO.File]::Create($outPath)
$crypto    = New-Object System.Security.Cryptography.CryptoStream($outStream, $encryptor, [System.Security.Cryptography.CryptoStreamMode]::Write)

try {
    $inStream.CopyTo($crypto)
    $crypto.FlushFinalBlock()
}
finally {
    $crypto.Dispose()
    $outStream.Dispose()
    $inStream.Dispose()
    $encryptor.Dispose()
    $aes.Dispose()
}

# Save keys to a text file for later use in C#
$keyText = @"
Key (Base64): $keyB64
IV  (Base64): $ivB64
File: frpc.enc
"@
$keyText | Out-File -FilePath $keyPath -Encoding UTF8 -NoNewline

# Print to console for convenience
Write-Host "Created frpc.enc"
Write-Host "Key (Base64): $keyB64"
Write-Host "IV  (Base64): $ivB64"
Write-Host "Keys saved to: $keyPath"
