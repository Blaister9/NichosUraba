param(
    [string]$BaseUrl = "http://localhost:8088",
    [int]$DurationSeconds = 60,
    [string]$Output = "docs/release/results/load-results.json"
)
$ErrorActionPreference = "Stop"
$scenarios = @(
    @{ Name = "directorio"; Url = "$BaseUrl/api/v1/public/businesses"; Users = 20 },
    @{ Name = "perfil"; Url = "$BaseUrl/api/v1/public/businesses/restaurante-sazon-local"; Users = 20 },
    @{ Name = "menu"; Url = "$BaseUrl/api/v1/public/businesses/restaurante-sazon-local/menu"; Users = 10 },
    @{ Name = "seguimiento"; Url = "$BaseUrl/api/v1/public/orders/demo-historical-order"; Users = 10 }
)
$results = foreach ($scenario in $scenarios) {
    $jobs = 1..$scenario.Users | ForEach-Object {
        Start-Job -ArgumentList $scenario.Url,$DurationSeconds -ScriptBlock {
            param($url,$seconds)
            Add-Type -AssemblyName System.Net.Http
            $client = [System.Net.Http.HttpClient]::new()
            $times = [Collections.Generic.List[double]]::new()
            $errors = 0
            $until = [DateTimeOffset]::UtcNow.AddSeconds($seconds)
            while ([DateTimeOffset]::UtcNow -lt $until) {
                $watch = [Diagnostics.Stopwatch]::StartNew()
                try { $response = $client.GetAsync($url).GetAwaiter().GetResult(); if (!$response.IsSuccessStatusCode) { $errors++ } }
                catch { $errors++ }
                $watch.Stop(); $times.Add($watch.Elapsed.TotalMilliseconds)
                Start-Sleep -Milliseconds 900
            }
            $client.Dispose()
            [pscustomobject]@{ Times = $times.ToArray(); Errors = $errors }
        }
    }
    $samples = $jobs | Receive-Job -Wait
    $jobs | Remove-Job
    $all = @($samples | ForEach-Object Times | Sort-Object)
    $count = $all.Count
    [pscustomobject]@{
        scenario=$scenario.Name; users=$scenario.Users; duration_seconds=$DurationSeconds
        requests=$count; errors=($samples | Measure-Object Errors -Sum).Sum
        average_ms=[math]::Round(($all | Measure-Object -Average).Average,2)
        p95_ms=[math]::Round($all[[math]::Min($count-1,[math]::Floor($count*.95))],2)
        requests_per_second=[math]::Round($count/$DurationSeconds,2)
    }
}
$directory = Split-Path $Output
if ($directory) { New-Item -ItemType Directory -Force $directory | Out-Null }
$results | ConvertTo-Json | Set-Content -Encoding utf8 $Output
$results | Format-Table
