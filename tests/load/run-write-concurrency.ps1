param([string]$BaseUrl = "http://localhost:8088", [int]$PerFlow = 5)
$ErrorActionPreference = "Stop"
$slot = (Invoke-RestMethod "$BaseUrl/api/v1/public/businesses/restaurante-sazon-local/pickup-slots").slots[0].start
$jobs = @()
1..$PerFlow | ForEach-Object {
    $body = @{ alias = "CargaQ$_" } | ConvertTo-Json
    $jobs += Start-Job -ArgumentList "$BaseUrl/api/v1/public/businesses/barberia-el-corte/queue/tickets",$body -ScriptBlock {
        param($url,$json)
        try { (Invoke-WebRequest -UseBasicParsing -Method Post -Uri $url -ContentType "application/json" -Body $json).StatusCode }
        catch { [int]$_.Exception.Response.StatusCode }
    }
}
1..$PerFlow | ForEach-Object {
    $body = @{ customerAlias="CargaO$_"; phone="3001234567"; pickupStart=$slot;
        consentAccepted=$true; consentNoticeVersion="pilot-1";
        lines=@(@{ productId="70000000-0000-0000-0000-000000000001"; quantity=1 }) } |
        ConvertTo-Json -Depth 5
    $jobs += Start-Job -ArgumentList "$BaseUrl/api/v1/public/businesses/restaurante-sazon-local/orders",$body -ScriptBlock {
        param($url,$json)
        try { (Invoke-WebRequest -UseBasicParsing -Method Post -Uri $url -ContentType "application/json" -Body $json).StatusCode }
        catch { [int]$_.Exception.Response.StatusCode }
    }
}
$statuses = @($jobs | Receive-Job -Wait)
$jobs | Remove-Job
$statuses | Group-Object | Select-Object Name,Count
if ($statuses.Count -ne 2*$PerFlow -or @($statuses | Where-Object { $_ -ne 201 }).Count -gt 0) { exit 1 }
