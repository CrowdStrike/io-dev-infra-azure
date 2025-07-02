param location string

resource storageAccount 'Microsoft.Storage/storageAccounts@2023-05-01' = {
  name: 'storage${uniqueString(resourceGroup().id)}'
  location: location
  sku: {
    name: 'Standard_LRS'
  }
  kind: 'StorageV2'
}

output storageAccountName string = storageAccount.name
output storageAccountId string = storageAccount.id
