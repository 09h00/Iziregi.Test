# release-client.ps1
# Usage : .\release-client.ps1 -Version "1.0.2"
#
# Publie une nouvelle version du client Iziregi (Iziregi.Test) de bout en
# bout, EN UNE SEULE COMMANDE :
#   1. Met a jour le numero de version dans Iziregi.Test.csproj ET dans
#      Installer\Iziregi.iss (les deux sont toujours modifies ensemble,
#      ils ne peuvent plus se desynchroniser).
#   2. Recompile le client (publish self-contained, plusieurs fichiers -- pas
#      en "fichier unique" : ce mode ajoutait plusieurs secondes de decompression
#      a chaque premier demarrage apres une mise a jour).
#   3. Regenere l'installateur avec Inno Setup.
#   4. Envoie le nouvel installateur sur le serveur.
#   5. Calcule le hash SHA-256 de l'installateur et le publie
#      (latest-version.sha256) -- verifie par le client avant d'executer
#      l'installateur telecharge (revue de securite du 17.07.2026).
#   6. Met a jour latest-version.txt sur le serveur EN DERNIER, uniquement
#      si tout ce qui precede a reussi.
#
# C'est ce dernier point qui corrige le bug rencontre le 4 juillet 2026 :
# le serveur annoncait "1.0.2" (latest-version.txt modifie a la main) alors
# qu'aucun installateur "1.0.2" correspondant n'avait ete construit ni
# envoye -> tous les clients bouclaient en proposant une "mise a jour" qui
# ne changeait jamais rien. Avec ce script, latest-version.txt n'est modifie
# QUE si le build, l'envoi de l'installateur ET la publication du hash ont
# reellement reussi avant.
#
# Prerequis : Inno Setup 6 installe, acces SSH/SCP configure vers le
# serveur (le meme que celui utilise par deploy.ps1 / publish-installer.ps1).

param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    Write-Host "ERREUR : la version doit etre au format X.Y.Z (ex: 1.0.2)" -ForegroundColor Red
    exit 1
}

$testProjDir = $PSScriptRoot
$csproj      = Join-Path $testProjDir "Iziregi.Test\Iziregi.Test.csproj"
$issFile     = Join-Path $testProjDir "Installer\Iziregi.iss"
$server      = "ubuntu@179.237.69.222"

try {
    Write-Host "1/7 - Mise a jour du numero de version ($Version) dans le .csproj et le .iss..." -ForegroundColor Cyan

    if (-not (Test-Path $csproj))  { throw "Introuvable : $csproj" }
    if (-not (Test-Path $issFile)) { throw "Introuvable : $issFile" }

    $csprojContent = Get-Content $csproj -Raw -Encoding UTF8
    if ($csprojContent -notmatch '<Version>[\d\.]+</Version>') { throw "Balise <Version> introuvable dans $csproj" }
    $csprojContent = $csprojContent -replace '<Version>[\d\.]+</Version>', "<Version>$Version</Version>"
    [System.IO.File]::WriteAllText($csproj, $csprojContent, [System.Text.Encoding]::UTF8)

    $issContent = Get-Content $issFile -Raw -Encoding UTF8
    if ($issContent -notmatch '#define MyAppVersion "[\d\.]+"') { throw "MyAppVersion introuvable dans $issFile" }
    $issContent = $issContent -replace '#define MyAppVersion "[\d\.]+"', "#define MyAppVersion `"$Version`""
    [System.IO.File]::WriteAllText($issFile, $issContent, [System.Text.Encoding]::UTF8)

    Write-Host "2/7 - Publication (Release, autonome, plusieurs fichiers)..." -ForegroundColor Cyan
    Push-Location $testProjDir
    try {
        if (Test-Path "publish-installer") { Remove-Item "publish-installer" -Recurse -Force }

        # ✅ Volontairement SANS -p:PublishSingleFile=true : le mode "fichier unique"
        # doit se decompresser dans un dossier temporaire a chaque premier demarrage
        # apres une mise a jour, ce qui ajoutait plusieurs secondes d'attente
        # (l'utilisateur ne voyait rien pendant ce temps). En mode multi-fichiers
        # classique, l'app demarre directement, sans etape de decompression.
        dotnet publish "Iziregi.Test\Iziregi.Test.csproj" -c Release -o "publish-installer" `
            --self-contained true -r win-x64
        if ($LASTEXITCODE -ne 0) { throw "Echec dotnet publish" }

        Write-Host "3/7 - Compilation de l'installateur..." -ForegroundColor Cyan
        $iscc = "C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
        if (-not (Test-Path $iscc)) { throw "Inno Setup introuvable a l'emplacement attendu : $iscc" }

        & $iscc "installer\Iziregi.iss"
        if ($LASTEXITCODE -ne 0) { throw "Echec compilation installateur" }
    }
    finally {
        Pop-Location
    }

    $exeLocal = Join-Path $testProjDir "installer\Output\IziregiSetup.exe"
    if (-not (Test-Path $exeLocal)) { throw "Installateur introuvable apres compilation : $exeLocal" }

    Write-Host "4/7 - Envoi de l'installateur vers le serveur..." -ForegroundColor Cyan
    scp $exeLocal "${server}:/tmp/IziregiSetup.exe"
    if ($LASTEXITCODE -ne 0) { throw "Echec scp de l'installateur" }

    Write-Host "5/7 - Installation sur le serveur (emplacement definitif + permissions)..." -ForegroundColor Cyan
    ssh $server "sudo mv /tmp/IziregiSetup.exe /opt/iziregi/downloads/IziregiSetup.exe && sudo chmod 644 /opt/iziregi/downloads/IziregiSetup.exe && echo OK"
    if ($LASTEXITCODE -ne 0) { throw "Echec installation de l'installateur sur le serveur" }

    Write-Host "6/7 - Publication du hash SHA-256 (verification d'integrite cote client)..." -ForegroundColor Cyan
    $sha256 = (Get-FileHash -Path $exeLocal -Algorithm SHA256).Hash
    ssh $server "echo '$sha256' | sudo tee /opt/iziregi/latest-version.sha256 > /dev/null && sudo chmod 644 /opt/iziregi/latest-version.sha256 && echo OK"
    if ($LASTEXITCODE -ne 0) { throw "Echec publication du hash SHA-256" }

    Write-Host "7/7 - Mise a jour de latest-version.txt (annonce la nouvelle version aux clients)..." -ForegroundColor Cyan
    ssh $server "echo '$Version' | sudo tee /opt/iziregi/latest-version.txt > /dev/null && echo OK"
    if ($LASTEXITCODE -ne 0) { throw "Echec mise a jour de latest-version.txt" }

    Write-Host ""
    Write-Host "Version $Version publiee avec succes." -ForegroundColor Green
    Write-Host "Le .csproj, le .iss, l'installateur sur le serveur, le hash SHA-256 et latest-version.txt sont maintenant tous coherents entre eux." -ForegroundColor Green
    Write-Host "Chaque client Iziregi proposera automatiquement cette mise a jour a son prochain demarrage." -ForegroundColor Green
}
catch {
    Write-Host ""
    Write-Host "ECHEC : $($_.Exception.Message)" -ForegroundColor Red
    Write-Host "latest-version.txt n'a PAS ete modifie sur le serveur (seule la derniere etape le fait, uniquement si tout le reste a reussi) -- aucun client ne sera donc perturbe par cette tentative." -ForegroundColor Yellow
    exit 1
}
