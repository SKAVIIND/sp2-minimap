@echo off
setlocal

set GAME=D:\steam\steamapps\common\SimplePlanes 2
set MANAGED=%GAME%\SimplePlanes 2_Data\Managed
set BEP=%GAME%\BepInEx\core

set CSC=C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild\Current\Bin\Roslyn\csc.exe

"%CSC%" /nologo /noconfig /nostdlib+ /target:library /langversion:7.3 /out:SP2Minimap.dll ^
 /r:"%MANAGED%\mscorlib.dll" ^
 /r:"%MANAGED%\System.dll" ^
 /r:"%MANAGED%\System.Core.dll" ^
 /r:"%MANAGED%\netstandard.dll" ^
 /r:"%BEP%\BepInEx.dll" ^
 /r:"%MANAGED%\UnityEngine.dll" ^
 /r:"%MANAGED%\UnityEngine.CoreModule.dll" ^
 /r:"%MANAGED%\UnityEngine.IMGUIModule.dll" ^
 /r:"%MANAGED%\UnityEngine.InputLegacyModule.dll" ^
 /r:"%MANAGED%\UnityEngine.PhysicsModule.dll" ^
 /r:"%BEP%\0Harmony.dll" ^
 /r:"%MANAGED%\Game.dll" ^
 /r:"%MANAGED%\UnityEngine.TerrainModule.dll" ^
 /r:"%MANAGED%\UnityEngine.ImageConversionModule.dll" ^
 /r:"%MANAGED%\Unity.Collections.dll" ^
 /r:"%MANAGED%\UnityEngine.TextRenderingModule.dll" ^
 SP2Minimap.cs

if errorlevel 1 (
  echo.
  echo === 编译失败 ===
) else (
  echo.
  echo === 编译成功: SP2Minimap.dll ===
  for %%F in (SP2Minimap.dll) do echo   Size: %%~zF bytes
)

pause