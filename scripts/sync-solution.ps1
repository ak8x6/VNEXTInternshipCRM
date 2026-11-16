param(
    [string]$SolutionName = "VNEXTInternshipCRM"
)
$ErrorActionPreference = "Stop"

Write-Host "Exporting Unmanaged & Managed solutions for $SolutionName..." -ForegroundColor Cyan
pac solution export --name $SolutionName --path "./.artifacts/${SolutionName}_unmanaged.zip" --managed false --overwrite
if ($LASTEXITCODE -ne 0) { throw "Unmanaged export failed." }

pac solution export --name $SolutionName --path "./.artifacts/${SolutionName}_managed.zip" --managed true --overwrite
if ($LASTEXITCODE -ne 0) { throw "Managed export failed." }

Write-Host "Unpacking unmanaged solution into ./solutions/$SolutionName..." -ForegroundColor Cyan
pac solution unpack --zipfile "./.artifacts/${SolutionName}_unmanaged.zip" --folder "./solutions/$SolutionName" --packagetype Unmanaged --allowDelete true --allowWrite true
if ($LASTEXITCODE -ne 0) { throw "Solution unpack failed." }

Write-Host "Sync complete!" -ForegroundColor Green
