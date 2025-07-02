param location string = resourceGroup().location
param logAnalyticsSkuName string = 'PerGB2018'
param containerRegistrySkuName string = 'Standard'

var rgName = resourceGroup().name

var logAnalyticsWorkspaceName = 'logs-${rgName}'
var appInsightsName = 'appins-${rgName}'

resource logAnalyticsWorkspace 'Microsoft.OperationalInsights/workspaces@2020-03-01-preview' = {
  name: logAnalyticsWorkspaceName
  location: location
  properties: any({
    retentionInDays: 30
    features: {
      searchVersion: 1
    }
    sku: {
      name: logAnalyticsSkuName
    }
  })
}

resource appInsights 'Microsoft.Insights/components@2020-02-02' = {
  name: appInsightsName
  location: location
  kind: 'web'
  properties: {
    Application_Type: 'web'
    WorkspaceResourceId:logAnalyticsWorkspace.id
  }
}

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2022-02-01-preview' = {
  name:'registry${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: containerRegistrySkuName
  }
  properties: {
    adminUserEnabled: true
    publicNetworkAccess: 'Enabled'
  }
}

// https://github.com/Azure/azure-rest-api-specs/blob/Microsoft.App-2022-03-01/specification/app/resource-manager/Microsoft.App/preview/2022-01-01-preview/ManagedEnvironments.json
resource environment 'Microsoft.App/managedEnvironments@2022-03-01' = {
  name: 'env-${rgName}'
  location: location
  properties: {
    appLogsConfiguration: {
      destination: 'log-analytics'
      logAnalyticsConfiguration: {
        customerId: logAnalyticsWorkspace.properties.customerId
        sharedKey: logAnalyticsWorkspace.listKeys().primarySharedKey
      }
    }
  }
}

output containerRegistryName string = containerRegistry.name
