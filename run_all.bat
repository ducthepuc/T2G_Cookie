@echo off
rem Runs the program in quiet mode on every input_NNN.txt of a folder and writes the
rem matching output_NNN.txt files, then packs them into results.zip.
rem   usage: run_all.bat <input folder> <output folder>
setlocal enabledelayedexpansion
if "%ROBOT%"=="" set ROBOT=publish\robot.exe
if not exist "%~2" mkdir "%~2"
for %%f in ("%~1\input_*.txt") do (
    set name=%%~nf
    "%ROBOT%" -q "%%f" "%~2\output_!name:input_=!.txt" > nul
)
powershell -NoProfile -Command "Compress-Archive -Force -Path '%~2\output_*.txt' -DestinationPath results.zip"
echo done
