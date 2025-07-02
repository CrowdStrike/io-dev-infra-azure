![CrowdStrike Falcon](https://raw.githubusercontent.com/CrowdStrike/falconpy/main/docs/asset/cs-logo.png) [![Twitter URL](https://img.shields.io/twitter/url?label=Follow%20%40CrowdStrike&style=social&url=https%3A%2F%2Ftwitter.com%2FCrowdStrike)](https://twitter.com/CrowdStrike)<br/>

# LogScale Azure Integration

Push-based CrowdStrike Falcon LogScale integration for Azure. 

This repo contains a service for listening to an Azure Event Hub and pushing all events to LogScale, as well as Bicep scripts for setting up the necessary infrastructure in Azure, including sending Azure Activity Log data.

Be aware that this will incur additional costs from Azure. [Azure Pricing Calculator](https://azure.microsoft.com/pricing/calculator/)

### Prerequisites

* .NET 8.0 or later
* Azure Bicep CLI
* LogScale API token and endpoint URL

### Usage

#### Parameters for usage:

| Name           	| Description                                                                                                                                                       	| Optional? 	| Default Value  	|
|---------------------	|-------------------------------------------------------------------------------------------------------------------------------------------------------------------	|-----------	|----------------	|
| LogScale Endpoint   	| Endpoint of your LogScale server, e.g. `ops.us.humio.com`.                                                                                                        	| No        	|                	|
| Ingest Token        	| Ingest token from LogScale. [Section on ingest tokens in the LogScale documentation](https://library.humio.com/falcon-logscale-cloud/ingesting-data-tokens.html). 	| No        	|                	|
| Azure Location Name 	| Location name of Azure location. List available values with `az account list-locations -o table`.                                                                 	| Yes       	| eastus         	|
| Deployment Name     	| Name of deployment in Azure.                                                                                                                                      	| Yes       	| logscaleingest 	|

#### Simple deployment with mandatory arguments:

```bash
./deploy.sh logscale-endpoint ingest-token
```
Example:
```bash
./deploy.sh ops.us.humio.com 01234567-89ab-cdef-0123-456789abcdef
```

#### Deployment with optional arguments:

```
./deploy.sh logscale-endpoint ingest-token location deployment-name
```
Example with default values:
```bash
./deploy.sh ops.us.humio.com 01234567-89ab-cdef-0123-456789abcdef eastus logscaleingest
```
#### Further configuration:
A few other settings can be configured in [deploy.bicepparam](https://github.com/CrowdStrike/io-dev-infra-azure/blob/main/deploy.bicepparam).

### Removal

If you wish to remove the integration, simply delete the created resource group and the appropriate [diagnostic setting](https://learn.microsoft.com/en-us/azure/azure-monitor/essentials/activity-log).

## Getting Help

If you encounter any issues, you can create an issue on our [Github repo](https://github.com/CrowdStrike/io-dev-infra-azure) for bugs, enhancements, or other requests.

## Contributing

You can contribute by:

* Raising any issues you find during usage
* Fixing issues by opening [Pull Requests](https://github.com/CrowdStrike/io-dev-infra-azure/pulls)
* Improving documentation

All bugs, tasks or enhancements are tracked as [GitHub issues](https://github.com/CrowdStrike/io-dev-infra-azure/issues).

## Additional Resources

 - LogScale Introduction: [LogScale Beginner Introduction](https://library.humio.com/training/training-getting-started.html)
 - LogScale Training: [LogScale Overview](https://library.humio.com/training/training-fc.html)
 - More about Falcon LogScale: [Falcon LogScale Services](https://www.crowdstrike.com/services/falcon-logscale/)
