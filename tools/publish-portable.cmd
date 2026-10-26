@echo off
rem Dong goi ban chay tu USB: khong can cai .NET, khong can cai dat, chep ca thu muc vao USB la chay.
rem Ket qua nam o publish\portable. File portable.txt bao ung dung ghi moi du lieu canh file .exe.
rem
rem Cac DLL native cua WPF de rieng canh file .exe (IncludeNativeLibrariesForSelfExtract=false)
rem thay vi bung ra %TEMP% moi lan chay, de rut USB ra la khong con gi tren may nguoi ban.

setlocal
cd /d "%~dp0.."

dotnet publish src\HardwareInspector.App\HardwareInspector.App.csproj ^
  -c Release -r win-x64 --self-contained true ^
  -p:PublishSingleFile=true ^
  -p:IncludeNativeLibrariesForSelfExtract=false ^
  -p:EnableCompressionInSingleFile=true ^
  -p:DebugType=none ^
  -o publish\portable
if errorlevel 1 (
  echo.
  echo Dong goi that bai.
  pause
  exit /b 1
)

echo Ban portable: du lieu va bien ban ghi canh file exe.> publish\portable\portable.txt
echo.
echo Xong. Chep toan bo thu muc publish\portable vao USB, chay HardwareInspector.exe bang quyen Administrator.
pause
