@echo off
rem Lance Memetropole puis ouvre le navigateur sur http://localhost:5180
cd /d "%~dp0"
echo Demarrage de Memetropole sur http://localhost:5180 (fermez cette fenetre pour l'arreter)...
echo Pour jouer a plusieurs sur ce PC, ouvrez un onglet par joueur.
start "" /b cmd /c ""%SystemRoot%\System32\timeout.exe" /t 6 /nobreak >nul & start http://localhost:5180"
dotnet run --project "%~dp0Memetropole.Web\Memetropole.Web.csproj"
pause
