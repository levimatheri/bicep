// $1 = logicApp
// $2 = 'name'
// $3 = location

param location string

resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: 'name'
  location: location
  properties: {
    definition: {
      '$schema': 'https://schema.management.azure.com/schemas/2016-06-01/Microsoft.Logic.json'
//@[6:15) [prefer-unquoted-property-names (Warning)] Property names that are valid identifiers should be declared without quotation marks and accessed using dot notation. (bicep core linter https://aka.ms/bicep/linter/prefer-unquoted-property-names) |'$schema'|
      contentVersion: '1.0.0.0'
    }
  }
}


