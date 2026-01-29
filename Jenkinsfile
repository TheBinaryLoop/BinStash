pipeline {
  agent any

  tools {
    dotnetsdk 'dotnet-lts'
  }

  options {
    timestamps()
  }

  environment {
    SOLUTION = 'BinStash.sln'
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
    //always {
      // Recommended by the plugin to avoid lingering build servers on agents
      // (shuts down MSBuild/Roslyn servers that may keep the build "hanging")
    //  dotnetBuild(
        // dummy no-op call solely to access the 'shutDownBuildServers' option
    //    project: '.', // ignored for shutdown
    //    sdk: 'dotnet-lts',
    //    shutDownBuildServers: true
    //  ) // :contentReference[oaicite:1]{index=1}
    //}
    success { echo "✅ Build completed for ${env.SOLUTION} (${env.BUILD_CONFIG})." }
    failure { echo "❌ Build failed. Check logs." }
  }
}
