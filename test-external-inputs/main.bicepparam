using 'main.bicep'

param location = 'eastus'
param appName = json(externalInput('ev2.scopeBinding', 'BINDING'))
