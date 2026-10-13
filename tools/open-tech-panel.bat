@echo off
REM  TechPopups - open the out-of-game tech picker.
REM  The game must be running (the panel is served by the mod itself).
REM  If you changed the port in the mod options, edit the URL below.
set PORT=32274
echo Opening http://localhost:%PORT%/
start "" "http://localhost:%PORT%/"
timeout /t 2 >nul
