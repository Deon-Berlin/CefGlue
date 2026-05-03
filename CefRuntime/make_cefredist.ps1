function Get-WslPath {
	param([string]$WindowsPath)
	$WindowsPath = $WindowsPath -replace '\\', '/'
	if ($WindowsPath -match '^([A-Za-z]):(.*)$') {
		return "/mnt/$($Matches[1].ToLower())$($Matches[2])"
	}
	return $WindowsPath
}

$WslScript = Get-WslPath $args[0]
$WslPackageDir = Get-WslPath (Split-Path -parent $args[0])
wsl bash -c "cd '$WslPackageDir' && sed -i 's/\r$//' $WslScript && chmod +x $WslScript && ($WslScript '$($args[1])' '$($args[2])')"