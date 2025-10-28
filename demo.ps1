PS C:\Users\dario\ConsentBridge> Write-Host "Token length: $($accessToken.Length)"  # sanity checkInvoke-RestMethod `
>>   -Uri "http://localhost:8080/v1/consent-requests" `
>>   -Method Post `
>>   -ContentType "application/json" `
>>   -Headers @{ Authorization = "Bearer $accessToken" } `
>>   -Body '{
>>     "CandidateEmail": "alice@example.com",
>>     "BoardTenantId": "mockboard_eu",
>>     "Scopes": ["apply.submit"]
>>   }'Invoke-RestMethod `
>>   -Uri "http://localhost:8080/v1/consent-requests" `
>>   -Method Post `
>>   -ContentType "application/json" `
>>   -Headers @{ Authorization = "Bearer $accessToken" } `
>>   -Body '{
>>     "CandidateEmail": "alice@example.com",
>>     "BoardTenantId": "mockboard_eu",
>>     "Scopes": ["apply.submit"]
>>   }'
Token length: 399
-Uri: 
Line |
   2 |    -Uri "http://localhost:8080/v1/consent-requests" `
     |    ~~~~
     | The term '-Uri' is not recognized as a name of a cmdlet, function, script file, or executable program.
Check the spelling of the name, or if a path was included, verify that the path is correct and try again.
PS C:\Users\dario\ConsentBridge> Invoke-RestMethod `
>>   -Uri "http://localhost:8080/v1/consent-requests" `
>>   -Method Post `
>>   -ContentType "application/json" `
>>   -Headers @{ Authorization = "Bearer $accessToken" } `
>>   -Body '{
>>     "CandidateEmail": "alice@example.com",
>>     "BoardTenantId": "mockboard_eu",
>>     "Scopes": ["apply.submit"]
>>   }'


PS C:\Users\dario\ConsentBridge> Invoke-RestMethod `
>>   -Uri "http://localhost:8080/v1/applications" `
>>   -Method Post `
>>   -ContentType "application/json" `
>>   -Headers @{
>>     Authorization    = "Bearer $accessToken"
>>     "X-JWS-Signature" = "demo.signature"
>>   } `
>>   -Body '{
>>     "ConsentToken": "ctok:5d037ba2-9fc6-48da-b6b5-2f83515a1678",
>>     "Candidate": {
>>       "Id": "cand_123",
>>       "Contact": { "Email": "alice@example.com", "Phone": "+45 1234" },
>>       "Pii": { "FirstName": "Alice", "LastName": "Larsen" },
>>       "Cv": { "Url": "https://example/cv.pdf", "Sha256": "deadbeef" }
>>     },
>>     "Job": {
>>       "ExternalId": "mock:98765",
>>       "Title": "Backend Engineer",
>>       "Company": "ACME GmbH",
>>       "ApplyEndpoint": "quick-apply"
>>     },
>>     "Materials": {
>>       "CoverLetter": { "Text": "Hello MockBoard!" },
>>       "Answers": [{ "QuestionId": "q_legal_work", "AnswerText": "Yes" }]
>>     },
>>     "Meta": { "Locale": "de-DE", "UserAgent": "agent/0.1", "Ts": "2025-10-27T10:15:00Z" }
>>   }'

id                                   status
--                                   ------
2ba00dfc-6e33-4652-a4a3-d885891eb346      2