// Deployment script for ingest test setup. One resource group for our deputy web app and another group for a customer blob storage and event hub.
// Run with the following command, while logged into Azure
// az deployment sub create -f main.bicep -l eastus

targetScope = 'subscription'

param location string = 'eastus'
param productName string = 'logscale'
param feature string = 'ingest'
param env string = 'dev'
param exportMonitorLog bool = true

param rgName string = '${productName}-${feature}-${env}'
param eventHubName string

// Customer services deploy
resource customerRg 'Microsoft.Resources/resourceGroups@2024-03-01' =  {
  name: rgName
  location: location
}

module storageAccount 'storageaccount.bicep' = {
  scope: customerRg
  name: 'storageAccountDeploy'
  params: {
    location: location
  }
}

module eventHubModule 'eventhub.bicep' = {
  scope: customerRg
  name: 'eventHubDeploy'
  params: {
    eventHubName: eventHubName
  }
}

module containerEnvModule 'containerenv.bicep' = {
  scope: customerRg
  name: 'containerAppDeploy'
  params: {
    location:location
  }
}

module blobStorageModule 'blobstorage.bicep' = {
  scope: customerRg
  name: 'blobStorageDeploy'
  params: {
    storageAccountName: storageAccount.outputs.storageAccountName
  }
}

module activityLogMonitorModule 'monitor.bicep' = if(exportMonitorLog) {
  scope:  subscription()
  name: 'activityLogMonitorDeploy'
  params: {
    eventHubName: eventHubModule.outputs.eventHubName
    eventHubAuthorizationRuleId: eventHubModule.outputs.eventHubAuthRuleId
    env: env
  }
}

output resourceGroupName string = rgName
output containerRegistryName string = containerEnvModule.outputs.containerRegistryName
output storageAccountName string = storageAccount.outputs.storageAccountName
output eventHubName string = eventHubModule.outputs.eventHubNamespaceName
output diagnosticName string = activityLogMonitorModule.outputs.diagnosticName
