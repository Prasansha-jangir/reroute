// Reroute — Azure infrastructure (resource-group scope).
//   az group create -n rg-reroute-dev -l westeurope
//   az deployment group create -g rg-reroute-dev -f infra/azure/main.bicep -p env=dev sqlAdminGroupObjectId=<entra-group-id> sqlAdminGroupName=<name>
// Cost notes: AKS free tier + 1 small node (stop when idle), Azure SQL serverless auto-pause, Redis Basic C0.

targetScope = 'resourceGroup'

@allowed(['dev', 'prod'])
param env string = 'dev'
param location string = resourceGroup().location
@description('OpenAI models are region-specific; Sweden Central keeps data in the EU and has broad model availability.')
param openAiLocation string = 'swedencentral'
@description('Entra ID group that administers Azure SQL (Entra-only auth: no SQL passwords exist).')
param sqlAdminGroupObjectId string
param sqlAdminGroupName string

var prefix = 'reroute-${env}'
var unique = uniqueString(resourceGroup().id)
var tags = { project: 'reroute', env: env }

// ---------------------------------------------------------------- observability
resource logs 'Microsoft.OperationalInsights/workspaces@2023-09-01' = {
  name: '${prefix}-logs'
  location: location
  tags: tags
  properties: { sku: { name: 'PerGB2018' }, retentionInDays: 30 }
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: '${prefix}-ai'
  location: location
  kind: 'web'
  tags: tags
  properties: { Application_Type: 'web', WorkspaceResourceId: logs.id }
}

// ---------------------------------------------------------------- containers
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' = {
  name: 'reroute${env}${unique}'
  location: location
  tags: tags
  sku: { name: 'Basic' }
  properties: { adminUserEnabled: false }
}

resource aks 'Microsoft.ContainerService/managedClusters@2024-05-01' = {
  name: '${prefix}-aks'
  location: location
  tags: tags
  sku: { name: 'Base', tier: 'Free' }
  identity: { type: 'SystemAssigned' }
  properties: {
    dnsPrefix: '${prefix}-aks'
    oidcIssuerProfile: { enabled: true }          // required for Workload Identity federation
    securityProfile: { workloadIdentity: { enabled: true } }
    agentPoolProfiles: [
      {
        name: 'system'
        mode: 'System'
        count: 1
        vmSize: env == 'prod' ? 'Standard_D4s_v5' : 'Standard_B4ms'
        osType: 'Linux'
        enableAutoScaling: true
        minCount: 1
        maxCount: env == 'prod' ? 5 : 2
      }
    ]
    networkProfile: { networkPlugin: 'azure', networkPluginMode: 'overlay', networkPolicy: 'cilium', networkDataplane: 'cilium' }
    addonProfiles: {
      azureKeyvaultSecretsProvider: { enabled: true, config: { enableSecretRotation: 'true' } }
      omsagent: { enabled: true, config: { logAnalyticsWorkspaceResourceID: logs.id } }
    }
  }
}

// Kubelet may pull from ACR — no registry passwords.
resource acrPull 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(acr.id, aks.id, 'acrpull')
  scope: acr
  properties: {
    principalId: aks.properties.identityProfile.kubeletidentity.objectId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '7f951dda-4ed3-4680-a7ca-43fe172d538d')
  }
}

// ---------------------------------------------------------------- workload identity (claims-api)
resource claimsIdentity 'Microsoft.ManagedIdentity/userAssignedIdentities@2023-01-31' = {
  name: '${prefix}-claims-id'
  location: location
  tags: tags
}

resource claimsFederation 'Microsoft.ManagedIdentity/userAssignedIdentities/federatedIdentityCredentials@2023-01-31' = {
  parent: claimsIdentity
  name: 'aks-claims-api'
  properties: {
    issuer: aks.properties.oidcIssuerProfile.issuerURL
    subject: 'system:serviceaccount:reroute:claims-api'   // only THIS service account can use the identity
    audiences: ['api://AzureADTokenExchange']
  }
}

// ---------------------------------------------------------------- data
resource sql 'Microsoft.Sql/servers@2023-08-01-preview' = {
  name: '${prefix}-sql-${unique}'
  location: location
  tags: tags
  properties: {
    minimalTlsVersion: '1.2'
    publicNetworkAccess: env == 'prod' ? 'Disabled' : 'Enabled'
    administrators: {
      administratorType: 'ActiveDirectory'
      azureADOnlyAuthentication: true
      login: sqlAdminGroupName
      sid: sqlAdminGroupObjectId
      principalType: 'Group'
      tenantId: subscription().tenantId
    }
  }
}

resource claimsDb 'Microsoft.Sql/servers/databases@2023-08-01-preview' = {
  parent: sql
  name: 'claims'
  location: location
  tags: tags
  sku: { name: 'GP_S_Gen5_1', tier: 'GeneralPurpose' }   // serverless
  properties: { autoPauseDelay: 60, minCapacity: json('0.5'), zoneRedundant: false }
}

