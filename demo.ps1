Param(
    [Parameter(Mandatory = $true)]
    [string]$PayloadPath,

    [string]$PrivateJwkPath = "./certs/agent_acme_private.jwk.json",
    [string]$Kid
)

if (-not (Test-Path $PayloadPath)) {
    Write-Error "Payload file '$PayloadPath' not found."
    exit 1
}

if (-not (Test-Path $PrivateJwkPath)) {
    Write-Error "Private JWK file '$PrivateJwkPath' not found."
    exit 1
}

function Convert-ToBase64Url([byte[]] $bytes) {
    ([Convert]::ToBase64String($bytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

function Convert-FromBase64Url([string] $value) {
    $padded = $value.Replace('-', '+').Replace('_', '/')
    switch ($padded.Length % 4) {
        2 { $padded += '==' }
        3 { $padded += '=' }
        1 { $padded += '===' }
    }
    [Convert]::FromBase64String($padded)
}

$payloadJson = Get-Content $PayloadPath -Raw

$jsonDocument = [System.Text.Json.JsonDocument]::Parse($payloadJson)
$serializerOptions = [System.Text.Json.JsonSerializerOptions]::new([System.Text.Json.JsonSerializerDefaults]::Web)
$serializerOptions.WriteIndented = $false
$canonicalJson = [System.Text.Json.JsonSerializer]::Serialize($jsonDocument.RootElement, $serializerOptions)

$jwk = Get-Content $PrivateJwkPath -Raw | ConvertFrom-Json
if (-not $Kid) {
    $Kid = $jwk.kid
}

$parameters = [System.Security.Cryptography.ECParameters]::new()
$parameters.Curve = [System.Security.Cryptography.ECCurve]::CreateFromFriendlyName("nistP256")
$parameters.Q = [System.Security.Cryptography.ECPoint]::new()
$parameters.Q.X = Convert-FromBase64Url $jwk.x
$parameters.Q.Y = Convert-FromBase64Url $jwk.y
$parameters.D = Convert-FromBase64Url $jwk.d

$header = @{
    alg = "ES256"
    kid = $Kid
    typ = "JOSE"
} | ConvertTo-Json -Compress

$headerEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($header))
$payloadEncoded = Convert-ToBase64Url([System.Text.Encoding]::UTF8.GetBytes($canonicalJson))

$signingInput = "$headerEncoded.$payloadEncoded"
$ecdsa = [System.Security.Cryptography.ECDsa]::Create()
$ecdsa.ImportParameters($parameters)
$signatureBytes = $ecdsa.SignData([System.Text.Encoding]::UTF8.GetBytes($signingInput), [System.Security.Cryptography.HashAlgorithmName]::SHA256)
$ecdsa.Dispose()
$signatureEncoded = Convert-ToBase64Url($signatureBytes)

$jws = "$headerEncoded.$payloadEncoded.$signatureEncoded"

Write-Host "Payload JSON (canonicalised):"
Write-Host $canonicalJson
Write-Host ""
Write-Host "X-JWS-Signature header value:"
Write-Host $jws
