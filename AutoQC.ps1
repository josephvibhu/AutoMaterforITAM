$ReportPath = ".\$($env:COMPUTERNAME)_Full_QC_Report.txt"
"========================================" | Out-File $ReportPath
"        HARDWARE QC REPORT              " | Out-File $ReportPath
"========================================" | Out-File $ReportPath
"Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $ReportPath -Append
"Computer Name: $($env:COMPUTERNAME)" | Out-File $ReportPath -Append

# --- 1. CORE SYSTEM & SERIALS (For Snipe-IT) ---
" `n--- SYSTEM INFO ---" | Out-File $ReportPath -Append
$Sys = Get-CimInstance Win32_ComputerSystem
$BIOS = Get-CimInstance Win32_BIOS
"Model: $($Sys.Model)" | Out-File $ReportPath -Append
"Serial Number: $($BIOS.SerialNumber)" | Out-File $ReportPath -Append
"Manufacturer: $($Sys.Manufacturer)" | Out-File $ReportPath -Append

# --- 2. CPU, RAM, & GPU (xsukax features) ---
" `n--- PROCESSING & MEMORY ---" | Out-File $ReportPath -Append
$CPU = Get-CimInstance Win32_Processor
"CPU: $($CPU.Name)" | Out-File $ReportPath -Append
"RAM: $([math]::Round($Sys.TotalPhysicalMemory / 1GB, 2)) GB" | Out-File $ReportPath -Append
$GPU = Get-CimInstance Win32_VideoController
foreach ($g in $GPU) { "GPU: $($g.Name)" | Out-File $ReportPath -Append }

# --- 3. CONNECTIVITY (Wi-Fi & Bluetooth) ---
" `n--- NETWORK & BLUETOOTH ---" | Out-File $ReportPath -Append
# Check Bluetooth via Plug and Play devices
$BT = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' }
if ($BT) {
    "Bluetooth: PASS (Found $($BT.Count) active BT radios)" | Out-File $ReportPath -Append
} else {
    "Bluetooth: FAIL (Missing or Driver Error)" | Out-File $ReportPath -Append
}

# Check Physical Network Adapters (Wi-Fi / Ethernet)
$NetAdapters = Get-NetAdapter -Physical -ErrorAction SilentlyContinue
if ($NetAdapters) {
    foreach ($adapter in $NetAdapters) {
        "Adapter: $($adapter.InterfaceDescription) | Status: $($adapter.Status)" | Out-File $ReportPath -Append
    }
} else {
    "Network: FAIL (No physical adapters found)" | Out-File $ReportPath -Append
}

# --- 4. STORAGE & HEALTH ---
" `n--- STORAGE HEALTH ---" | Out-File $ReportPath -Append
Get-PhysicalDisk | Select-Object FriendlyName, MediaType, HealthStatus, @{Name="Size(GB)";Expression={[math]::Round($_.Size/1GB, 2)}} | Format-Table -AutoSize | Out-File $ReportPath -Append

$SMART = Get-WmiObject -namespace root\wmi -class MSStorageDriver_FailurePredictStatus -ErrorAction SilentlyContinue
if ($SMART) {
    foreach ($drive in $SMART) {
        $status = if ($drive.PredictFailure) { "FAIL PENDING" } else { "PASS (Healthy)" }
        "SMART Health: $status" | Out-File $ReportPath -Append
    }
} else {
    "SMART Health: Must run script as Administrator to view." | Out-File $ReportPath -Append
}

# --- 5. BATTERY HEALTH (Calculates Wear Level) ---
" `n--- BATTERY STATUS ---" | Out-File $ReportPath -Append
$b_static = Get-WmiObject -Namespace root\wmi -Class BatteryStaticData -ErrorAction SilentlyContinue
$b_full = Get-WmiObject -Namespace root\wmi -Class BatteryFullChargedCapacity -ErrorAction SilentlyContinue

if ($b_static -and $b_full) {
    $design = $b_static.DesignedCapacity
    $full = $b_full.FullChargedCapacity
    if ($design -gt 0) {
        $wear = [math]::Round((($design - $full) / $design) * 100, 2)
        if ($wear -lt 0) { $wear = 0 } # Fixes anomalies if battery reads slightly over design capacity
        
        "Design Capacity: $($design) mWh" | Out-File $ReportPath -Append
        "Current Max Capacity: $($full) mWh" | Out-File $ReportPath -Append
        "Battery Wear Level: $($wear)% (Higher is worse)" | Out-File $ReportPath -Append
    }
} else {
    "Battery: No battery detected or missing WMI data." | Out-File $ReportPath -Append
}

Write-Host "Full Diagnostic log saved to $ReportPath" -ForegroundColor Green
Start-Sleep -Seconds 4
