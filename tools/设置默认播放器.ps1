$ErrorActionPreference = 'Stop'
$xmp = 'C:\A-zhangchen\A-zhangchen\web工具\迅雷影音\Xmp.exe'
$progId = 'Applications\Xmp.exe'
$exts = '.mp4','.mkv','.avi','.mov','.wmv','.flv','.f4v','.ts','.m2ts','.m4v','.rmvb','.rm','.webm','.mpg','.mpeg','.3gp','.vob','.asf'
$log = 'C:\A-zhangchen\Codex-WorkSpace\InstantLock\backup\设置关联.log'
$cu = [Microsoft.Win32.Registry]::CurrentUser
"=== 设置迅雷影音为默认播放器 $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" | Set-Content $log -Encoding utf8

# 1) 重建 Applications\Xmp.exe
$app = $cu.CreateSubKey("Software\Classes\$progId")
$app.SetValue('', '迅雷影音')
$app.SetValue('FriendlyAppName', '迅雷影音')
$cmd = $cu.CreateSubKey("Software\Classes\$progId\shell\open\command")
$cmd.SetValue('', '"' + $xmp + '" "%1"')
$ico = $cu.CreateSubKey("Software\Classes\$progId\DefaultIcon")
$ico.SetValue('', $xmp + ',0')
$sup = $cu.CreateSubKey("Software\Classes\$progId\SupportedTypes")
foreach ($e in $exts) { $sup.SetValue($e, '') }
Add-Content $log "打开命令 = `"$xmp`" `"%1`"" -Encoding utf8

# 2) 每个扩展：默认 ProgID + OpenWithProgids，并清除受保护的 UserChoice
foreach ($e in $exts) {
    $old = ''
    try { $uc0 = $cu.OpenSubKey("Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$e\UserChoice"); if ($uc0) { $old = [string]$uc0.GetValue('ProgId') } } catch { }
    $extKey = $cu.CreateSubKey("Software\Classes\$e")
    $extKey.SetValue('', $progId)
    $owp = $cu.CreateSubKey("Software\Classes\$e\OpenWithProgids")
    $owp.SetValue($progId, (New-Object byte[] 0), [Microsoft.Win32.RegistryValueKind]::Binary)
    $state = 'UserChoice 已清除'
    $ucPath = "Software\Microsoft\Windows\CurrentVersion\Explorer\FileExts\$e"
    try {
        $uc = $cu.OpenSubKey($ucPath, $true)
        if ($uc) { $uc.DeleteSubKeyTree('UserChoice', $false); $uc.Close() }
    } catch { $state = 'UserChoice 保留: ' + $_.Exception.Message }
    Add-Content $log ("{0,-7} 原={1,-26} {2}" -f $e, $(if ($old) { $old } else { '-' }), $state) -Encoding utf8
}
Add-Content $log '完成' -Encoding utf8
