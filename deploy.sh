#!/usr/bin/env bash
echo Push-based CrowdStrike Falcon LogScale integration for Azure.
echo https://github.com/CrowdStrike/io-dev-infra-azure
echo —-----------------------------------------------------------------------

HUMIO_URL=$1
HUMIO_API_KEY=$2
LOCATION="${3:-eastus}"

DEPLOYMENT_NAME="${4:-logscaleingest}$(date +%s)"

if [ -z "$1" ]; then
    echo "Error: Missing first argument, LogScale Endpoint URL"
    echo "Usage: ./deploy.sh logscale-endpoint ingest-token"
    exit 1
fi
if [ -z "$2" ]; then
    echo "Error: Missing second argument, LogScale Ingest Token"
    echo "Usage: ./deploy.sh logscale-endpoint ingest-token"
    exit 1
fi

echo "Deploying infrastructure"

az deployment sub create \
    -n $DEPLOYMENT_NAME \
    -p deploy.bicepparam \
    -p location=$LOCATION \
    -l $LOCATION > deploy_infrastructure.txt

if [ $? -ne 0 ]; then
    cat deploy_infrastructure.txt
    rm deploy_infrastructure.txt
    echo
    echo Deployment of infrastructure failed. See error output above for details.
    exit 1
fi

rm deploy_infrastructure.txt

CONTAINER_REGISTRY_NAME=$(az deployment sub show -n $DEPLOYMENT_NAME --query properties.outputs.containerRegistryName.value --output tsv) 
STORAGE_ACCOUNT_NAME=$(az deployment sub show -n $DEPLOYMENT_NAME --query properties.outputs.storageAccountName.value --output tsv) 
EVENT_HUB_NAME=$(az deployment sub show -n $DEPLOYMENT_NAME --query properties.outputs.eventHubName.value --output tsv)
RESOURCE_GROUP_NAME=$(az deployment sub show -n $DEPLOYMENT_NAME --query properties.outputs.resourceGroupName.value --output tsv)
DIAGNOSTIC_NAME=$(az deployment sub show -n $DEPLOYMENT_NAME --query properties.outputs.diagnosticName.value --output tsv)

IMAGE_NAME="img-cont-"$RESOURCE_GROUP_NAME

echo "Building worker image"

az acr build -r $CONTAINER_REGISTRY_NAME -t $IMAGE_NAME AzureLogScaleIntegration/EventProcessorWorker/. > build_docker.txt

if [ $? -ne 0 ]; then
    cat build_docker.txt
    rm build_docker.txt
    echo
    echo Building docker image failed. See error output above for details.
    exit 1
fi

rm build_docker.txt

echo
echo "Deploying container app"
az deployment group create \
    -n $DEPLOYMENT_NAME \
    -f deployment/bicep/containerapp.bicep \
    -g $RESOURCE_GROUP_NAME \
    -p containerRegistryName=$CONTAINER_REGISTRY_NAME storageAccountName=$STORAGE_ACCOUNT_NAME eventHubName=$EVENT_HUB_NAME humioUrl=$HUMIO_URL humioApiKey=$HUMIO_API_KEY imageName=$IMAGE_NAME > deploy_app.txt

if [ $? -ne 0 ]; then    
    cat deploy_app.txt
    rm deploy_app.txt
    echo
    echo Deployment of container app failed. See error output above for details.
    exit 1
fi

rm deploy_app.txt

CONTAINER_APP_NAME=$(az deployment group show -g $RESOURCE_GROUP_NAME -n $DEPLOYMENT_NAME --query properties.outputs.containerAppName.value --output tsv)

echo
echo "Container Registry: $CONTAINER_REGISTRY_NAME"
echo "Storage Account:    $STORAGE_ACCOUNT_NAME"
echo "Event Hub:          $EVENT_HUB_NAME"
echo "Container App Name: $CONTAINER_APP_NAME"
echo
echo "Deployment complete. You can now direct traffic to the Event Hub. Azure Activity Logs have automatically been enabled."
echo
echo "To delete this deployment, run the following two commands:"
echo az group delete -n $RESOURCE_GROUP_NAME 
echo az monitor diagnostic-settings subscription delete -n $DIAGNOSTIC_NAME
echo
