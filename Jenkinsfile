pipeline {
  agent any

  tools {
    dotnetsdk 'dotnet-lts'
    nodejs 'node-lts'
  }

  options {
    timestamps()
  }

  environment {
    SOLUTION = 'BinStash.slnx'
    BUILD_CONFIG = 'Release'
    MSBUILDDISABLENODEREUSE = 1
  }

  parameters {
    booleanParam(name: 'RUN_TESTS', defaultValue: true,  description: 'Run unit tests')
  }

  stages {
    stage('Checkout') {
      steps {
        checkout scm
      }
    }

    stage('Frontend') {
      steps {
        dir('src/BinStash.Frontend') {
          bat 'corepack enable'
          bat 'corepack prepare pnpm@latest --activate'
          bat 'pnpm install --frozen-lockfile'
          bat 'pnpm build'
        }
      }
      post {
        success { echo '✅ Frontend build completed.' }
        failure { echo '❌ Frontend build failed. Check logs.' }
      }
    }

    stage('SDK Info') {
      steps {
        // The SDK is on PATH via the 'tools { dotnetsdk ... }' directive
        withDotNet(sdk: 'dotnet-lts') {
          bat 'dotnet --info'
        }
      }
    }

    stage('Restore') {
      steps {
        dotnetRestore(
          project: env.SOLUTION,
          sdk: 'dotnet-lts',
          showSdkInfo: false // prints dotnet --info before the command
        )
      }
    }

    stage('Security Audit') {
      // Reports vulnerable dependencies across the .NET solution (NuGet, direct + transitive)
      // and the frontend (pnpm, src/BinStash.Frontend — shipped/prod dependencies only).
      // Policy (both ecosystems): High/Critical fail the build; Low/Moderate mark it UNSTABLE.
      // (For NuGet, High/Critical also fail earlier at Restore via Directory.Build.props, which
      //  promotes NU1903/NU1904 to errors; this stage is the report + flag tier + a backstop,
      //  and archives the full vulnerability reports.)
      steps {
        script {
          def dotnetStatus = powershell(returnStatus: true, script: '''
            $ErrorActionPreference = "Stop"
            try {
              # Advisory URLs accepted because no upstream fix exists yet.
              # Keep in sync with Directory.Build.props <NuGetAuditSuppress>. Comment each with why + date.
              $suppressed = @(
                # "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx"
              )
              $failSeverities = @("high", "critical")

              $raw = dotnet list "$env:SOLUTION" package --vulnerable --include-transitive --format json | Out-String
              if (-not $raw.Contains("{")) { Write-Host "No JSON returned by 'dotnet list package'."; exit 1 }
              $raw = $raw.Substring($raw.IndexOf("{"))
              Set-Content -Path "vulnerable-packages.json" -Value $raw -Encoding utf8
              $data = $raw | ConvertFrom-Json

              $findings = New-Object System.Collections.Generic.List[object]
              foreach ($proj in $data.projects) {
                if (-not $proj.frameworks) { continue }
                foreach ($fw in $proj.frameworks) {
                  foreach ($bucket in @("topLevelPackages", "transitivePackages")) {
                    foreach ($pkg in $fw.$bucket) {
                      foreach ($vuln in $pkg.vulnerabilities) {
                        [void]$findings.Add([pscustomobject]@{
                          Project    = [System.IO.Path]::GetFileNameWithoutExtension($proj.path)
                          Package    = $pkg.id
                          Version    = $pkg.resolvedVersion
                          Severity   = $vuln.severity
                          Advisory   = $vuln.advisoryurl
                          Suppressed = ($suppressed -contains $vuln.advisoryurl)
                        })
                      }
                    }
                  }
                }
              }

              if ($findings.Count -eq 0) { Write-Host "No vulnerable NuGet packages found."; exit 0 }

              $findings | Sort-Object Severity, Package |
                Format-Table Project, Package, Version, Severity, Suppressed, Advisory -AutoSize -Wrap |
                Out-String -Width 240 | Write-Host

              $active = @($findings | Where-Object { -not $_.Suppressed })
              $fail   = @($active | Where-Object { $failSeverities -contains $_.Severity.ToLower() })
              $flag   = @($active | Where-Object { $failSeverities -notcontains $_.Severity.ToLower() })

              if ($fail.Count -gt 0) { Write-Host "FAIL: $($fail.Count) High/Critical vulnerable NuGet package(s) without suppression."; exit 2 }
              if ($flag.Count -gt 0) { Write-Host "FLAG: $($flag.Count) Low/Moderate vulnerable NuGet package(s)."; exit 3 }
              Write-Host "All vulnerable NuGet packages are suppressed; no active findings."
              exit 0
            } catch {
              Write-Host "NuGet security audit script error: $_"
              exit 1
            }
          ''')

          def pnpmStatus = 0
          dir('src/BinStash.Frontend') {
            pnpmStatus = powershell(returnStatus: true, script: '''
              $ErrorActionPreference = "Stop"
              try {
                # Advisory URLs accepted because no upstream fix exists yet.
                # For a permanent ignore, prefer pnpm.auditConfig (ignoreCves / ignoreGhsas) in package.json.
                $suppressed = @(
                  # "https://github.com/advisories/GHSA-xxxx-xxxx-xxxx"
                )
                $failSeverities = @("high", "critical")

                # pnpm audit exits non-zero when advisories exist; that does not throw in PowerShell.
                # --prod audits shipped dependencies only (excludes devDependencies / build tooling).
                $raw = pnpm audit --json --prod | Out-String
                if (-not $raw.Contains("{")) { Write-Host "No JSON returned by 'pnpm audit'."; exit 1 }
                $raw = $raw.Substring($raw.IndexOf("{"))
                Set-Content -Path "pnpm-audit.json" -Value $raw -Encoding utf8
                $data = $raw | ConvertFrom-Json

                $findings = New-Object System.Collections.Generic.List[object]
                if ($data.advisories) {
                  foreach ($adv in $data.advisories.PSObject.Properties.Value) {
                    [void]$findings.Add([pscustomobject]@{
                      Package    = $adv.module_name
                      Severity   = $adv.severity
                      Advisory   = $adv.url
                      Suppressed = ($suppressed -contains $adv.url)
                    })
                  }
                }

                if ($findings.Count -eq 0) { Write-Host "No vulnerable frontend packages found."; exit 0 }

                $findings | Sort-Object Severity, Package |
                  Format-Table Package, Severity, Suppressed, Advisory -AutoSize -Wrap |
                  Out-String -Width 240 | Write-Host

                $active = @($findings | Where-Object { -not $_.Suppressed })
                $fail   = @($active | Where-Object { $failSeverities -contains $_.Severity.ToLower() })
                $flag   = @($active | Where-Object { $failSeverities -notcontains $_.Severity.ToLower() })

                if ($fail.Count -gt 0) { Write-Host "FAIL: $($fail.Count) High/Critical vulnerable frontend package(s) without suppression."; exit 2 }
                if ($flag.Count -gt 0) { Write-Host "FLAG: $($flag.Count) Low/Moderate vulnerable frontend package(s)."; exit 3 }
                Write-Host "All vulnerable frontend packages are suppressed; no active findings."
                exit 0
              } catch {
                Write-Host "Frontend security audit script error: $_"
                exit 1
              }
            ''')
          }

          // Combined gate: High/Critical (2) on either side fails; a script error (1) fails;
          // otherwise Low/Moderate (3) flags the build UNSTABLE.
          if (dotnetStatus == 2 || pnpmStatus == 2) {
            error("Security audit: High/Critical vulnerable dependencies found with no suppression — failing build (nuget=${dotnetStatus}, pnpm=${pnpmStatus}).")
          } else if (dotnetStatus == 1 || pnpmStatus == 1) {
            error("Security audit script failed (nuget=${dotnetStatus}, pnpm=${pnpmStatus}).")
          } else if (dotnetStatus == 3 || pnpmStatus == 3) {
            unstable("Security audit: Low/Moderate vulnerable dependencies found — build flagged UNSTABLE (nuget=${dotnetStatus}, pnpm=${pnpmStatus}).")
          } else {
            echo '✅ Security audit: no active vulnerable dependencies (NuGet + pnpm).'
          }
        }
      }
      post {
        always {
          archiveArtifacts artifacts: 'vulnerable-packages.json, src/BinStash.Frontend/pnpm-audit.json', allowEmptyArchive: true
        }
      }
    }

    stage('Build') {
      steps {
        dotnetBuild(
          project: env.SOLUTION,
          configuration: env.BUILD_CONFIG,
          noRestore: true,
          sdk: 'dotnet-lts'
        )
      }
    }

    stage('Test') {
      when { expression { params.RUN_TESTS } }
      steps {
        dotnetTest(
          project: env.SOLUTION,
          configuration: env.BUILD_CONFIG,
          noBuild: true,
          logger: 'xunit',
          sdk: 'dotnet-lts',
          shutDownBuildServers: true
        )
      }
      post {
        always { xunit checksName: '', tools: [xUnitDotNet(excludesPattern: '', pattern: '**/TestResults/*.xml', stopProcessingIfError: true)] }
        success { echo '✅ Tests passed' }
      }
    }
  }
  post {
    success { echo "✅ Build completed for ${env.SOLUTION} (${env.BUILD_CONFIG})." }
    failure { echo "❌ Build failed. Check logs." }
  }
}
