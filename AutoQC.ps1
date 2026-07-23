# ==========================================
#        ITAS CONSOLE HARDWARE QC 
# ==========================================
Clear-Host
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "        HARDWARE QC REPORT              " -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Date: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
Write-Host "Computer Name: $($env:COMPUTERNAME)"

# --- 1. CORE SYSTEM & SERIALS (With OEM Fallbacks) ---
Write-Host "`n--- SYSTEM INFO ---" -ForegroundColor Yellow
$Sys = Get-CimInstance Win32_ComputerSystem
$BIOS = Get-CimInstance Win32_BIOS
$CSP = Get-CimInstance Win32_ComputerSystemProduct
$Board = Get-CimInstance Win32_BaseBoard

# Deep Model Fallback (For Acer/Asus)
$Model = $Sys.Model
if ([string]::IsNullOrWhiteSpace($Model) -or $Model -match "System Product Name" -or $Model -match "Default string") { 
    $Model = $CSP.Name
    if ([string]::IsNullOrWhiteSpace($Model)) { $Model = $Board.Product }
}

# Deep Serial Fallback
$Serial = $BIOS.SerialNumber
if ([string]::IsNullOrWhiteSpace($Serial) -or $Serial -match "Default string") { 
    $Serial = $CSP.IdentifyingNumber
}

Write-Host "Model: $Model" -ForegroundColor White
Write-Host "Serial Number: $Serial" -ForegroundColor White
Write-Host "Manufacturer: $($Sys.Manufacturer)"

# --- 2. CPU, RAM, & GPU ---
Write-Host "`n--- PROCESSING & MEMORY ---" -ForegroundColor Yellow
$CPU = Get-CimInstance Win32_Processor
Write-Host "CPU: $($CPU.Name)"
Write-Host "RAM: $([math]::Round($Sys.TotalPhysicalMemory / 1GB, 2)) GB"
$GPU = Get-CimInstance Win32_VideoController
foreach ($g in $GPU) { Write-Host "GPU: $($g.Name)" }

# --- 3. CONNECTIVITY (Wi-Fi & Bluetooth) ---
Write-Host "`n--- NETWORK & BLUETOOTH ---" -ForegroundColor Yellow
$BT = Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object { $_.Status -eq 'OK' }
if ($BT) { Write-Host "Bluetooth: PASS (Found active BT radios)" } else { Write-Host "Bluetooth: FAIL" -ForegroundColor Red }

$NetAdapters = Get-NetAdapter -Physical -ErrorAction SilentlyContinue
if ($NetAdapters) {
    foreach ($adapter in $NetAdapters) {
        Write-Host "Adapter: $($adapter.InterfaceDescription) | Status: $($adapter.Status)"
    }
}

# --- 4. STORAGE HEALTH (Hard Disk Sentinel Integration) ---
Write-Host "`n--- STORAGE HEALTH ---" -ForegroundColor Yellow

# Use absolute paths so HDSentinel doesn't get lost
$HdsPath = "$PSScriptRoot\HDS\HDSentinel.exe"
$XmlReport = "$PSScriptRoot\HDS\HDS_Report.xml"

if (Test-Path $HdsPath) {
    Write-Host "Analyzing drives with Hard Disk Sentinel..." -ForegroundColor DarkGray
    
    # Kill any stuck HDSentinel processes from previous attempts
    Stop-Process -Name "HDSentinel" -Force -ErrorAction SilentlyContinue
    
    if (Test-Path $XmlReport) { Remove-Item $XmlReport -Force -ErrorAction SilentlyContinue }
    
    # Run HDS using absolute paths
    Start-Process -FilePath $HdsPath -ArgumentList "/XML /REPORT `"$XmlReport`"" -WindowStyle Hidden
    
    # Increased timeout to 15 seconds for slower laptops
    $timeout = 15
    while (!(Test-Path $XmlReport) -and $timeout -gt 0) {
        Start-Sleep -Seconds 1
        $timeout--
    }
    
    Start-Sleep -Seconds 1
    
    Stop-Process -Name "HDSentinel" -Force -ErrorAction SilentlyContinue
    
    if (Test-Path $XmlReport) {
        [xml]$hdsData = Get-Content $XmlReport
        
        $DiskNodes = $hdsData.Hard_Disk_Sentinel.ChildNodes | Where-Object { $_.Name -match "Physical_Disk_Information_Disk_" }
        
        foreach ($Node in $DiskNodes) {
            $Model = $Node.Hard_Disk_Model_ID
            $Health = $Node.Health
            $PowerOn = $Node.Power_on_time
            $Condition = $Node.Hard_Disk_Status
            
            $HealthNum = $Health -replace '[^\d]', ''
            
            if (![string]::IsNullOrWhiteSpace($HealthNum) -and [int]$HealthNum -ge 80) {
                Write-Host "Drive: $Model | Health: $Health | Status: $Condition" -ForegroundColor Green
            } else {
                Write-Host "Drive: $Model | Health: $Health | Status: $Condition" -ForegroundColor Red
            }
            Write-Host "   -> Usage: $PowerOn" -ForegroundColor DarkGray
        }
        
        Remove-Item $XmlReport -Force -ErrorAction SilentlyContinue
    } else {
        Write-Host "Error: HDSentinel took too long to generate the report." -ForegroundColor Red
    }
} else {
    Write-Host "Error: HDSentinel.exe not found in USB\HDS folder!" -ForegroundColor Red
    
    $Disks = Get-PhysicalDisk
    foreach ($Disk in $Disks) {
        $SizeGB = [math]::Round($Disk.Size / 1GB, 2)
        Write-Host "Drive: $($Disk.FriendlyName) | Size: $SizeGB GB | Health: Unknown (HDS Missing)" -ForegroundColor DarkGray
    }
}

# --- 5. BATTERY HEALTH (Current Capacity %) ---
Write-Host "`n--- BATTERY STATUS ---" -ForegroundColor Yellow
$b_static = Get-WmiObject -Namespace root\wmi -Class BatteryStaticData -ErrorAction SilentlyContinue
$b_full = Get-WmiObject -Namespace root\wmi -Class BatteryFullChargedCapacity -ErrorAction SilentlyContinue

if ($b_static -and $b_full) {
    $design = $b_static.DesignedCapacity
    $full = $b_full.FullChargedCapacity
    if ($design -gt 0) {
        $healthPct = [math]::Round(($full / $design) * 100, 2)
        if ($healthPct -gt 100) { $healthPct = 100 } 
        
        Write-Host "Design Capacity: $($design) mWh"
        Write-Host "Current Max Capacity: $($full) mWh"
        
        if ($healthPct -ge 80) {
            Write-Host "Battery Health: $($healthPct)% (Healthy)" -ForegroundColor Green
        } else {
            Write-Host "Battery Health: $($healthPct)% (Degraded)" -ForegroundColor Red
        }
    }
} else {
    Write-Host "Battery: No battery detected or missing WMI data." -ForegroundColor DarkGray
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Launching Visual QC Dashboard..." -ForegroundColor Cyan

# --- 6. AUTO-LAUNCH HTML DASHBOARD ---
$HtmlPath = "$PSScriptRoot\Interactive_QC.html"
if (Test-Path $HtmlPath) {
    Start-Process $HtmlPath
} else {
    Write-Host "Could not find Interactive_QC.html on the USB drive!" -ForegroundColor Red
}

Write-Host "`nPress any key to close this console..." -ForegroundColor DarkGray
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
