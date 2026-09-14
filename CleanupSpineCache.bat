@echo off
chcp 65001 >nul
echo ========================================
echo 清理 spine-unity 缓存脚本
echo 请在 Unity 完全关闭后运行
echo ========================================
pause

cd /d "D:\unity\plan go\Musical Sprite"

echo [1/4] 删除 spine-unity PackageCache...
if exist "Library\PackageCache\com.esotericsoftware.spine.spine-unity@21b16484175f" (
    rmdir /s /q "Library\PackageCache\com.esotericsoftware.spine.spine-unity@21b16484175f"
    echo      已删除 spine-unity 缓存
) else (
    echo      spine-unity 缓存不存在，跳过
)

echo [2/4] 删除 spine-csharp PackageCache...
if exist "Library\PackageCache\com.esotericsoftware.spine.spine-csharp@62e1be8a5d43" (
    rmdir /s /q "Library\PackageCache\com.esotericsoftware.spine.spine-csharp@62e1be8a5d43"
    echo      已删除 spine-csharp 缓存
) else (
    echo      spine-csharp 缓存不存在，跳过
)

echo [3/4] 删除 spine urp-shaders PackageCache...
if exist "Library\PackageCache\com.esotericsoftware.spine.urp-shaders@413194061080" (
    rmdir /s /q "Library\PackageCache\com.esotericsoftware.spine.urp-shaders@413194061080"
    echo      已删除 urp-shaders 缓存
) else (
    echo      urp-shaders 缓存不存在，跳过
)

echo [4/4] 删除 packages-lock.json，强制 Unity 重新解析依赖...
if exist "Packages\packages-lock.json" (
    del /f /q "Packages\packages-lock.json"
    echo      已删除 packages-lock.json
) else (
    echo      packages-lock.json 不存在，跳过
)

echo.
echo ========================================
echo 清理完成。请重新打开 Unity。
echo 打开后 Unity 会从 GitHub 重新拉取 spine 包，请耐心等待。
echo ========================================
pause
