param location string = resourceGroup().location

param containerRegistryName string
param storageAccountName string
param eventHubName string
param humioUrl string
param humioApiKey string
param imageName string = 'img-cont-${resourceGroup().name}'

param containerCpu int = 1
param containerMemory string = '2Gi'

var rgName = resourceGroup().name
var containerAppName = 'cont-${rgName}'

resource containerRegistry 'Microsoft.ContainerRegistry/registries@2022-02-01-preview' existing = {
  name: containerRegistryName
}

resource environment 'Microsoft.App/managedEnvironments@2022-03-01' existing = {
  name: 'env-${rgName}'
}

// https://github.com/Azure/azure-rest-api-specs/blob/Microsoft.App-2022-01-01-preview/specification/app/resource-manager/Microsoft.App/preview/2022-01-01-preview/ContainerApps.json
resource containerApp 'Microsoft.App/containerApps@2022-03-01' ={
  name: containerAppName
  location: location
  identity: {
    type: 'SystemAssigned'
  }
  properties:{
    managedEnvironmentId: environment.id  
    configuration: {
      ingress: null
      secrets: [
        {
          name: 'acr-accesskey'
          value: containerRegistry.listCredentials().passwords[0].value
        }
      ]
      registries: [
        {
          passwordSecretRef: 'acr-accesskey'
          server: '${containerRegistry.name}.azurecr.io'
          username: containerRegistry.listCredentials().username
        }
      ]
    }
    template: {
      containers: [
        {
          name: 'cont-${rgName}'
          resources: {
            cpu: containerCpu
            memory: containerMemory
          }
          image: '${containerRegistry.name}.azurecr.io/${imageName}:latest'
          env: [
            {
              name: 'storageAccountName'
              value: storageAccountName
            }
            {
              name: 'blobContainerName'
              value: 'logs'
            }
            {
              name: 'eventHubNamespace'
              value: eventHubName
            }
            {
              name:'eventHubName'
              value:'logs'
            }
            {
              name: 'HumioUrl'
              value: humioUrl
            }
            {
              name:'HumioApiKey'
              value: humioApiKey
            }
          ]          
        }
      ]
      scale: {
        minReplicas: 32
        maxReplicas: 32    
      }
    }
  }
}

var principalId = containerApp.identity.principalId

@description('This is the built-in event hub reader role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles')
resource hubReaderRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: 'a638d3c7-ab3a-418d-83e6-5f17a39d4fde'
}

@description('This is the built-in blob contributor role. See https://docs.microsoft.com/azure/role-based-access-control/built-in-roles')
resource storageOwnerRoleDefinition 'Microsoft.Authorization/roleDefinitions@2018-01-01-preview' existing = {
  scope: subscription()
  name: 'ba92f5b4-2d11-453d-a403-e96b0029c9fe'
}

resource hubReaderRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, containerAppName, hubReaderRoleDefinition.id)
  properties: {
    roleDefinitionId: hubReaderRoleDefinition.id
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

resource storageOwnerRoleAssignment 'Microsoft.Authorization/roleAssignments@2022-04-01' = {
  name: guid(resourceGroup().id, containerAppName, storageOwnerRoleDefinition.id)
  properties: {
    roleDefinitionId: storageOwnerRoleDefinition.id
    principalId: principalId
    principalType: 'ServicePrincipal'
  }
}

output containerAppName string = containerAppName
