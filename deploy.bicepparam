using 'deployment/bicep/main.bicep'

param location = ''             // Leave empty, as it is set from the deployment script
param productName = 'logscale'
param feature = 'ingest'
param env = 'dev'               // Set this to your appropriate deployment environment

param rgName = '${productName}-${feature}-${env}'
param eventHubName = 'evh-${rgName}' 
