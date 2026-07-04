$proj   = "Iziregi.Test\Iziregi.Test.csproj"
$target = "publish-installer"
$iscc   = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
$issFile = "installer\Iziregi.iss"

Write-Host "Nettoyage du dossier de publication..." -ForegroundColor Cyan
if (Test-Path $target) { Remove-Item $target -Recurse -Force }

Write-Host "Publication (Release, autonome, fichier unique) vers $target ..." -ForegroundColor Cyan
dotnet publish $proj -c Release -o "$target" --self-contained true -r win-x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR publish" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Compilation de l'installateur..." -ForegroundColor Cyan
if (-not (Test-Path $iscc)) {
    Write-Host "Inno Setup introuvable a l'emplacement attendu." -ForegroundColor Red
    Write-Host "Installe-le depuis https://jrsoftware.org/isdl.php puis relance ce script." -ForegroundColor Red
    exit 1
}

& $iscc $issFile
if ($LASTEXITCODE -ne 0) { Write-Host "ERREUR compilation installateur" -ForegroundColor Red; exit 1 }

Write-Host ""
Write-Host "Installateur pret : installer\Output\IziregiSetup.exe" -ForegroundColor Green
Write-Host "C'est ce fichier que tu envoies a ton client." -ForegroundColor Green
