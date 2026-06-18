param(
    [Parameter(Mandatory = $true)]
    [string]$EnvFile,

    [Parameter(Mandatory = $true)]
    [string]$OutputFile
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-DotEnv {
    param([string]$Path)

    $result = @{}
    if (-not (Test-Path -LiteralPath $Path)) {
        return $result
    }

    foreach ($line in Get-Content -LiteralPath $Path) {
        $trimmed = $line.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) {
            continue
        }

        if ($trimmed.StartsWith('#')) {
            continue
        }

        $index = $trimmed.IndexOf('=')
        if ($index -lt 0) {
            continue
        }

        $name = $trimmed.Substring(0, $index).Trim()
        $value = $trimmed.Substring($index + 1).Trim()

        if (($value.StartsWith('"') -and $value.EndsWith('"')) -or ($value.StartsWith("'") -and $value.EndsWith("'"))) {
            if ($value.Length -ge 2) {
                $value = $value.Substring(1, $value.Length - 2)
            }
        }

        if (-not [string]::IsNullOrWhiteSpace($name)) {
            $result[$name] = $value
        }
    }

    return $result
}

function To-CSharpStringLiteral {
    param([string]$Text)

    if ($null -eq $Text) {
        return '""'
    }

    $escaped = $Text.Replace('\\', '\\\\').Replace('"', '\\"')
    return '"' + $escaped + '"'
}

$envValues = Read-DotEnv -Path $EnvFile
$runtimeKey = ''
$adminKey = ''

if ($envValues.ContainsKey('OPENAI_SHARED_RUNTIME_KEY')) {
    $runtimeKey = $envValues['OPENAI_SHARED_RUNTIME_KEY']
}

if ($envValues.ContainsKey('OPENAI_SHARED_ADMIN_KEY')) {
    $adminKey = $envValues['OPENAI_SHARED_ADMIN_KEY']
}

$runtimeLiteral = To-CSharpStringLiteral -Text $runtimeKey
$adminLiteral = To-CSharpStringLiteral -Text $adminKey

$outputDirectory = Split-Path -Parent $OutputFile
if (-not (Test-Path -LiteralPath $outputDirectory)) {
    New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null
}

$generated = @"
namespace SmartphoneAppMessenger
{
    internal static class EmbeddedAiSecrets
    {
        internal const string SharedOpenAiRuntimeKey = $runtimeLiteral;
        internal const string SharedOpenAiAdminKey = $adminLiteral;
    }
}
"@

Set-Content -LiteralPath $OutputFile -Value $generated -NoNewline
