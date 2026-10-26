@echo off
rem Go driver cam bien WinRing0 (dich vu R0HardwareInspector) con sot lai sau khi app bi dung giua chung,
rem vi du bam Stop trong Visual Studio. Sau khi chay, file HardwareInspector.sys het bi khoa va xoa duoc
rem ma khong can khoi dong lai may. Can chay bang quyen Administrator.

net session >nul 2>&1
if errorlevel 1 (
  echo Can chay bang quyen Administrator: chuot phai ^> Run as administrator.
  pause
  exit /b 1
)

sc stop R0HardwareInspector
sc delete R0HardwareInspector
echo.
echo Xong. Neu van bao file dang dung, hay dong het cua so Hardware Inspector roi chay lai.
pause
