@echo off
setlocal
title Codex Palette - Mode silencieux
cd /d "%~dp0"

echo [0/3] Fermeture des anciennes palettes...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$root=[Regex]::Escape((Get-Location).Path); Get-CimInstance Win32_Process | Where-Object { $_.Name -in @('node.exe','electron.exe') -and $_.CommandLine -match $root } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }"
if errorlevel 1 goto :error

echo [1/3] Fermeture de Codex...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $apps=@(Get-Process -Name ChatGPT -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*OpenAI.Codex_*' }); foreach($app in $apps){ $null=$app.CloseMainWindow() }; if($apps.Count){ Start-Sleep -Seconds 2 }; $remaining=@(Get-Process -Name ChatGPT -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*OpenAI.Codex_*' }); if($remaining.Count){ $remaining | Stop-Process -Force; Start-Sleep -Seconds 2 }; $stillRunning=@(Get-Process -Name ChatGPT -ErrorAction SilentlyContinue | Where-Object { $_.Path -like '*OpenAI.Codex_*' }); if($stillRunning.Count){ throw 'Impossible de terminer Codex.' }"
if errorlevel 1 goto :error

echo [2/3] Demarrage de Codex avec CDP local...
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -Command ^
  "$ErrorActionPreference='Stop'; $package=Get-AppxPackage OpenAI.Codex | Select-Object -First 1; if(-not $package){ throw 'Le package OpenAI.Codex est introuvable.' }; $exe=Join-Path $package.InstallLocation 'app\ChatGPT.exe'; if(-not (Test-Path $exe)){ throw 'ChatGPT.exe est introuvable.' }; $port=Get-Random -Minimum 41000 -Maximum 49000; Start-Process $exe -ArgumentList @('--remote-debugging-address=127.0.0.1',('--remote-debugging-port='+$port)); Write-Host ('Port CDP : '+$port)"
if errorlevel 1 goto :error

echo [3/3] Demarrage de la palette...
call npm run dev
goto :end

:error
echo.
echo Le lancement a echoue. Consultez le message ci-dessus.
pause
exit /b 1

:end
endlocal
