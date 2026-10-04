@echo off
chcp 65001 >nul
setlocal EnableDelayedExpansion
set "VER=2021.3.39f1"
set "UNITY="
rem 1) путь установки редакторов из настроек Unity Hub
set "SIP=%APPDATA%\UnityHub\secondaryInstallPath.json"
if exist "%SIP%" (
  for /f "usebackq delims=" %%P in ("%SIP%") do set "HUBDIR=%%~P"
  if defined HUBDIR if exist "!HUBDIR!\%VER%\Editor\Unity.exe" set "UNITY=!HUBDIR!\%VER%\Editor\Unity.exe"
)
rem 2) стандартные места
for %%D in ("C:\Program Files\Unity\Hub\Editor" "D:\Unity\Hub\Editor" "D:\Program Files\Unity\Hub\Editor") do (
  if not defined UNITY if exist "%%~D\%VER%\Editor\Unity.exe" set "UNITY=%%~D\%VER%\Editor\Unity.exe"
)
if not defined UNITY (
  echo Не найден Unity %VER%. Установите его через Unity Hub ^(Installs - Install Editor - Archive^)
  echo или впишите путь вручную: set "UNITY=...\%VER%\Editor\Unity.exe" в начале этого файла.
  pause
  exit /b 1
)
echo Unity: %UNITY%
set "PROJ=%~dp0"
set "PROJ=%PROJ:~0,-1%"
set "OUT=%PROJ%\Build"
rem Папку игры можно передать первым параметром: build_nepfx.bat "D:\SteamLibrary\steamapps\common\Neptunia Game Maker REvolution"
set "GAME=%PROJ%\__no_game__"
if not "%~1"=="" set "GAME=%~1\BepInEx\plugins\NepFix"
echo Сборка шейдеров NepFX... Первый запуск 5-15 минут: Unity скачивает и импортирует пакеты URP.
"%UNITY%" -batchmode -quit -nographics -projectPath "%PROJ%" -executeMethod NepFX.Build.BuildBundles -nepfxOut "%OUT%" -logFile "%PROJ%\build.log"
if errorlevel 1 (
  echo ОШИБКА сборки. Подробности в build.log
  pause
  exit /b 1
)
if exist "%GAME%" (
  copy /Y "%OUT%\nepfx.bundle" "%GAME%\nepfx.bundle" >nul && echo Готово: nepfx.bundle скопирован в папку NepFix игры.
) else (
  echo Готово: %OUT%\nepfx.bundle. Скопируйте его в BepInEx\plugins\NepFix игры.
)
pause
