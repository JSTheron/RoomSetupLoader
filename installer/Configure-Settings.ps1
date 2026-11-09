param(
    [Parameter(Mandatory=$true)][ValidateSet('Read','Validate','Write')][string]$Mode,
    [Parameter(Mandatory=$true)][string]$IniPath,
    [string]$ConfigPath
)
$ErrorActionPreference = 'Stop'
try {
    if ($Mode -eq 'Read') {
        if (-not (Test-Path -LiteralPath $ConfigPath)) { exit 0 }
        $config = Get-Content -LiteralPath $ConfigPath -Raw -Encoding UTF8 | ConvertFrom-Json
        $entries = [ordered]@{
            AtemIpAddress = [string]$config.AtemIpAddress
            AtemConnectionAttempts = $(if ($null -ne $config.AtemConnectionAttempts) { [string]$config.AtemConnectionAttempts } else { '3' })
            AtemConnectionRetryDelaySeconds = $(if ($null -ne $config.AtemConnectionRetryDelaySeconds) { [string]$config.AtemConnectionRetryDelaySeconds } else { '5' })
            ImageFolder = [string]$config.ImageFolder
            ImagePrefix = [string]$config.ImagePrefix
            Width = [string]$config.Width
            Height = [string]$config.Height
            MaxIndividualLogSizeMB = $(if ($null -ne $config.MaxIndividualLogSizeMB) { [string]$config.MaxIndividualLogSizeMB } else { '10' })
            LogCleanupIntervalHours = $(if ($null -ne $config.LogCleanupIntervalHours) { [string]$config.LogCleanupIntervalHours } else { '24' })
            MaxLogFolderSizeMB = $(if ($null -ne $config.MaxLogFolderSizeMB) { [string]$config.MaxLogFolderSizeMB } else { '1024' })
            LogRetentionDays = $(if ($null -ne $config.LogRetentionDays) { [string]$config.LogRetentionDays } else { '365' })
        }
        # Values containing newlines cannot be represented in the temporary INI file.
        foreach ($value in $entries.Values) {
            if ($value -match '[\r\n]') { throw 'Existing configuration contains a newline in a field.' }
        }
        $lines = @('[Settings]') + @($entries.GetEnumerator() | ForEach-Object { '{0}={1}' -f $_.Key,$_.Value })
        [System.IO.File]::WriteAllLines($IniPath, [string[]]$lines, (New-Object System.Text.UTF8Encoding($false)))
        exit 0
    }

    $settings = @{}
    foreach ($line in [System.IO.File]::ReadAllLines($IniPath)) {
        if ($line -match '^([^=]+)=(.*)$') { $settings[$matches[1]] = $matches[2] }
    }
    foreach ($key in @('AtemIpAddress','AtemConnectionAttempts','AtemConnectionRetryDelaySeconds','ImageFolder','ImagePrefix','Width','Height','LogRetentionDays','MaxLogFolderSizeMB','LogCleanupIntervalHours','MaxIndividualLogSizeMB')) {
        if (-not $settings.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($settings[$key])) { throw "$key must not be empty." }
    }
    $ip = $null
    if (-not [System.Net.IPAddress]::TryParse($settings.AtemIpAddress, [ref]$ip) -or
        $ip.AddressFamily -ne [System.Net.Sockets.AddressFamily]::InterNetwork -or
        $settings.AtemIpAddress -notmatch '^(\d{1,3}\.){3}\d{1,3}$') { throw 'ATEM IP address must be a valid IPv4 address.' }
    [int]$AtemConnectionAttempts = 0
    if (-not [int]::TryParse($settings.AtemConnectionAttempts, [ref]$AtemConnectionAttempts) -or $AtemConnectionAttempts -lt 1 -or $AtemConnectionAttempts -gt 20) { throw 'AtemConnectionAttempts must be between 1 and 20.' }
    [int]$AtemConnectionRetryDelaySeconds = 0
    if (-not [int]::TryParse($settings.AtemConnectionRetryDelaySeconds, [ref]$AtemConnectionRetryDelaySeconds) -or $AtemConnectionRetryDelaySeconds -lt 0 -or $AtemConnectionRetryDelaySeconds -gt 300) { throw 'AtemConnectionRetryDelaySeconds must be between 0 and 300.' }
    [int]$width = 0; [int]$height = 0
    if (-not [int]::TryParse($settings.Width, [ref]$width) -or $width -le 0 -or $width -gt 16384) { throw 'Width must be between 1 and 16384.' }
    if (-not [int]::TryParse($settings.Height, [ref]$height) -or $height -le 0 -or $height -gt 16384) { throw 'Height must be between 1 and 16384.' }
    [int]$LogRetentionDays = 0
    if (-not [int]::TryParse($settings.LogRetentionDays, [ref]$LogRetentionDays) -or $LogRetentionDays -lt 0) { throw 'LogRetentionDays must be a non-negative integer (0 disables this setting).' }
    [int]$MaxLogFolderSizeMB = 0
    if (-not [int]::TryParse($settings.MaxLogFolderSizeMB, [ref]$MaxLogFolderSizeMB) -or $MaxLogFolderSizeMB -lt 0) { throw 'MaxLogFolderSizeMB must be a non-negative integer (0 disables this setting).' }
    [int]$LogCleanupIntervalHours = 0
    if (-not [int]::TryParse($settings.LogCleanupIntervalHours, [ref]$LogCleanupIntervalHours) -or $LogCleanupIntervalHours -lt 0) { throw 'LogCleanupIntervalHours must be a non-negative integer (0 disables this setting).' }
    [int]$MaxIndividualLogSizeMB = 0
    if (-not [int]::TryParse($settings.MaxIndividualLogSizeMB, [ref]$MaxIndividualLogSizeMB) -or $MaxIndividualLogSizeMB -lt 0) { throw 'MaxIndividualLogSizeMB must be a non-negative integer (0 disables this setting).' }
    if ($settings.ImageFolder -match '[\x00-\x1f]') { throw 'Image folder contains invalid control characters.' }
    if ($settings.ImagePrefix -match '[\\/:*?"<>|\x00-\x1f]') { throw 'Image prefix contains characters not allowed in Windows filenames.' }
    if ($Mode -eq 'Write') {
        $config = [ordered]@{ AtemIpAddress=$settings.AtemIpAddress; AtemConnectionAttempts=$AtemConnectionAttempts; AtemConnectionRetryDelaySeconds=$AtemConnectionRetryDelaySeconds; ImageFolder=$settings.ImageFolder; ImagePrefix=$settings.ImagePrefix; Width=$width; Height=$height; LogRetentionDays=$LogRetentionDays; MaxLogFolderSizeMB=$MaxLogFolderSizeMB; LogCleanupIntervalHours=$LogCleanupIntervalHours; MaxIndividualLogSizeMB=$MaxIndividualLogSizeMB }
        $dir = Split-Path -Parent $ConfigPath
        [System.IO.Directory]::CreateDirectory($dir) | Out-Null
        $tmp = Join-Path $dir ('config.' + [guid]::NewGuid().ToString('N') + '.tmp')
        try {
            [System.IO.File]::WriteAllText($tmp, ($config | ConvertTo-Json -Depth 4), (New-Object System.Text.UTF8Encoding($false)))
            Move-Item -LiteralPath $tmp -Destination $ConfigPath -Force
        } finally { if (Test-Path -LiteralPath $tmp) { Remove-Item -LiteralPath $tmp -Force } }
    }
    exit 0
} catch {
    [Console]::Error.WriteLine($_.Exception.Message)
    exit 1
}
