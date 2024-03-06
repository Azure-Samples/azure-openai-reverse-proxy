$env_file = ".env"

if (-not (Test-Path $env_file)) {
    Write-Host ".env file not found"
    exit 1
} else {
    Get-Content $env_file | foreach {
        $name, $value = $_.Split('=')
        Set-Content env:\$name $value
    }
}

if ([string]::IsNullOrEmpty($env:AZURE_OPENAI_API_KEY)) {
    Write-Host "OpenAI key is missing - `$AZURE_OPENAI_API_KEY must be set"
    exit 1
}

$url = "http://127.0.0.1:8080/chat/completions?api-version=2023-05-15"

while ($true) {
    Get-Date -Format "HH:mm:ss"

    $response = Invoke-RestMethod -Uri $url -Method Post -ContentType "application/json" -Headers @{"api-key" = $env:AZURE_OPENAI_API_KEY} -Body '{"messages": [{"role": "user", "content": "How to make cookies?"}]}'

    $completion = $response.choices[0].message.content

    Write-Host "$completion`n"
}