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
    ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$payloadJson = Get-Content $PayloadPath -Raw

$jsonDocument = [System.Text.Json.JsonDocument]::Parse($payloadJson)
$serializerOptions = [System.Text.Json.JsonSerializerOptions]::new([System.Text.Json.JsonSerializerDefaults]::Web)
$serializerOptions.WriteIndented = $false
$canonicalJson = [System.Text.Json.JsonSerializer]::Serialize($jsonDocument.RootElement, $serializerOptions)

$header = @{
    alg = "HS256"
    kid = $Kid
    typ = "JOSE"
} | ConvertTo-Json -Compress

$headerEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($canonicalJson))

$signingInput = "$headerEncoded.$payloadEncoded"
$keyBytes = [System.Text.Encoding]::UTF8.GetBytes($Secret)
$hmac = [System.Security.Cryptography.HMACSHA256]::new($keyBytes)
$signatureBytes = $hmac.ComputeHash([System.Text.Encoding]::UTF8.GetBytes($signingInput))
$signatureEncoded = Convert-ToBase64Url($signatureBytes)

$jws = "$headerEncoded.$payloadEncoded.$signatureEncoded"

Write-Host "Payload JSON (canonicalised):"
Write-Host $canonicalJson
Write-Host ""
Write-Host "X-JWS-Signature header value:"
Write-Host $jws
