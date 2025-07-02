@minLength(6)
param eventHubName string

resource eventHubNamespace 'Microsoft.EventHub/namespaces@2024-01-01' = {
  name: eventHubName
  location: resourceGroup().location
}

resource eventHub 'Microsoft.EventHub/namespaces/eventhubs@2024-01-01' = {
  parent: eventHubNamespace
  properties: {
    partitionCount: 32
  }
  name: 'logs'
}

resource authorizationRule 'Microsoft.EventHub/namespaces/authorizationRules@2024-01-01' = {
  name: eventHubName
  parent: eventHubNamespace
  properties: {
    rights: [
      'Manage'
      'Send'
      'Listen'
    ]
  }
}

output eventHubNamespaceName string = eventHubNamespace.name
output eventHubName string = eventHub.name
output eventHubAuthRuleId string = authorizationRule.id
