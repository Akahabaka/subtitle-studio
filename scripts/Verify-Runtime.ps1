param([Parameter(Mandatory=$true)][string]$Directory)
$ErrorActionPreference='Stop'
$root=(Resolve-Path -LiteralPath $Directory).Path
$runtimeFiles=Get-ChildItem -LiteralPath $root -Recurse -Filter *.dll | Where-Object {
    $_.Name -ne 'SubtitleStudio.dll' -and
    -not ([IO.Path]::GetRelativePath($root,$_.FullName).StartsWith('engines'+[IO.Path]::DirectorySeparatorChar))
}
if(-not $runtimeFiles){throw '没有找到随包运行库。请使用自包含发布。'}
$invalid=@()
foreach($file in $runtimeFiles){
    $signature=Get-AuthenticodeSignature -LiteralPath $file.FullName
    if($signature.Status -ne 'Valid'){
        $invalid += ([IO.Path]::GetRelativePath($root,$file.FullName)+': '+$signature.Status)
    }
}
if($invalid.Count){throw ('运行库完整性检查失败：'+[Environment]::NewLine+($invalid -join [Environment]::NewLine))}
Write-Output ('PASS '+$runtimeFiles.Count+' 个运行库 DLL 数字签名有效')
