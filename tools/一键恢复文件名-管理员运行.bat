@echo off
chcp 65001 >nul
net session >nul 2>&1
if errorlevel 1 (
  echo 需要管理员权限，正在申请提权...
  powershell -NoProfile -Command "Start-Process -Verb RunAs -FilePath '%~f0'"
  exit /b
)
echo ================ 第一步：试运行（不改动任何文件）================
"%~dp0恢复文件名.exe" -dry "C:\下载"
echo.
echo 以上是试运行结果。确认无误后按任意键执行真正的恢复...
pause >nul
echo.
echo ================ 第二步：执行恢复 ================
"%~dp0恢复文件名.exe" "C:\下载"
echo.
echo 恢复结束，请检查 C:\下载\迅雷下载 下的文件是否回来了。
pause
