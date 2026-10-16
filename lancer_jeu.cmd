@echo off
rem Lance Fais ton meme puis ouvre le navigateur sur http://localhost:5180
cd /d "%~dp0"
echo Demarrage de Fais ton meme sur http://localhost:5180 (fermez cette fenetre pour l'arreter)...
echo Pour jouer a plusieurs sur ce PC, ouvrez un onglet par joueur.
start "" /b cmd /c ""%SystemRoot%\System32\timeout.exe" /t 6 /nobreak >nul & start http://localhost:5180"
dotnet run --project "%~dp0FaisTonMeme.Web\FaisTonMeme.Web.csproj"
pause
