// $1 = appInsightsAlertRules
// $2 = 'name'
// $3 = location
// $4 = 'name'
// $5 = 'description'
// $6 = 3
// $7 = Microsoft.Azure.Management.Insights.Models.LocationThresholdRuleCondition
// $8 = Microsoft.Azure.Management.Insights.Models.RuleManagementEventDataSource
// $9 = 'resourceUri'
// $10 = 'windowSize'
// $11 = Microsoft.Azure.Management.Insights.Models.RuleEmailAction

param location string

resource appInsightsAlertRules 'Microsoft.Insights/alertrules@2016-03-01' = {
  name: 'name'
  location: location
  properties: {
    name: 'name'
    description: 'description'
    isEnabled: false
    condition: {
      failedLocationCount: 3
      'odata.type': 'Microsoft.Azure.Management.Insights.Models.LocationThresholdRuleCondition'
//@[6:18) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'odata.type'|
      dataSource: {
        'odata.type': 'Microsoft.Azure.Management.Insights.Models.RuleManagementEventDataSource'
//@[8:20) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'odata.type'|
        resourceUri: 'resourceUri'
      }
      windowSize: 'windowSize'
    }
    action: {
      'odata.type': 'Microsoft.Azure.Management.Insights.Models.RuleEmailAction'
//@[6:18) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'odata.type'|
      sendToServiceOwners: true
    }
  }
}


