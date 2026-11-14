// Azure infrastructure for the paper-trading platform.
//
//   Static Web Apps (Free)  ── HTTPS/WSS ──▶  Container Apps (1 replica)  ── WSS ──▶  Binance public market data
//        React SPA                                .NET API + SignalR
//                                                       ▲
//                                     image pull via managed identity (no registry passwords)
//                                                       │
//                                          Azure Container Registry (Basic)
//
// Deployed in two passes by infra/bootstrap.sh: first without the API (so the registry exists to push an
// image to), then with it. After that, GitHub Actions only updates the API image and the static site.

targetScope = 'resourceGroup'

@description('Region for the API and its dependencies. Must be outside the US: Binance geo-blocks US IP ranges.')
param location string = resourceGroup().location

@description('Region for the Static Web App. Only its metadata lives here; the site is served from a global edge network.')
param webLocation string = location

@description('Short prefix for resource names.')
@maxLength(12)
param prefix string = 'papertrading'

@description('Create the API container app. False on the first pass, before any image exists in the registry.')
param deployApi bool = true

@description('Full API image reference, e.g. <registry>.azurecr.io/papertrading-api:<git-sha>. Required when deployApi is true.')
param apiImage string = ''

@description('JWT signing key. Generated fresh per deployment and stored only as a Container Apps secret. Accounts live in memory, so rotating it on redeploy loses nothing.')
@secure()
param authSigningKey string = base64('${newGuid()}${newGuid()}${newGuid()}')

var suffix = uniqueString(resourceGroup().id)
var acrPullRoleId = '7f951dda-4ed3-4680-a7ca-43fe172d538d'

resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  properties: {
    sku: { name: 'PerGB2018' }
    retentionInDays: 30
  }
}

resource registry 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: '${prefix}${suffix}'
  location: location
  sku: { name: 'Basic' }
  properties: {
    adminUserEnabled: false // no shared registry password; pulls use a managed identity, pushes use OIDC
  }
}

// Identity the container app uses to pull its image. User-assigned so the role exists before the app is created.
resource pullIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-api-identity'
  location: location
}

resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(registry.id, pullIdentity.id, acrPullRoleId)
  scope: registry
  properties: {
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', acrPullRoleId)
    principalId: pullIdentity.properties.principalId
    principalType: 'ServicePrincipal'
  }
}

resource environment 'Microsoft.App/managedEnvironments@2024-03-01' = {
  name: '${prefix}-env'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logs.properties.customerId
        sharedKey: logs.listKeys().primarySharedKey
      }
    }
  }
}

resource web 'Microsoft.Web/staticSites@2023-12-01' = {
  name: '${prefix}-web'
  location: webLocation
  sku: {
    name: 'Free'
    tier: 'Free'
  }
  properties: {
    allowConfigFileUpdates: true
  }
}

resource api 'Microsoft.App/containerApps@2024-03-01' = if (deployApi) {
  name: '${prefix}-api'
  location: location
  identity: {
    type: 'UserAssigned'
    userAssignedIdentities: { '${pullIdentity.id}': {} }
  }
  dependsOn: [acrPull]
  properties: {
    managedEnvironmentId: environment.id
    configuration: {
      activeRevisionsMode: 'Single'
      ingress: {
        external: true
        targetPort: 8080
        transport: 'auto' // HTTP/1.1 + WebSockets for SignalR
        allowInsecure: false
      }
      registries: [
        {
          server: registry.properties.loginServer
          identity: pullIdentity.id
        }
      ]
      secrets: [
        {
          name: 'auth-signing-key'
          value: authSigningKey
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'api'
          image: apiImage
          resources: {
            cpu: json('0.25')
            memory: '0.5Gi'
          }
          env: [
            { name: 'ASPNETCORE_ENVIRONMENT', value: 'Production' }
            { name: 'Auth__SigningKey', secretRef: 'auth-signing-key' }
            { name: 'Cors__AllowedOrigins__0', value: 'https://${web.properties.defaultHostname}' }
            { name: 'MarketData__Provider', value: 'Binance' }
          ]
          // Liveness only: readiness must not depend on the price feed, or a Binance outage would take the
          // single replica out of rotation instead of letting the app fall back to the mock feed.
          probes: [
            {
              type: 'Startup'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 3
              failureThreshold: 20
            }
            {
              type: 'Liveness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 15
            }
            {
              type: 'Readiness'
              httpGet: { path: '/health/live', port: 8080 }
              periodSeconds: 10
            }
          ]
        }
      ]
      // The matching engine keeps state in memory: exactly one always-on replica (see README, "Trade-offs").
      scale: {
        minReplicas: 1
        maxReplicas: 1
      }
    }
  }
}

output registryName string = registry.name
output registryLoginServer string = registry.properties.loginServer
output apiName string = '${prefix}-api'
output apiUrl string = deployApi ? 'https://${api!.properties.configuration.ingress.fqdn}' : ''
output webName string = web.name
output webUrl string = 'https://${web.properties.defaultHostname}'
