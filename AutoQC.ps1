$ReportPath = ".\$($env:COMPUTERNAME)_QC_Report.txt"
"--- HARDWARE QC REPORT ---" | Out-File $ReportPath
"Date: $(Get-Date)" | Out-File $ReportPath -Append

# 1. System Specs
"--- SYSTEM INFO ---" | Out-File $ReportPath -Append
$Sys = Get-CimInstance Win32_ComputerSystem
"Model: $($Sys.Model)" | Out-File $ReportPath -Append
"RAM: $([math]::Round($Sys.TotalPhysicalMemory / 1GB, 2)) GB" | Out-File $ReportPath -Append

# 2. SSD / Hard Drive Health
"--- STORAGE HEALTH ---" | Out-File $ReportPath -Append
Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, Size | Format-Table | Out-File $ReportPath -Append

# 3. SSD SMART Failure Prediction (Must run as Administrator)
$SMART = Get-WmiObject -namespace root\wmi -class MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue
if ($SMART) {
    foreach ($drive in $SMART) {
        "Failure Predicted: $($drive.PredictFailure) (False means healthy)" | Out-File $ReportPath -Append
    }
} else {
    "SMART Prediction: Right-click and 'Run as Administrator' to view." | Out-File $ReportPath -Append
}

Write-Host "Hardware log saved to $ReportPath" -ForegroundColor Green
Start-Sleep -Seconds 3
