Param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadPath,

    [string]$Kid = "agent_acme",
    [string]$Secret = "agent-signing-secret"
)

if (-not (Test-Path $PayloadPath)) {
    Write-Error "Payload file '$PayloadPath' not found."
    exit 1
}

function Convert-ToBase64Url([byte[]] $bytes) {
    [Convert]::ToBase64String($bytes).TrimEnd('=') `
        .Replace('+', '-') `
        .Replace('/', '_')
}

$payloadJson = Get-Content $PayloadPath -Raw

$header = @{
    alg = "HS256"
    kid = $Kid
    typ = "JOSE"
} | ConvertTo-Json -Compress

$headerEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($payloadJson))

$signingInput = "$headerEncoded.$payloadEncoded"
$hmac = New-Object System.Security.Cryptography.HMACSHA256 ([System.Text.Encoding]::UTF8.GetBytes($Secret))
$signatureBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$signatureEncoded = Convert-ToBase64Url($signatureBytes)

$jws = "$headerEncoded.$payloadEncoded.$signatureEncoded"

Write-Host "Payload JSON (canonicalised):"
Write-Host $payloadJson
Write-Host ""
Write-Host "X-JWS-Signature header value:"
Write-Host $jws
Write-Output $jws
