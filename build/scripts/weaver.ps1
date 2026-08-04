[CmdletBinding()]
param(
  [Parameter(Position = 0)]
  [ValidateSet('check', 'package', 'resolve', 'update-markdown', 'help')]
  [string]$Command = 'help',

  [Parameter(Position = 1)]
  [string]$Component,

  [switch]$DryRun
)

$ErrorActionPreference = 'Stop'

# Keep these versions together so registry checks and generated documentation are
# reproducible across developer machines and CI.
$WeaverVersion = 'v0.25.1'
$SemanticConventionsVersion = '1.41.0'
$RepositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot '../..')).Path
$TemplateRoot = Join-Path $PSScriptRoot 'weaver-templates'

function Show-Usage {
  Write-Host @'
Usage: ./build/scripts/weaver.ps1 <check|package|resolve|update-markdown> <component>

The component is the package directory name below src, for example:
  OpenTelemetry.Instrumentation.StackExchangeRedis
  OpenTelemetry.Instrumentation.SqlClient

Root-level manifest.yaml, spans.yaml, metrics.yaml, and other Weaver signal YAML
files are part of a component registry. Keep unrelated YAML in a subdirectory.

Use -DryRun with update-markdown to fail when README generated sections are stale.
'@
}

function Resolve-ComponentPath([string]$Name) {
  if ([string]::IsNullOrWhiteSpace($Name)) {
    throw 'A component name is required (for example OpenTelemetry.Instrumentation.SqlClient).'
  }

  # Component names are package directory names, not arbitrary paths. This also
  # prevents a caller from mounting a path outside this repository into Docker.
  if ($Name -notmatch '^OpenTelemetry\.[A-Za-z0-9.]+$') {
    throw "Invalid component name '$Name'. Use an OpenTelemetry package name."
  }

  $path = Join-Path $RepositoryRoot ('src/' + $Name)
  if (-not (Test-Path -LiteralPath (Join-Path $path 'manifest.yaml'))) {
    throw "No root-level manifest.yaml was found for component '$Name' at '$path'."
  }

  return (Resolve-Path -LiteralPath $path).Path
}

function Invoke-Weaver([string[]]$Arguments) {
  $repoMount = $RepositoryRoot.Replace('\', '/')
  & docker run --rm `
    -u 1000:1000 `
    -v "${repoMount}:/workspace" `
    -w /workspace `
    -e HOME=/tmp `
    ("otel/weaver:{0}" -f $WeaverVersion) @Arguments

  if ($LASTEXITCODE -ne 0) {
    throw "Weaver failed with exit code $LASTEXITCODE."
  }
}

function Invoke-Component([string]$Name) {
  $componentPath = Resolve-ComponentPath $Name
  $registry = '/workspace/src/' + $Name
  $artifact = '/workspace/artifacts/weaver/' + $Name
  $artifactHost = Join-Path $RepositoryRoot ('artifacts/weaver/' + $Name)

  switch ($Command.ToLowerInvariant()) {
    'check' {
      Invoke-Weaver @('registry', 'check', '--registry', $registry, '--v2')
    }
    'package' {
      New-Item -ItemType Directory -Force -Path $artifactHost | Out-Null
      Invoke-Weaver @('registry', 'package', '--registry', $registry, '--v2', '--resolved-registry-uri', ('https://opentelemetry.io/schemas/dotnet-contrib/' + $Name + '/resolved.yaml'), '--output', ($artifact + '/package'))
    }
    'resolve' {
      New-Item -ItemType Directory -Force -Path $artifactHost | Out-Null
      Invoke-Weaver @('registry', 'resolve', '--registry', $registry, '--v2', '--output', ($artifact + '/resolved.yaml'))
    }
    'update-markdown' {
      # markdown_dir is a positional/config-only argument in Weaver v0.25.1.
      $args = @('registry', 'update-markdown', '--registry', $registry, '--v2', '--templates', '/workspace/build/scripts/weaver-templates', '--target', 'markdown', ('/workspace/src/' + $Name))
      if ($DryRun) {
        $args += '--dry-run'
      }
      Invoke-Weaver $args
    }
    default {
      throw "Unknown command '$Command'."
    }
  }
}

if ($Command -eq 'help') {
  Show-Usage
  exit 0
}

Invoke-Component $Component
