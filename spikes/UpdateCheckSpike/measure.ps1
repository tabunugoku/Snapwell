param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string[]]$Rid = @('win-x64', 'win-arm64'),
    [ValidateRange(1, 1000)]
    [int]$Runs = 5,
    [string]$Url = 'https://github.com/tabunugoku/Snapwell/releases/latest/download/latest.json'
)

$ErrorActionPreference = 'Stop'

function Get-Median {
    param([double[]]$Values)
    $sorted = @($Values | Sort-Object)
    $middle = [int][Math]::Floor($sorted.Count / 2)
    if ($sorted.Count % 2 -eq 0) {
        return ($sorted[$middle - 1] + $sorted[$middle]) / 2
    }
    return $sorted[$middle]
}

$results = @()
$session = Join-Path (Join-Path $PSScriptRoot 'artifacts') ([Guid]::NewGuid().ToString('N'))
foreach ($probe in @('HttpClientProbe', 'WinHttpProbe')) {
    $project = Join-Path (Join-Path $PSScriptRoot $probe) ($probe + '.csproj')
    foreach ($runtime in $Rid) {
        $output = Join-Path (Join-Path $session $probe) $runtime
        & dotnet publish $project -c Release -r $runtime --no-restore -o $output
        if ($LASTEXITCODE -ne 0) {
            throw "publish に失敗した: $probe $runtime"
        }
        $exe = Join-Path $output ($probe + '.exe')
        $sizeKb = (Get-Item -LiteralPath $exe).Length / 1KB
        Write-Host ('{0} {1}: exe={2:F2} KB' -f $probe, $runtime, $sizeKb)

        $workingSets = @()
        $privateBytes = @()
        $elapsed = @()
        $runStatus = 'ok'
        for ($run = 0; $run -lt $Runs; $run++) {
            # stderr も保存し、通信の失敗でスクリプト全体を中断しない。
            $stderr = Join-Path $output ('stderr-' + $run + '.txt')
            $savedErrorAction = $ErrorActionPreference
            try {
                # PowerShell 5.1 は native stderr を ErrorRecord にするため、この間だけ継続する。
                $ErrorActionPreference = 'Continue'
                $lines = @(& $exe $Url 2> $stderr)
                $commandSucceeded = $?
                $exitCode = $LASTEXITCODE
            }
            catch {
                $runStatus = '実行できない (RID と実機を確認する)'
                Write-Warning $_.Exception.Message
                break
            }
            finally {
                $ErrorActionPreference = $savedErrorAction
            }
            $lines | ForEach-Object { Write-Host $_ }
            if (-not $commandSucceeded -and $exitCode -eq 0) {
                $runStatus = '実行できない (RID と実機を確認する)'
                Write-Host $runStatus
                break
            }
            if ($exitCode -ne 0) {
                $runStatus = '通信できない'
                Write-Host $runStatus
                if (Test-Path -LiteralPath $stderr) {
                    Get-Content -LiteralPath $stderr | ForEach-Object { Write-Host $_ }
                }
                break
            }
            $resultLine = $lines | Where-Object { $_ -match '^probe=' } | Select-Object -Last 1
            if ($resultLine -notmatch 'elapsed_ms=([0-9.]+) working_set=([0-9]+) private=([0-9]+)') {
                throw "測定結果を読み取れない: $probe $runtime"
            }
            $elapsed += [double]::Parse($Matches[1], [Globalization.CultureInfo]::InvariantCulture)
            $workingSets += [double]$Matches[2]
            $privateBytes += [double]$Matches[3]
        }
        $workingMedian = $null
        $privateMedian = $null
        $elapsedMedian = $null
        if ($runStatus -eq 'ok') {
            $workingMedian = Get-Median $workingSets
            $privateMedian = Get-Median $privateBytes
            $elapsedMedian = Get-Median $elapsed
        }
        $results += [pscustomobject]@{
            probe = $probe
            rid = $runtime
            exe_KB = [Math]::Round($sizeKb, 2)
            runs = $elapsed.Count
            working_set = $workingMedian
            private = $privateMedian
            elapsed_ms = $elapsedMedian
            result = $runStatus
        }
    }
}
$results | Format-Table -AutoSize
