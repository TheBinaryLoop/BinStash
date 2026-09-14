pipeline {
  agent any

  tools {
    dotnetsdk 'dotnet-lts'
    nodejs 'node-lts'
  }

  options {
    timestamps()
    // The build wipes the workspace before it starts, so two runs of this job sharing an agent
    // would delete each other's sources mid-compile. Queue them instead of running them at once.
    disableConcurrentBuilds()
  }

  environment {
    SOLUTION = 'BinStash.slnx'
    BUILD_CONFIG = 'Release'
    MSBUILDDISABLENODEREUSE = 1
    // Vitest colourises its output; ANSI escapes make the Jenkins console log unreadable.
    NO_COLOR = 'true'
  }

  parameters {
    booleanParam(name: 'RUN_TESTS', defaultValue: true,  description: 'Run unit tests')
  }

  stages {
    stage('Checkout') {
      steps {
        // Start from an empty workspace: stale bin/obj, node_modules and wwwroot output from a
        // previous build can mask a broken build or resurrect deleted files in the bundle.
        cleanWs()
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

    stage('Frontend Tests') {
      when { expression { params.RUN_TESTS } }
      steps {
        dir('src/BinStash.Frontend') {
          // Writes junit.xml and coverage/ (cobertura + html); see vitest.config.ts.
          bat 'pnpm test:ci'
        }
      }
      post {
        always {
          // The `junit` step, not the xUnit plugin used by the .NET stage: xUnit validates the
          // report against the Surefire XSD, whose SUREFIRE_TIME allows at most three decimals,
          // and vitest writes full float seconds (time="0.8954164"). JUnitResultArchiver does not
          // validate, and the two publishers contribute to the same build test result.
          junit allowEmptyResults: true, testResults: 'src/BinStash.Frontend/junit.xml'
          publishHTML([
            allowMissing: true,
            alwaysLinkToLastBuild: true,
            keepAll: true,
            reportDir: 'src/BinStash.Frontend/coverage',
            reportFiles: 'index.html',
            reportName: 'Frontend Coverage'
          ])
          archiveArtifacts artifacts: 'src/BinStash.Frontend/coverage/cobertura-coverage.xml', allowEmptyArchive: true
        }
        success { echo '✅ Frontend tests passed.' }
        failure { echo '❌ Frontend tests failed. Check logs.' }
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
      // Reports vulnerable NuGet packages (direct + transitive) across the solution.
      // Policy: High/Critical fail the build; Low/Moderate mark it UNSTABLE.
      // (High/Critical also fail earlier at Restore via Directory.Build.props, which
      //  promotes NU1903/NU1904 to errors; this stage is the report + flag tier + a
      //  backstop, and archives the full vulnerability report.)
      steps {
        script {
          def status = powershell(returnStatus: true, script: '''
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

              if ($findings.Count -eq 0) { Write-Host "No vulnerable packages found."; exit 0 }

              $findings | Sort-Object Severity, Package |
                Format-Table Project, Package, Version, Severity, Suppressed, Advisory -AutoSize -Wrap |
                Out-String -Width 240 | Write-Host

              $active = @($findings | Where-Object { -not $_.Suppressed })
              $fail   = @($active | Where-Object { $failSeverities -contains $_.Severity.ToLower() })
              $flag   = @($active | Where-Object { $failSeverities -notcontains $_.Severity.ToLower() })

              if ($fail.Count -gt 0) { Write-Host "FAIL: $($fail.Count) High/Critical vulnerable package(s) without suppression."; exit 2 }
              if ($flag.Count -gt 0) { Write-Host "FLAG: $($flag.Count) Low/Moderate vulnerable package(s)."; exit 3 }
              Write-Host "All vulnerable packages are suppressed; no active findings."
              exit 0
            } catch {
              Write-Host "Security audit script error: $_"
              exit 1
            }
          ''')
          if (status == 2) {
            error('Security audit: High/Critical vulnerable dependencies found with no suppression — failing build.')
          } else if (status == 3) {
            unstable('Security audit: Low/Moderate vulnerable dependencies found — build flagged UNSTABLE.')
          } else if (status != 0) {
            error("Security audit script failed (exit code ${status}).")
          } else {
            echo '✅ Security audit: no active vulnerable dependencies.'
          }
        }
      }
      post {
        always {
          archiveArtifacts artifacts: 'vulnerable-packages.json', allowEmptyArchive: true
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
        // xunit.v3 4.x runs on Microsoft.Testing.Platform (MTP), which the .NET 10 SDK
        // will not execute through the legacy VSTest target that the `dotnetTest` step
        // (and its `logger: 'xunit'`) relies on. MTP is opted into via global.json, takes
        // the solution through `--solution`, and emits the xUnit.net v2+ XML that the
        // xUnitDotNet publisher below consumes via `--report-xunit-xml`.
        bat "dotnet test --solution ${env.SOLUTION} -c ${env.BUILD_CONFIG} --no-build --report-xunit-xml"
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
