targetScope = 'subscription'

param eventHubName string
param eventHubAuthorizationRuleId string
param env string

var activityLogDiagnosticSettingsName = 'activity-log-${eventHubName}-${env}'

resource subscriptionActivityLog 'Microsoft.Insights/diagnosticSettings@2021-05-01-preview' = {
  name: activityLogDiagnosticSettingsName
  properties: {    
    eventHubName: eventHubName
    eventHubAuthorizationRuleId: eventHubAuthorizationRuleId
    logs: [
      {
        category: 'Administrative'
        enabled: true
      }
      {
        category: 'Security'
        enabled: true
      }
      {
        category: 'ServiceHealth'
        enabled: true
      }
      {
        category: 'Alert'
        enabled: true
      }
      {
        category: 'Recommendation'
        enabled: true
      }
      {
        category: 'Policy'
        enabled: true
      }
      {
        category: 'Autoscale'
        enabled: true
      }
      {
        category: 'ResourceHealth'
        enabled: true
      }
    ]
  }
}

output diagnosticName string = activityLogDiagnosticSettingsName
