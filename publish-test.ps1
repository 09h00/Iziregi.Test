$proj   = "Iziregi.Test\Iziregi.Test.csproj"
$target = "$env:LOCALAPPDATA\Iziregi"

Write-Host "Fermeture de l'app si elle tourne encore..." -ForegroundColor Cyan
Get-Process -Name "Iziregi.Test" -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 1

Write-Host "Publication (Release) vers $target ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o "$target"
if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR publish" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Publication terminee. Tu peux relancer l'app via l'icone Iziregi." -ForegroundColor Green
