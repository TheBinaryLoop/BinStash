pipeline {
  agent any

  tools {
    dotnetsdk 'dotnet-9.0'
  }

  options {
    timestamps()
  }

  environment {
    SOLUTION     = 'BinStash.sln'
    BUILD_CONFIG = 'Release'
  }

  parameters {
    booleanParam(name: 'RUN_TESTS', defaultValue: false,  description: 'Run unit tests')
  }

  stages {
    stage('Checkout') {
      steps {
        checkout scm
      }
    }

    stage('SDK Info') {
      steps {
        // The SDK is on PATH via the 'tools { dotnetsdk ... }' directive
        withDotNet(sdk: 'dotnet-9.0') {
          bat 'dotnet --info'
        }
      }
    }

    stage('Restore') {
      steps {
        dotnetRestore(
          project: env.SOLUTION,
          sdk: 'dotnet-9.0',
          showSdkInfo: true // prints dotnet --info before the command
        )
      }
    }

    stage('Build') {
      steps {
        dotnetBuild(
          project: env.SOLUTION,
          configuration: env.BUILD_CONFIG,
          noRestore: true,
          sdk: 'dotnet-9.0'
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
          sdk: 'dotnet-9.0'
        )

        // Option B (optional): also produce TRX for JUnit if you want test reports in Jenkins UI
        // withDotNet(sdk: 'dotnet-9.0') {
        //   sh 'dotnet test "$SOLUTION" -c "$BUILD_CONFIG" --no-build --logger "trx;LogFileName=test_results.trx"'
        // }
      }
      post {
        // If you enabled Option B above, publish TRX:
        // always { junit allowEmptyResults: true, testResults: '**/TestResults/*.trx' }
        success { echo '✅ Tests passed' }
      }
    }
  }
  post {
    always {
      // Recommended by the plugin to avoid lingering build servers on agents
      // (shuts down MSBuild/Roslyn servers that may keep the build "hanging")
      dotnetBuild(
        // dummy no-op call solely to access the 'shutDownBuildServers' option
        project: '.', // ignored for shutdown
        sdk: 'dotnet-9.0',
        shutDownBuildServers: true
      ) // :contentReference[oaicite:1]{index=1}
    }
    success { echo "✅ Build completed for ${env.SOLUTION} (${env.BUILD_CONFIG})." }
    failure { echo "❌ Build failed. Check logs." }
  }
}
