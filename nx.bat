@ECHO OFF
SETLOCAL
SET path_to_root=%~dp0
WHERE dotnet >nul 2>nul
IF %ERRORLEVEL% NEQ 0 (ECHO nx wrapper requires the .NET SDK on PATH. & EXIT /B 1)
SET DOTNET_NX_NXDN=%path_to_root%scripts\nxdn.cmd
dotnet nxdn nx -- %*