resource redis 'Microsoft.Cache/redis@2024-03-01' = {
  name: '${prefix}-redis-${unique}'
  location: location
  tags: tags
  properties: {
    sku: env == 'prod' ? { name: 'Standard', family: 'C', capacity: 1 } : { name: 'Basic', family: 'C', capacity: 0 }
    enableNonSslPort: false
    minimumTlsVersion: '1.2'
    redisConfiguration: { 'aad-enabled': 'true' }
  }
}

// ---------------------------------------------------------------- messaging
resource serviceBus 'Microsoft.ServiceBus/namespaces@2022-10-01-preview' = {
  name: '${prefix}-sb-${unique}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard' }   // topics needed by MassTransit publish
  properties: { minimumTlsVersion: '1.2', disableLocalAuth: false }
}

resource eventHubs 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: '${prefix}-eh-${unique}'
  location: location
  tags: tags
  sku: { name: 'Standard', tier: 'Standard', capacity: 1 }   // Standard+ exposes the Kafka endpoint
  properties: { kafkaEnabled: true, minimumTlsVersion: '1.2' }
}

var topics = ['flight-status.raw.v1', 'disruption.detected.v1', 'itinerary.events.v1', 'claims.events.v1']
resource hubs 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = [for t in topics: {
  parent: eventHubs
  name: t
  properties: { partitionCount: env == 'prod' ? 12 : 2, messageRetentionInDays: 7 }
}]

resource dlqHubs 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = [for t in topics: {
  parent: eventHubs
  name: '${t}.dlq'
  properties: { partitionCount: 1, messageRetentionInDays: 7 }
}]

// ---------------------------------------------------------------- secrets
resource keyVault 'Microsoft.KeyVault/vaults@2023-07-01' = {
  name: 'kv-${env}-${take(unique, 12)}'
  location: location
  tags: tags
  properties: {
    tenantId: subscription().tenantId
    sku: { family: 'A', name: 'standard' }
    enableRbacAuthorization: true
    enableSoftDelete: true
    enablePurgeProtection: true
  }
}

resource kvSecretsUser 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(keyVault.id, claimsIdentity.id, 'kv-secrets-user')
  scope: keyVault
  properties: {
    principalId: claimsIdentity.properties.principalId
    principalType: 'ServicePrincipal'
    roleDefinitionId: subscriptionResourceId('Microsoft.Authorization/roleDefinitions', '4633458b-17de-408a-b874-0445c86b69e6')
  }
}

// ---------------------------------------------------------------- AI
resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: '${prefix}-oai-${unique}'
  location: openAiLocation
  tags: tags
  kind: 'OpenAI'
  sku: { name: 'S0' }
  properties: { customSubDomainName: '${prefix}-oai-${unique}', publicNetworkAccess: 'Enabled' }
}

resource chatDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'gpt-4o-mini'
  sku: { name: 'GlobalStandard', capacity: 30 }
  properties: { model: { format: 'OpenAI', name: 'gpt-4o-mini', version: '2024-07-18' } }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'text-embedding-3-small'
  dependsOn: [chatDeployment]   // deployments on one account must be sequential
  sku: { name: 'Standard', capacity: 30 }
  properties: { model: { format: 'OpenAI', name: 'text-embedding-3-small', version: '1' } }
}

// ---------------------------------------------------------------- frontend (App Service)
resource plan 'Microsoft.Web/serverfarms@2023-12-01' = {
  name: '${prefix}-plan'
  location: location
  tags: tags
  kind: 'linux'
  sku: { name: env == 'prod' ? 'P0v3' : 'B1' }
  properties: { reserved: true }
}

resource web 'Microsoft.Web/sites@2023-12-01' = {
  name: '${prefix}-web-${unique}'
  location: location
  tags: tags
  properties: {
    serverFarmId: plan.id
    httpsOnly: true
    siteConfig: {
      linuxFxVersion: 'NODE|22-lts'
      appCommandLine: 'pm2 serve /home/site/wwwroot --no-daemon --spa'
      minTlsVersion: '1.2'
      ftpsState: 'Disabled'
      appSettings: [{ name: 'APPLICATIONINSIGHTS_CONNECTION_STRING', value: appInsights.properties.ConnectionString }]
    }
  }
}

resource webStaging 'Microsoft.Web/sites/slots@2023-12-01' = if (env == 'prod') {
  parent: web
  name: 'staging'   // deploy here, smoke test, then swap → zero-downtime + instant rollback
  location: location
  properties: { serverFarmId: plan.id }
}

output acrLoginServer string = acr.properties.loginServer
output aksName string = aks.name
output claimsIdentityClientId string = claimsIdentity.properties.clientId
output keyVaultName string = keyVault.name
output openAiEndpoint string = openAi.properties.endpoint
output webAppName string = web.name
output sqlServerFqdn string = sql.properties.fullyQualifiedDomainName
