<#
.SYNOPSIS
  Renames this Bloxstrap fork in one go. Safe to re-run with a different name later.

.EXAMPLE
  powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name Pulsestrap
  powershell -ExecutionPolicy Bypass -File fork\rebrand.ps1 -Name Pulsestrap -Repo myuser/pulsestrap -Icon C:\art\pulse.ico

.NOTES
  Deliberately NOT renamed (compatibility / credit):
    - the C# namespace "Bloxstrap"          (keeps upstream merges painless)
    - "BloxstrapRPC"                         (games use this to talk to the bootstrapper)
    - "BloxstrapCustomBootstrapper"          (existing custom themes keep working)
    - the "Bloxstrap" bootstrapper style/icon enum names
    - LicensesPage.xaml and the About credits (MIT license requires keeping the original notice)
#>
param(
    [Parameter(Mandatory)][ValidatePattern('^[A-Za-z][A-Za-z0-9]{1,31}$')][string]$Name,
    [string]$Owner = $Name,
    [ValidatePattern('^$|^[\w.-]+/[\w.-]+$')][string]$Repo = "",
    [string]$Icon = ""
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$src  = Join-Path $root 'Bloxstrap'

function Read-Text($path) {
    $bytes = [IO.File]::ReadAllBytes($path)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    return @{ Text = [IO.File]::ReadAllText($path); Bom = $bom }
}
function Write-Text($path, $file) {
    [IO.File]::WriteAllText($path, $file.Text, (New-Object Text.UTF8Encoding($file.Bom)))
}

# --- 1. App.xaml.cs constants (single source of truth for paths, registry, mutexes) ---
$appPath = Join-Path $src 'App.xaml.cs'
$app = Read-Text $appPath
$m = [regex]::Match($app.Text, '#else\s+public const string ProjectName = "([^"]+)";')
if (-not $m.Success) { throw "Couldn't find ProjectName in App.xaml.cs" }
$old = $m.Groups[1].Value
Write-Host "Renaming '$old' -> '$Name'"

$repoUrl = if ($Repo) { "https://github.com/$Repo" } else { "" }
$set = {
    param($const, $value)
    $pattern = "public const string $const = `"[^`"]*`";"
    if (-not [regex]::IsMatch($app.Text, $pattern)) { throw "Couldn't find $const" }
    $app.Text = [regex]::Replace($app.Text, $pattern, "public const string $const = `"$value`";")
}
$app.Text = [regex]::Replace($app.Text, 'ProjectName = "[^"]+-QA";', "ProjectName = `"$Name-QA`";")
$app.Text = [regex]::Replace($app.Text, '(#else\s+public const string ProjectName = )"[^"]+";', "`$1`"$Name`";")
& $set 'ProjectOwner' $Owner
if ($Repo) {
    & $set 'ProjectRepository' $Repo
    & $set 'ProjectDownloadLink' "$repoUrl/releases/latest"
    & $set 'ProjectHelpLink' "$repoUrl#readme"
    & $set 'ProjectSupportLink' "$repoUrl/issues/new"
}
Write-Text $appPath $app

# --- 2. csproj: exe name + product name (RootNamespace pinned so embedded resource names don't move) ---
$projPath = Join-Path $src 'Bloxstrap.csproj'
$proj = Read-Text $projPath
foreach ($tag in 'AssemblyName', 'Product', 'RootNamespace') {
    $value = if ($tag -eq 'RootNamespace') { 'Bloxstrap' } else { $Name }
    if ($proj.Text -match "<$tag>") {
        $proj.Text = [regex]::Replace($proj.Text, "<$tag>[^<]*</$tag>", "<$tag>$value</$tag>")
    } else {
        $proj.Text = $proj.Text.Replace('<OutputType>WinExe</OutputType>', "<OutputType>WinExe</OutputType>`r`n    <$tag>$value</$tag>")
    }
}
Write-Text $projPath $proj

# --- 3. Every translation: replace the name inside <value> only (resource keys stay the same) ---
$wordOld = '(?<![\w/.])' + [regex]::Escape($old) + '(?![\w])'
$resxCount = 0
Get-ChildItem (Join-Path $src 'Resources') -Filter 'Strings*.resx' | ForEach-Object {
    $f = Read-Text $_.FullName
    $f.Text = [regex]::Replace($f.Text, '(?s)(<value>)(.*?)(</value>)', {
        param($v) $v.Groups[1].Value + [regex]::Replace($v.Groups[2].Value, $wordOld, $Name) + $v.Groups[3].Value
    })
    Write-Text $_.FullName $f
    $resxCount++
}
Write-Host "Updated $resxCount string tables"

# --- 4. Hardcoded window titles / headings in XAML and code ---
$xamlFiles = Get-ChildItem $src -Recurse -Include *.xaml | Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' -and $_.Name -ne 'LicensesPage.xaml' }
foreach ($x in $xamlFiles) {
    $f = Read-Text $x.FullName
    $before = $f.Text
    $f.Text = $f.Text.Replace("Title=`"$old`"", "Title=`"$Name`"").Replace("Text=`"$old`"", "Text=`"$Name`"").Replace("Text=`"$old v", "Text=`"$Name v").Replace("Content=`"$old`"", "Content=`"$Name`"")
    if ($repoUrl) {
        $f.Text = $f.Text.Replace('NavigateUri="https://github.com/bloxstraplabs/bloxstrap/issues"', "NavigateUri=`"$repoUrl/issues`"")
        $f.Text = $f.Text.Replace('NavigateUri="https://github.com/bloxstraplabs/bloxstrap"', "NavigateUri=`"$repoUrl`"")
        $f.Text = $f.Text.Replace('CommandParameter="https://github.com/bloxstraplabs/bloxstrap/releases/latest"', "CommandParameter=`"$repoUrl/releases/latest`"")
    }
    if ($f.Text -ne $before) { Write-Text $x.FullName $f; Write-Host "  patched $($x.Name)" }
}
$dlg = Join-Path $src 'UI\Elements\Bootstrapper\CustomDialog.Elements.cs'
$f = Read-Text $dlg
$f.Text = $f.Text.Replace("?? `"$old`";", "?? App.ProjectName;")
Write-Text $dlg $f

# --- 5. Optional icon ---
if ($Icon) {
    if (-not (Test-Path $Icon)) { throw "Icon not found: $Icon" }
    Copy-Item $Icon (Join-Path $src 'Bloxstrap.ico') -Force
    Copy-Item $Icon (Join-Path $src 'Resources\IconBloxstrap.ico') -Force
    Write-Host "Replaced app icon"
}

Write-Host ""
Write-Host "Done. Build with:  dotnet build Bloxstrap.sln -c Release"
Write-Host "Output exe:        Bloxstrap\bin\Release\net6.0-windows\$Name.exe"
if ($app.Text -match 'ProjectRepository = "";') { Write-Host "Auto-updater stays OFF until you re-run with -Repo owner/name." }
